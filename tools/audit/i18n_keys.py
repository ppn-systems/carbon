#!/usr/bin/env python3
"""Dead-data audit: localization keys (resources/localization/<locale>.ini) versus the C# that looks them up.

Run (read-only, standard library only):

    python3 -I tools/audit/i18n_keys.py              # markdown report
    python3 -I tools/audit/i18n_keys.py --tsv        # one TSV row per key finding
    python3 -I tools/audit/i18n_keys.py --root /path/to/carbon --in-flight 'net.reconnected_tcp*,death.gold_*'

What it reports, and how it relates to CI
-----------------------------------------
``LocalizationTests.EnglishKeys_AreAllUsed`` (tests/architecture/Carbon.Architecture.Tests) already fails when an
en.ini key is neither (1) a whole string literal anywhere in desktop/, graphics/ or server/ production code (plural
``.one``/``.other`` included) nor (2) in ``LocalizationRepository.DerivedKeys()`` — every item code
(``item.<code>.name`` and ``.hint`` when ``actionHint`` is set), monster id, status-effect key (``.name`` and
``.desc``), BiomeType, StructureType, SkillCatalog code (``.name``/``.summary``) and NPC spec name.
``Locales_HaveExactlyTheEnglishKeySet`` already fails on any en/vi key-set difference.

So this script reports what that guard cannot see:

* ``derived-no-builder``  key the test accepts only through DerivedKeys(), but no C# builds that prefix+suffix
                          (e.g. a ``status_effects.<key>.desc`` that no code ever formats). Dead text CI cannot catch.
* ``dead-const``          the only literal sits in a const/static field whose identifier is never used.
* ``unreferenced``        no literal and no dynamic builder (CI should already fail unless the key is derived).
* ``literal-not-lookup`` the literal exists but never on a line with a lookup call or a const (only with
                          ``--review-literals``; noisy, mostly switch arms returning a key, which is fine).
* ``in-flight``           keys matching ``--in-flight``, with their current reference count.
* ``dynamic-only``        key matched only by a dynamically built prefix (``"biome." + name``,
                          ``DerivedTextCache("skill.", ".summary")``, ``$"item.{code}.name"``) — possibly used,
                          listed separately for review.
* ``en-only`` / ``vi-only`` key-set differences (CI already guards these; listed for completeness).

Keys under ``--in-flight`` globs (default: keys touched by open PRs #155/#159) are flagged, not hidden.
"""

from __future__ import annotations

import argparse
import fnmatch
import os
import re
import sys

SOURCE_DIRS = ("desktop", "graphics", "server", "wasm", "engine", "contracts")
DEFAULT_IN_FLIGHT = "net.reconnected_tcp*,auth.connection_failed,character.net*,death.gold_*"
DERIVED_NAMESPACES = ("item.", "monster.", "status_effects.", "biome.", "structure.", "skill.", "npc.")
KEY_RE = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")
LOOKUP_RE = re.compile(r"GameText\.|\.Get\(|\.Format\(|\.Plural\(|CachedText|DerivedTextCache|Localiz|TextArg")


def parse_ini(path: str) -> dict[str, int]:
    keys: dict[str, int] = {}
    section = ""
    cont = False
    with open(path, encoding="utf-8-sig") as fh:
        for n, raw in enumerate(fh, 1):
            line = raw.rstrip("\n")
            if cont:
                cont = line.endswith("\\") and not line.endswith("\\\\")
                continue
            s = line.strip()
            if not s or s[0] in ";#":
                continue
            if s.startswith("[") and s.endswith("]"):
                section = s[1:-1].strip()
                continue
            if "=" in s:
                k = s.split("=", 1)[0].strip()
                keys[f"{section}.{k}" if section else k] = n
                cont = line.endswith("\\") and not line.endswith("\\\\")
    return keys


