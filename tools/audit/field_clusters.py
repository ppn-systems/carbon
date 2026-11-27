#!/usr/bin/env python3
"""Field-usage clustering for C# (partial) classes -- responsibility audit helper.

For every member (method, constructor, property, nested type) of one class, spread
over any number of partial files, list the private instance fields it references,
then group members into clusters that share non-hub fields. Clusters are concern
candidates; "hub" fields (used by many members, e.g. a lock or the player registry)
are reported separately as the coupling that any extraction would have to pass in.

This is a lexical tool (no Roslyn): comments and string literals are blanked, braces
are counted, and a field is "referenced" when its identifier appears as a whole word.
Good enough for responsibility clustering; not a compiler.

Usage (read-only; writes only to stdout):

    python3 -I tools/audit/field_clusters.py <ClassName> <file.cs> [<file.cs> ...]
        [--hub 0.25]      fields used by more than this fraction of members are hubs
        [--min-shared 1]  non-hub fields two members must share to be clustered
        [--props]         also treat settable auto-properties as state
        [--wide 10]       members touching more than this many fields (constructors,
                          tick/update dispatchers) are reported as ORCHESTRATORS and
                          left out of clustering, so they do not glue every concern together

Example:

    python3 -I tools/audit/field_clusters.py AuthoritativeWorld \
        server/Carbon.Application/World/AuthoritativeWorld*.cs

Output sections: FIELDS (declaring file:line), MEMBERS (file:start-end, fields,
W = written in that member), FIELD INDEX (field -> members), CLUSTERS.
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import sys
from collections import defaultdict

TYPE_KEYWORDS = ("class", "struct", "record", "interface", "enum")
WRITE_SUFFIX = re.compile(r"\s*(=(?!=)|\+=|-=|\*=|/=|\|=|&=|\?\?=|\+\+|--)")
MUTATING_CALL = re.compile(
    r"\s*\.\s*(Add|AddRange|Clear|Remove|RemoveAt|RemoveAll|Insert|Enqueue|Dequeue|"
    r"TryAdd|TryRemove|TryDequeue|Push|Pop|Set|Reset|Return)\s*\(")
IDENT = re.compile(r"[A-Za-z_]\w*")


def blank_comments_and_strings(src: str) -> str:
    """Replace comments and string/char literal contents with spaces, keeping newlines."""
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif c == "/" and nxt == "*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", src[i:j]))
            i = j
        elif c == '"' and src.startswith('"""', i):
            j = src.find('"""', i + 3)
            j = n if j < 0 else j + 3
            out.append(re.sub(r"[^\n]", " ", src[i:j]))
            i = j
        elif c == '"' or (c in "@$" and nxt == '"') or (c in "@$" and nxt in "@$" and src[i + 2:i + 3] == '"'):
            # Regular, verbatim or interpolated string. Interpolation holes are blanked too
            # (field refs inside $"{_x}" are rare and only affect formatting code).
            start = i
            verbatim = "@" in src[i:i + 3].split('"')[0]
            i = src.index('"', i) + 1
            while i < n:
                if verbatim and src.startswith('""', i):
                    i += 2
                    continue
                if not verbatim and src[i] == "\\":
                    i += 2
                    continue
                if src[i] == '"':
                    i += 1
                    break
                i += 1
            out.append(re.sub(r"[^\n]", " ", src[start:i]))
        elif c == "'":
            m = re.match(r"'(\\.|[^\\'])+'", src[i:])
            if m:
                out.append(" " * len(m.group(0)))
                i += len(m.group(0))
            else:
                out.append(c)
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def line_of(text: str, pos: int) -> int:
    return text.count("\n", 0, pos) + 1


def member_name(header: str) -> str:
    h = re.sub(r"\[[^\]]*\]", " ", header)  # attributes
    h = " ".join(h.split())
    for kw in TYPE_KEYWORDS:
        m = re.search(rf"\b{kw}\s+([A-Za-z_]\w*)", h)
        if m and "(" not in h.split(m.group(0))[0]:
            return f"<{kw} {m.group(1)}>"
    # The method name is the last identifier that opens a top-level parameter list, so tuple
    # return types such as "static (int X, int Y) Unpack(long k)" resolve to "Unpack".
    depth, angle, name = 0, 0, None
    for i, ch in enumerate(h):
        if ch == "<":
            angle += 1
        elif ch == ">":
            angle -= 1
        elif ch == "(":
            if depth == 0 and angle == 0:
                before = re.sub(r"<[^<>]*>$", "", h[:i].rstrip()).rstrip()  # generic method args
                m = re.search(r"([A-Za-z_]\w*)$", before)
                if m and m.group(1) not in ("static", "async", "readonly", "unsafe", "new", "override",
                                            "virtual", "private", "public", "internal", "protected"):
                    name = m.group(1)
            depth += 1
        elif ch == ")":
            depth -= 1
    if name:
        return name + "()"
    if "this[" in h:
        return "this[]"
    m = re.search(r"([A-Za-z_]\w*)\s*(=>|$)", h)
    return m.group(1) if m else h[:40]


