#!/usr/bin/env python3
"""Dead-data audit: gameplay config keys (resources/config/*.json) versus the C# that reads them.

Run from anywhere (read-only, standard library only):

    python3 -I tools/audit/config_keys.py            # markdown report
    python3 -I tools/audit/config_keys.py --tsv      # one TSV row per finding
    python3 -I tools/audit/config_keys.py --root /path/to/carbon

How it works
------------
1. DTO mode. Every ``[JsonSerializable(typeof(T))]`` in non-generated C# is a candidate root. Each JSON file is
   matched to the root whose properties best cover the file's top-level keys (array roots use the first element).
   The JSON tree and the C# type graph are then walked together. JSON names come from the
   ``JsonKnownNamingPolicy.CamelCase`` policy (System.Text.Json algorithm) or ``[JsonPropertyName]``. Lists, arrays
   and dictionaries are unwrapped; dictionary keys are data, not property names.
2. Manual mode. Files with no matching root are parsed by hand (``JsonDocument``/``JsonElement``). Their keys are
   checked against string literals passed to ``GetProperty``/``TryGetProperty`` or ``Helper(element, "name", ...)``
   calls in C# files that parse JSON and mention the document name.
3. Reads. A DTO property is "read" when non-generated, non-test C# contains ``.Name`` that is not an assignment,
   a property pattern ``Name:``, or a bare ``Name`` inside the declaring type (not a declaration, ``Name =`` or
   ``nameof``). Literal names used by any manual JSON parser also count as reads.

Findings
--------
* ``A-unmapped``     JSON key with no DTO property / no parser literal (ignored; DTO mode would throw on load,
                    because every context uses ``UnmappedMemberHandling.Disallow``).
* ``B-unread``       DTO property that is deserialized from JSON but never read by game code.
* ``C-default``      DTO property (or manual-parser literal) with no key in any JSON instance (always default).

Limits: reads are matched by member name, not by type, so a common name (``Id``, ``Name``) read on another type
hides a dead one (false negatives, never false positives from that cause). Properties set only through
positional record constructors are not modelled.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass, field

SOURCE_DIRS = ("engine", "server", "desktop", "graphics", "wasm", "contracts")
COLLECTION_WRAPPERS = {
    "IReadOnlyList", "List", "IList", "ICollection", "IReadOnlyCollection", "IEnumerable",
    "ImmutableArray", "ImmutableList", "HashSet", "IReadOnlySet", "ISet", "FrozenSet",
}
VERSION_KEYS = {"version", "schemaVersion"}
MAP_WRAPPERS = {"Dictionary", "IReadOnlyDictionary", "IDictionary", "ImmutableDictionary", "FrozenDictionary",
                "SortedDictionary"}


@dataclass
class Prop:
    name: str
    type_text: str
    json_name: str
    owner: str
    file: str
    line: int


@dataclass
class CsType:
    name: str
    file: str
    body: str
    body_offset: int
    props: dict[str, Prop] = field(default_factory=dict)


@dataclass
class Finding:
    kind: str
    json_file: str
    path: str
    member: str
    where: str
    evidence: str


def camel_case(name: str) -> str:
    """System.Text.Json JsonNamingPolicy.CamelCase."""
    chars = list(name)
    for i in range(len(chars)):
        if i == 1 and not chars[i].isupper():
            break
        has_next = i + 1 < len(chars)
        if i > 0 and has_next and not chars[i + 1].isupper():
            if chars[i + 1] == " ":
                chars[i] = chars[i].lower()
            break
        chars[i] = chars[i].lower()
    return "".join(chars)


def strip_comments(text: str) -> str:
    """Blank out // and /* */ comments while keeping strings and line numbers intact."""
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j]))
            i = j
        elif c == '"':
            verbatim = i > 0 and text[i - 1] == "@"
            j = i + 1
            while j < n:
                if not verbatim and text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    if verbatim and j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    break
                if text[j] == "\n" and not verbatim:
                    break
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        elif c == "'" and i + 2 < n:
            j = i + 1
            if text[j] == "\\":
                j += 1
            j += 1
            if j < n and text[j] == "'":
                out.append(text[i:j + 1])
                i = j + 1
            else:
                out.append(c)
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def is_generated(path: str) -> bool:
    parts = path.replace("\\", "/").split("/")
    return ("obj" in parts or "bin" in parts or path.endswith((".g.cs", ".Designer.cs", ".generated.cs")))