def strip_comments(text: str) -> str:
    """Blank // and /* */ comments; keep strings and newlines."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j]))
            i = j
        elif c == '"':
            j = i + 1
            verbatim = i > 0 and text[i - 1] == "@"
            while j < n and text[j] != '"' and (verbatim or text[j] != "\n"):
                j += 2 if (text[j] == "\\" and not verbatim) else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif c == "'" and i + 3 < n and (text[i + 2] == "'" or (text[i + 1] == "\\" and text[i + 3] == "'")):
            j = i + (3 if text[i + 2] == "'" else 4)
            out.append(text[i:j])
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


LITERAL_RE = re.compile(r'(\$@|@\$|\$|@)?"((?:[^"\\\n]|\\.)*)"')


def literals(text: str):
    """Yield (line, value, is_interpolated, start, end). Interpolation holes become ``{}``."""
    for m in LITERAL_RE.finditer(text):
        value = m.group(2)
        interp = bool(m.group(1) and "$" in m.group(1))
        if interp:
            value = re.sub(r"\{\{|\}\}", "", value)
            value = re.sub(r"\{[^{}]*\}", "{}", value)
        yield text.count("\n", 0, m.start()) + 1, value, interp, m.start(), m.end()


def load_sources(root: str) -> dict[str, str]:
    out: dict[str, str] = {}
    for d in SOURCE_DIRS:
        for dirpath, dirnames, filenames in os.walk(os.path.join(root, d)):
            dirnames[:] = [x for x in dirnames if x not in ("obj", "bin")]
            for f in filenames:
                if f.endswith(".cs") and not f.endswith((".g.cs", ".Designer.cs")):
                    p = os.path.join(dirpath, f)
                    with open(p, encoding="utf-8", errors="replace") as fh:
                        out[os.path.relpath(p, root)] = strip_comments(fh.read())
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description="Localization key dead-data audit")
    ap.add_argument("--root", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    ap.add_argument("--tsv", action="store_true")
    ap.add_argument("--review-literals", action="store_true",
                    help="also list keys whose literals are never on a lookup/const line (noisy: switch arms)")
    ap.add_argument("--in-flight", default=DEFAULT_IN_FLIGHT, help="comma-separated key globs edited by open PRs")
    args = ap.parse_args()
    loc = os.path.join(args.root, "resources", "localization")
    en = parse_ini(os.path.join(loc, "en.ini"))
    vi = parse_ini(os.path.join(loc, "vi.ini"))
    in_flight = [g.strip() for g in args.in_flight.split(",") if g.strip()]
    sources = load_sources(args.root)
    all_keys = set(en) | set(vi)

    exact: dict[str, list[str]] = {}
    exact_lines: dict[str, list[tuple[str, int]]] = {}
    patterns: list[tuple[re.Pattern[str], str, str]] = []  # (regex, human pattern, where)
    for rel, text in sources.items():
        lines = text.split("\n")
        lits = list(literals(text))
        by_line: dict[int, list[str]] = {}
        for ln, val, _, _, _ in lits:
            by_line.setdefault(ln, []).append(val)
        for ln, val, interp, _, _ in lits:
            where = f"{rel}:{ln}"
            if not interp and KEY_RE.match(val):
                exact.setdefault(val, []).append(where)
                exact_lines.setdefault(val, []).append((rel, ln))
            if interp and "{}" in val and re.match(r"^[a-z][a-z0-9_]*\.", val):
                rx = re.escape(val).replace(r"\{\}", ".+")
                patterns.append((re.compile("^" + rx + "$"), val.replace("{}", "*"), where))
                continue
            prefix = None
            if re.match(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)*\.$", val):
                prefix = val
            elif not interp and KEY_RE.match(val) and val not in all_keys and any(k.startswith(val + ".") for k in all_keys):
                prefix = val + "."
            if prefix is None:
                continue
            near = [v for l2 in range(ln, ln + 3) for v in by_line.get(l2, [])]
            suffixes = [s for s in near if re.match(r"^\.[a-z0-9_.]+$", s) and s != prefix]
            if suffixes:
                for s in suffixes:
                    patterns.append((re.compile("^" + re.escape(prefix) + r"[a-z0-9_]+" + re.escape(s) + "$"),
                                     f"{prefix}*{s}", where))
            else:
                patterns.append((re.compile("^" + re.escape(prefix) + ".+$"), f"{prefix}*", where))

    def exact_ref(key: str) -> list[str]:
        refs = list(exact.get(key, []))
        for suffix in (".one", ".other"):
            if key.endswith(suffix):
                refs += exact.get(key[: -len(suffix)], [])
        return refs

    def dead_const(key: str) -> bool:
        base = [key] + [key[: -len(s)] for s in (".one", ".other") if key.endswith(s)]
        sites = [site for b in base for site in exact_lines.get(b, [])]
        if not sites:
            return False
        for rel, ln in sites:
            line = sources[rel].split("\n")[ln - 1]
            m = re.search(r"\b(?:const|static\s+readonly)\s+string\??\s+(\w+)\s*=", line)
            if not m:
                return False
            ident = m.group(1)
            uses = sum(len(re.findall(r"\b" + re.escape(ident) + r"\b", t)) for t in sources.values())
            if uses > 1:
                return False
        return True

    rows: list[tuple[str, str, str, str]] = []  # kind, key, where, evidence
    for key in sorted(en):
        flight = " [IN-FLIGHT]" if any(fnmatch.fnmatchcase(key, g) for g in in_flight) else ""
        refs = exact_ref(key)
        where = f"en.ini:{en[key]}"
        if flight:
            rows.append(("in-flight", key, where, f"{len(refs)} literal ref(s) on this tree"
                         + (f", e.g. {refs[0]}" if refs else "")))
        if refs:
            if dead_const(key):
                rows.append(("dead-const", key, where, f"only literal is an unused constant ({refs[0]}){flight}"))
            elif args.review_literals and not any(LOOKUP_RE.search(sources[r].split("\n")[ln - 1]) or
                         re.search(r"\b(?:const|readonly)\b", sources[r].split("\n")[ln - 1])
                         for b in [key] + [key[: -len(x)] for x in (".one", ".other") if key.endswith(x)]
                         for r, ln in exact_lines.get(b, [])):
                rows.append(("literal-not-lookup", key, where,
                             f"literal(s) found but none on a GameText/CachedText/const line, e.g. {refs[0]}{flight}"))
            continue
        dyn = [(h, w) for rx, h, w in patterns if rx.match(key)]
        derived = key.startswith(DERIVED_NAMESPACES)
        if dyn:
            rows.append(("dynamic-only", key, where, f"`{dyn[0][0]}` at {dyn[0][1]}"
                         + (f" (+{len(dyn) - 1} more)" if len(dyn) > 1 else "") + flight))
        elif derived:
            rows.append(("derived-no-builder", key, where,
                         "test DerivedKeys() accepts it; no code builds this prefix/suffix" + flight))
        else:
            rows.append(("unreferenced", key, where, "no literal, no dynamic prefix (CI EnglishKeys_AreAllUsed "
                         "should fail unless another scanner path sees it)" + flight))
    for key in sorted(set(en) - set(vi)):
        rows.append(("en-only", key, f"en.ini:{en[key]}", "missing from vi.ini"))
    for key in sorted(set(vi) - set(en)):
        rows.append(("vi-only", key, f"vi.ini:{vi[key]}", "missing from en.ini"))

    if args.tsv:
        print("kind\tkey\twhere\tevidence")
        for r in rows:
            print("\t".join(r))
        return 0
    print(f"# Localization key audit\n\nen.ini: {len(en)} keys, vi.ini: {len(vi)} keys, "
          f"{len(patterns)} dynamic key builders found in C#.\n")
    order = ("derived-no-builder", "dead-const", "literal-not-lookup", "unreferenced", "en-only", "vi-only",
             "in-flight", "dynamic-only")
    for kind in order:
        sel = [r for r in rows if r[0] == kind]
        print(f"## {kind} — {len(sel)}\n")
        if kind == "dynamic-only" and sel:
            groups: dict[str, list[str]] = {}
            for _, key, _, ev in sel:
                groups.setdefault(ev.split("`")[1], []).append(key)
            print("| builder pattern | keys matched | sample |\n|---|---|---|")
            for pat, keys in sorted(groups.items()):
                print(f"| `{pat}` | {len(keys)} | {', '.join(keys[:4])}{' …' if len(keys) > 4 else ''} |")
            print()
            continue
        if sel:
            print("| key | where | evidence |\n|---|---|---|")
            for _, key, where, ev in sel:
                print(f"| `{key}` | {where} | {ev} |")
            print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