def parse_class(path: str, class_name: str):
    """Yield (kind, name, start_line, end_line, body_text, header) for the class's members."""
    raw = open(path, encoding="utf-8-sig").read()
    text = blank_comments_and_strings(raw)
    m = re.search(rf"\bpartial\s+class\s+{class_name}\b|\bclass\s+{class_name}\b", text)
    if not m:
        return []
    open_brace = text.index("{", m.end())
    members = []
    depth = 0
    i = open_brace + 1
    seg_start = i
    n = len(text)
    paren = 0
    while i < n:
        c = text[i]
        if c == "(":
            paren += 1
        elif c == ")":
            paren -= 1
        elif c == "{" and paren == 0:
            if depth == 0 and "=>" in text[seg_start:i]:
                pass  # counted below: brace inside an expression body, e.g. "=> x is { A: true };"
            elif depth == 0:
                header = text[seg_start:i]
                # find matching brace
                d, j = 1, i + 1
                while j < n and d:
                    if text[j] == "{":
                        d += 1
                    elif text[j] == "}":
                        d -= 1
                    j += 1
                # property/field with initializer continues to ';' (e.g. "{ get; } = x;")
                end = j
                tail = re.match(r"\s*(=[^;]*;)", text[j:])
                if tail:
                    end = j + tail.end()
                hs = header.strip()
                if hs:
                    start_pos = seg_start + (len(header) - len(header.lstrip()))
                    members.append((hs, start_pos, end, text[seg_start:end]))
                i = end
                seg_start = i
                continue
            depth += 1
        elif c == "}" and paren == 0:
            if depth == 0:
                break  # end of class
            depth -= 1
        elif c == ";" and depth == 0 and paren == 0:
            seg = text[seg_start:i + 1]
            hs = seg.strip()
            if hs:
                start_pos = seg_start + (len(seg) - len(seg.lstrip()))
                members.append((hs, start_pos, i + 1, seg))
            seg_start = i + 1
        i += 1

    out = []
    for header, s, e, body in members:
        sig = header.split("{")[0].split("=>")[0]
        if header.endswith(";") and re.match(r"^[^=]*?\s(_[A-Za-z]\w*)\s*(=(?!>)|;|,)", " ".join(header.split())):
            kind = "field"  # incl. tuple-typed fields such as List<(int A, int B)> _x
        elif any(re.search(rf"\b{kw}\b", sig) for kw in TYPE_KEYWORDS) and "(" not in sig.split("class")[0]:
            kind = "type"
        elif "(" in sig and not re.search(r"(?<![=!<>])=(?!=|>)", sig.split("(")[0]):
            kind = "method"
        elif header.endswith(";") and "=>" not in sig and "{" not in header:
            kind = "field"
        else:
            kind = "property"
        out.append((kind, member_name(sig) if kind != "field" else "<field>",
                    line_of(text, s), line_of(text, e - 1), body, header))
    return out


FIELD_DECL = re.compile(
    r"^(?:\[[^\]]*\]\s*)*(?:(?:private|protected|internal|public|static|readonly|volatile|required)\s+)*"
    r"[\w<>\[\],.?() ]+?\s+(_[A-Za-z]\w*)\s*(?:=|;|,)")