def load_sources(root: str) -> dict[str, str]:
    files: dict[str, str] = {}
    for d in SOURCE_DIRS:
        base = os.path.join(root, d)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [x for x in dirnames if x not in ("obj", "bin", "node_modules")]
            for f in filenames:
                p = os.path.join(dirpath, f)
                rel = os.path.relpath(p, root)
                if f.endswith(".cs") and not is_generated(rel) and "/tests/" not in "/" + rel and \
                        not re.search(r"(^|/)[^/]*\.Tests?/", rel):
                    with open(p, encoding="utf-8", errors="replace") as fh:
                        files[rel] = strip_comments(fh.read())
    return files


TYPE_DECL = re.compile(r"\b(?:class|struct|record(?:\s+struct|\s+class)?)\s+(\w+)\b[^;{]*\{")
PROP_DECL = re.compile(
    r"(?:\[JsonPropertyName\(\s*\"([^\"]+)\"\s*\)\]\s*)?"
    r"public\s+(?:required\s+|virtual\s+|override\s+|new\s+)*"
    r"([\w.]+(?:<[^{};=()]*?>)?(?:\[\])*\??)\s+(\w+)\s*\{\s*get;\s*(?:set|init|private\s+set|internal\s+set)?;?")


def match_brace(text: str, open_idx: int) -> int:
    depth = 0
    for i in range(open_idx, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return i
    return len(text) - 1


def top_level(body: str) -> str:
    """Blank out nested braces so only depth-1 members of a type body remain."""
    out, depth = [], 0
    for c in body:
        if c == "{":
            depth += 1
            out.append(c if depth == 1 else " ")
        elif c == "}":
            out.append(c if depth == 1 else " ")
            depth -= 1
        else:
            out.append(c if depth <= 1 or c == "\n" else " ")
    return "".join(out)


def index_types(sources: dict[str, str]) -> dict[str, list[CsType]]:
    types: dict[str, list[CsType]] = {}
    for rel, text in sources.items():
        for m in TYPE_DECL.finditer(text):
            open_idx = m.end() - 1
            close_idx = match_brace(text, open_idx)
            body = text[open_idx:close_idx + 1]
            t = CsType(m.group(1), rel, body, open_idx)
            for pm in PROP_DECL.finditer(body):
                if depth_at(body, pm.start()) != 1:
                    continue
                json_name = pm.group(1) or camel_case(pm.group(3))
                line = text.count("\n", 0, open_idx + pm.start()) + 1
                t.props[pm.group(3)] = Prop(pm.group(3), pm.group(2), json_name, t.name, rel, line)
            types.setdefault(t.name, []).append(t)
    return types


def depth_at(body: str, idx: int) -> int:
    return body.count("{", 0, idx) - body.count("}", 0, idx)


def split_generic(text: str) -> tuple[str, list[str]]:
    text = text.strip().rstrip("?")
    if text.endswith("[]"):
        return "[]", [text[:-2]]
    if "<" not in text:
        return text.split(".")[-1], []
    head, inner = text.split("<", 1)
    inner = inner[: inner.rfind(">")]
    args, depth, cur = [], 0, ""
    for c in inner:
        if c == "<":
            depth += 1
        elif c == ">":
            depth -= 1
        if c == "," and depth == 0:
            args.append(cur.strip())
            cur = ""
        else:
            cur += c
    args.append(cur.strip())
    return head.split(".")[-1], args


def resolve(types: dict[str, list[CsType]], name: str, near: str) -> CsType | None:
    cands = [t for t in types.get(name, []) if t.props]
    if not cands:
        return None
    top = near.split("/")[:2]
    cands.sort(key=lambda t: (t.file.split("/")[:2] != top, len(t.file)))
    return cands[0]


def innermost(type_text: str) -> str:
    head, args = split_generic(type_text)
    return innermost(args[-1]) if args else head


class Walker:
    def __init__(self, types: dict[str, list[CsType]]):
        self.types = types
        self.seen: set[tuple[str, str]] = set()       # (owner file::type, prop)
        self.reachable: dict[tuple[str, str], CsType] = {}
        self.findings: list[Finding] = []
        self.parents: dict[str, set[str]] = {}       # type name -> property names that hold it
        self.json_file = ""

    def walk(self, value, type_text: str, near: str, path: str) -> None:
        head, args = split_generic(type_text)
        if head == "[]" or head in COLLECTION_WRAPPERS:
            if isinstance(value, list):
                for v in value:
                    self.walk(v, args[0], near, path + "[]")
            else:
                self.mark_reachable(args[0], near)
            return
        if head in MAP_WRAPPERS:
            if isinstance(value, dict):
                for v in value.values():
                    self.walk(v, args[1], near, path + ".*")
            else:
                self.mark_reachable(args[1], near)
            return
        t = resolve(self.types, head, near)
        if t is None:
            return
        self.mark_reachable(head, near)
        if not isinstance(value, dict):
            return
        by_json = {p.json_name: p for p in t.props.values()}
        for key, v in value.items():
            p = by_json.get(key)
            if p is None:
                self.findings.append(Finding("A-unmapped", self.json_file, f"{path}.{key}".lstrip("."), key,
                                             f"{t.file} ({t.name})", "no property maps to this JSON name"))
                continue
            self.seen.add((t.file + "::" + t.name, p.name))
            self.walk(v, p.type_text, t.file, f"{path}.{key}".lstrip("."))

    def mark_reachable(self, type_text: str, near: str) -> None:
        head, args = split_generic(type_text)
        if head == "[]" or head in COLLECTION_WRAPPERS or head in MAP_WRAPPERS:
            if args:
                self.mark_reachable(args[-1], near)
            return
        t = resolve(self.types, head, near)
        if t is None:
            return
        key = (t.file + "::" + t.name, "")
        if key in self.reachable:
            return
        self.reachable[key] = t
        for p in t.props.values():
            self.parents.setdefault(innermost(p.type_text), set()).add(p.name)
            self.mark_reachable(p.type_text, t.file)


def read_count(prop: Prop, owner: CsType, sources: dict[str, str], manual_literals: set[str],
               scope: set[str] | None = None) -> tuple[int, str]:
    name = re.escape(prop.name)
    member = re.compile(r"\.\s*" + name + r"\b(?!\s*(?:=(?![=>])|\+=|-=|\?\?=))")
    pattern = re.compile(r"[{,]\s*" + name + r"\s*:(?!:)")
    hits, first = 0, ""
    for rel, text in sources.items():
        if prop.name not in text or (scope is not None and rel not in scope):
            continue
        n = len(member.findall(text)) + len(pattern.findall(text))
        if n and not first:
            m = member.search(text) or pattern.search(text)
            first = f"{rel}:{text.count(chr(10), 0, m.start()) + 1}"
        hits += n
    body = owner.body
    bare = re.compile(r"(?<![\w.])" + name + r"\b(?!\s*(?:=(?![=>])|\{\s*get))")
    for m in bare.finditer(body):
        before = body[max(0, m.start() - 8):m.start()]
        if "nameof(" in before:
            continue
        line_start = body.rfind("\n", 0, m.start()) + 1
        line = body[line_start:body.find("\n", m.start())]
        if re.search(r"public\s.*\b" + name + r"\s*\{", line):
            continue
        hits += 1
        if not first:
            first = f"{owner.file}:{owner.file and sources[owner.file].count(chr(10), 0, owner.body_offset + m.start()) + 1}"
    if prop.json_name in manual_literals:
        hits += 1
        first = first or "manual JSON parser literal"
    return hits, first


MANUAL_CALL = re.compile(r"(?:GetProperty|TryGetProperty)\(\s*\"(\w+)\"|\(\s*\w+\s*,\s*\"([a-zA-Z]\w*)\"")
DOC_NAME = re.compile(r"\"(\w+)(?:\.json)?\"")


def load_tests(root: str) -> dict[str, str]:
    out: dict[str, str] = {}
    for dirpath, dirnames, filenames in os.walk(os.path.join(root, "tests")):
        dirnames[:] = [x for x in dirnames if x not in ("obj", "bin")]
        for f in filenames:
            if f.endswith(".cs"):
                with open(os.path.join(dirpath, f), encoding="utf-8", errors="replace") as fh:
                    out[os.path.relpath(os.path.join(dirpath, f), root)] = fh.read()
    return out


def test_note(prop: Prop, tests: dict[str, str]) -> str:
    rx = re.compile(r"\.\s*" + re.escape(prop.name) + r"\b")
    n = sum(len(rx.findall(t)) for t in tests.values() if prop.name in t)
    return f"; {n} read(s) in tests/ only" if n else "; not read in tests either"


def manual_parsers(sources: dict[str, str]) -> dict[str, set[str]]:
    out: dict[str, set[str]] = {}
    for rel, text in sources.items():
        if "JsonElement" not in text or "Canonicalizer" in rel:
            continue
        out[rel] = {a or b for a, b in MANUAL_CALL.findall(text)}
    return out


def json_keys(value, path: str, acc: dict[str, set[str]], enumerates: bool = False) -> None:
    if isinstance(value, dict):
        if enumerates and len(value) >= 2 and all(isinstance(v, dict) for v in value.values()):
            for v in value.values():  # parser walks EnumerateObject(): child names are data, not schema
                json_keys(v, path + ".*", acc, enumerates)
            return
        for k, v in value.items():
            acc.setdefault(k, set()).add(path)
            json_keys(v, f"{path}.{k}".lstrip("."), acc, enumerates)
    elif isinstance(value, list):
        for v in value:
            json_keys(v, path + "[]", acc, enumerates)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--root", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    ap.add_argument("--tsv", action="store_true")
    args = ap.parse_args()
    root = args.root
    sources = load_sources(root)
    types = index_types(sources)
    roots: list[tuple[str, str]] = []  # (type text, declaring file)
    for rel, text in sources.items():
        for m in re.finditer(r"\[JsonSerializable\(typeof\(([^)]+)\)\)\]", text):
            roots.append((m.group(1), rel))
    parsers = manual_parsers(sources)
    manual_literals = set().union(*parsers.values()) if parsers else set()

    cfg_dir = os.path.join(root, "resources", "config")
    walker = Walker(types)
    findings: list[Finding] = []
    mapping: list[tuple[str, str]] = []
    for fname in sorted(os.listdir(cfg_dir)):
        if not fname.endswith(".json"):
            continue
        with open(os.path.join(cfg_dir, fname), encoding="utf-8") as fh:
            doc = json.load(fh)
        sample = doc[0] if isinstance(doc, list) and doc else doc
        top = set(sample.keys()) if isinstance(sample, dict) else set()
        top -= VERSION_KEYS  # every document has one; it must not decide the match
        best, best_score = None, 0.0
        for type_text, rel in roots:
            head, targs = split_generic(type_text)
            is_array = head == "[]" or head in COLLECTION_WRAPPERS
            if is_array != isinstance(doc, list):
                continue
            t = resolve(types, targs[0] if is_array else head, rel)
            if t is None or not top:
                continue
            names = {p.json_name for p in t.props.values()}
            score = len(top & names) / len(top)
            if score > best_score:
                best, best_score = (type_text, rel), score
        if best and best_score >= 0.5:
            mapping.append((fname, f"DTO {best[0]} ({best_score:.0%} top-level match)"))
            walker.json_file = fname
            walker.walk(doc, best[0], best[1], "")
            continue
        stem = fname[:-5]
        owners = [rel for rel, text in sources.items() if rel in parsers and
                  (f'"{stem}"' in text or f"{stem}.json" in text)]
        mapping.append((fname, "manual parser: " + (", ".join(owners) or "NONE FOUND")))
        lits = set().union(*(parsers[o] for o in owners)) if owners else set()
        keys: dict[str, set[str]] = {}
        json_keys(doc, "", keys, any("EnumerateObject" in sources[o] for o in owners))
        for k in sorted(keys):
            if k not in lits:
                findings.append(Finding("A-unmapped", fname, ", ".join(sorted(f"{p}.{k}".lstrip(".") for p in keys[k]))[:120],
                                        k, ", ".join(owners) or "-", "no GetProperty/TryGetProperty literal in parser"))
        for lit in sorted(lits - set(keys)):
            findings.append(Finding("C-default", fname, "-", lit, ", ".join(owners),
                                    "parser reads literal; JSON never sets it (or literal is not a key)"))
    findings[:0] = walker.findings

    # B and C for DTO properties
    declared_count: dict[str, int] = {}
    for tlist in types.values():
        for t in tlist:
            for n in t.props:
                declared_count[n] = declared_count.get(n, 0) + 1
    tests = load_tests(root)
    for (tkey, _), t in sorted(walker.reachable.items()):
        for p in t.props.values():
            if (tkey, p.name) in walker.seen:
                hits, first = read_count(p, t, sources, manual_literals)
                if hits == 0:
                    findings.append(Finding("B-unread", "", p.json_name, f"{t.name}.{p.name}", f"{p.file}:{p.line}",
                                            "deserialized, 0 read sites outside declaration/tests" + test_note(p, tests)))
                elif declared_count.get(p.name, 0) > 1:
                    # Name shared with other types: re-count only in files that can see this type.
                    hints = {t.name} | {"." + n for n in walker.parents.get(t.name, set())}
                    scope = {rel for rel, text in sources.items() if any(h in text for h in hints)}
                    scoped, _ = read_count(p, t, sources, manual_literals, scope)
                    if scoped == 0:
                        findings.append(Finding("B-unread?", "", p.json_name, f"{t.name}.{p.name}",
                                                f"{p.file}:{p.line}",
                                                f"name shared by {declared_count[p.name]} types; {hits} global hit(s), "
                                                f"0 in files that mention {t.name} or its holder property" +
                                                test_note(p, tests)))
            else:
                findings.append(Finding("C-default", "", p.json_name, f"{t.name}.{p.name}", f"{p.file}:{p.line}",
                                        "no JSON key in any config instance"))

    if args.tsv:
        print("kind\tjson_file\tjson_path\tmember\twhere\tevidence")
        for f in findings:
            print("\t".join((f.kind, f.json_file, f.path, f.member, f.where, f.evidence)))
        return 0
    print("# Config key audit\n\n## Document mapping\n\n| file | parsed by |\n|---|---|")
    for fname, how in mapping:
        print(f"| {fname} | {how} |")
    for kind, title in (("A-unmapped", "(a) JSON keys with no C# consumer"),
                        ("B-unread", "(b) DTO properties deserialized but never read"),
                        ("B-unread?", "(b?) Likely unread: shared member name, no read where the type is visible"),
                        ("C-default", "(c) C# members with no JSON key (always default)")):
        rows = [f for f in findings if f.kind == kind]
        print(f"\n## {title} — {len(rows)}\n\n| json file | json path | member | where | evidence |\n|---|---|---|---|---|")
        for f in rows:
            print(f"| {f.json_file or '-'} | `{f.path}` | `{f.member}` | {f.where} | {f.evidence} |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