PROP_DECL = re.compile(r"\b([A-Z]\w*)\s*\{\s*(?:get;\s*)?(?:private\s+|internal\s+|protected\s+)?(?:set|init);")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("class_name")
    ap.add_argument("files", nargs="+")
    ap.add_argument("--hub", type=float, default=0.25)
    ap.add_argument("--min-shared", type=int, default=1)
    ap.add_argument("--props", action="store_true")
    ap.add_argument("--wide", type=int, default=10)
    args = ap.parse_args()

    files = sorted({f for pat in args.files for f in (glob.glob(pat) or [pat])})
    parsed = {f: parse_class(f, args.class_name) for f in files}

    fields: dict[str, str] = {}
    for f, mems in parsed.items():
        for kind, name, s, e, body, header in mems:
            if kind == "field":
                for fm in re.finditer(r"(_[A-Za-z]\w*)\s*(?:=|;|,)", header):
                    if FIELD_DECL.match(" ".join(header.split())) or "," in header:
                        fields.setdefault(fm.group(1), f"{os.path.basename(f)}:{s}")
                    break
            elif kind == "property" and args.props:
                pm = PROP_DECL.search(" ".join(body.split()))
                if pm:
                    fields.setdefault(pm.group(1), f"{os.path.basename(f)}:{s} (prop)")

    rows = []  # (file, name, s, e, refs:set, writes:set)
    for f, mems in parsed.items():
        for kind, name, s, e, body, header in mems:
            if kind == "field":
                continue
            refs, writes = set(), set()
            for fld in fields:
                for m in re.finditer(rf"(?<![\w.]){re.escape(fld)}\b|(?<=\.){re.escape(fld)}\b", body):
                    refs.add(fld)
                    after = body[m.end():m.end() + 40]
                    if WRITE_SUFFIX.match(after) or MUTATING_CALL.match(after) or \
                            re.search(r"(\+\+|--|\bref\s+|\bout\s+)\s*$", body[max(0, m.start() - 6):m.start()]):
                        writes.add(fld)
            if kind == "property" and args.props and name in fields:
                refs.discard(name)
                writes.discard(name)
            rows.append((os.path.basename(f), name, s, e, refs, writes))

    print(f"# {args.class_name}: {len(files)} files, {len(fields)} fields, {len(rows)} members\n")
    print("## FIELDS")
    for fld, where in sorted(fields.items(), key=lambda kv: kv[1]):
        print(f"  {fld:40s} {where}")

    print("\n## MEMBERS (file:lines  name  [fields; *=written])")
    for f, name, s, e, refs, writes in rows:
        tag = ", ".join(sorted((r + "*" if r in writes else r) for r in refs))
        print(f"  {f}:{s}-{e}  {name}  [{tag}]")

    users: dict[str, list[str]] = defaultdict(list)
    for f, name, s, e, refs, writes in rows:
        for r in refs:
            users[r].append(f"{name}@{f}:{s}")
    n_members = max(1, sum(1 for r in rows if r[4]))
    hubs = {fld for fld, us in users.items() if len(us) / n_members > args.hub}

    print("\n## FIELD INDEX (field -> members; H = hub)")
    for fld in sorted(fields, key=lambda x: -len(users.get(x, []))):
        us = users.get(fld, [])
        print(f"  {'H' if fld in hubs else ' '} {fld} ({len(us)}): {', '.join(us) if us else 'UNUSED'}")

    # Union-find on members sharing >= min_shared non-hub fields.
    parent = list(range(len(rows)))

    def find(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    wide = {idx for idx, r in enumerate(rows) if len(r[4]) > args.wide}
    print(f"\n## ORCHESTRATORS (> {args.wide} fields; excluded from clustering)")
    for idx in sorted(wide):
        f, name, s, e, refs, _ = rows[idx]
        print(f"    {f}:{s}-{e} {name} ({len(refs)} fields, {e - s + 1} lines)")

    by_field: dict[str, list[int]] = defaultdict(list)
    for idx, r in enumerate(rows):
        if idx in wide:
            continue
        for fld in r[4] - hubs:
            by_field[fld].append(idx)
    if args.min_shared <= 1:
        for idxs in by_field.values():
            for other in idxs[1:]:
                parent[find(other)] = find(idxs[0])
    else:
        for a in range(len(rows)):
            for b in range(a + 1, len(rows)):
                if a in wide or b in wide:
                    continue
                if len((rows[a][4] & rows[b][4]) - hubs) >= args.min_shared:
                    parent[find(b)] = find(a)

    clusters: dict[int, list[int]] = defaultdict(list)
    for idx, r in enumerate(rows):
        if r[4] - hubs and idx not in wide:
            clusters[find(idx)].append(idx)
    print(f"\n## CLUSTERS (non-hub shared fields; hubs = {', '.join(sorted(hubs)) or 'none'})")
    for k, (root, idxs) in enumerate(sorted(clusters.items(), key=lambda kv: -len(kv[1])), 1):
        own = set().union(*(rows[i][4] - hubs for i in idxs))
        hub_use = set().union(*(rows[i][4] & hubs for i in idxs))
        print(f"\n### C{k}: {len(idxs)} members, {len(own)} fields")
        print(f"  fields: {', '.join(sorted(own))}")
        print(f"  hubs touched: {', '.join(sorted(hub_use)) or '-'}")
        for i in idxs:
            f, name, s, e, refs, writes = rows[i]
            print(f"    {f}:{s}-{e} {name}")
    hub_only = [r for r in rows if r[4] and not (r[4] - hubs)]
    stateless = [r for r in rows if not r[4]]
    print(f"\n## HUB-ONLY members ({len(hub_only)})")
    for f, name, s, e, refs, _ in hub_only:
        print(f"    {f}:{s}-{e} {name} [{', '.join(sorted(refs))}]")
    print(f"\n## NO-FIELD members ({len(stateless)}): " + ", ".join(f"{r[1]}@{r[0]}:{r[2]}" for r in stateless))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BrokenPipeError:  # piped into head/less
        sys.stderr.close()
        sys.exit(0)
