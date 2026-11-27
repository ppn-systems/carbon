#!/usr/bin/env python3
"""Dead-data audit: screenshot scenarios (tools/screenshots/scenarios.txt) and CARBON_* environment knobs.

Run (read-only, standard library only):

    python3 -I tools/audit/screenshot_scenarios.py            # markdown report
    python3 -I tools/audit/screenshot_scenarios.py --tsv
    python3 -I tools/audit/screenshot_scenarios.py --root /path/to/carbon --scenarios other_scenarios.txt

A scenario is not dispatched by name: it is a pair of env-var sets (server | client). "Handled" therefore means
each knob a scenario sets is read by code, and each value token it passes is understood by code. Checks:

* ``knob-unread``         a scenario sets ``CARBON_X`` that no C# literal / shell ``$CARBON_X`` reads.
* ``value-unhandled``     a value token (split on ``: , ; | x +``) that is not numeric, not a C# string literal (or
                          covered by a ``"prefix-"`` literal), not a C# enum member and not a status-effect key
                          (case-insensitive). A token reaching a ``_ =>`` default arm shows up here: verify by hand.
* ``knob-never-listed``   a screenshot knob (documented in tools/screenshots/README.md, or read in desktop
                          Capture/DesktopOptions code or the host's ``CARBON_DEBUG_*`` overrides) no scenario sets.
* ``value-never-listed``  a value code accepts (a ``"x" =>`` switch arm after ``get("CARBON_X")`` in
                          DesktopOptions, or a ``CARBON_X=a|b`` README alternative) that no scenario uses.
* ``env-undocumented``    ``CARBON_*`` read in C# or shell but named in no doc (``*.md``, deploy/, scenarios.txt).
* ``env-doc-unread``      ``CARBON_*`` named in docs but read nowhere.
* ``duplicate-name`` / ``name-too-long`` scenario name rules from scenarios.txt's header.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys

CODE_DIRS = ("desktop", "server", "engine", "graphics", "wasm", "contracts", "analyzer")


def read(path: str) -> str:
    with open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def files(root: str, dirs, patterns) -> dict[str, str]:
    out: dict[str, str] = {}
    for d in dirs:
        for pat in patterns:
            for p in glob.glob(os.path.join(root, d, "**", pat), recursive=True):
                rel = os.path.relpath(p, root)
                if "/obj/" in rel or "/bin/" in rel or "/node_modules/" in rel:
                    continue
                out[rel] = read(p)
    return out


def parse_env(text: str) -> dict[str, str]:
    env: dict[str, str] = {}
    for tok in text.split():
        if "=" in tok:
            k, v = tok.split("=", 1)
            env[k] = v
    return env


def main() -> int:
    ap = argparse.ArgumentParser(description="Screenshot scenario and CARBON_* knob audit")
    ap.add_argument("--root", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    ap.add_argument("--scenarios", default="tools/screenshots/scenarios.txt")
    ap.add_argument("--tsv", action="store_true")
    args = ap.parse_args()
    root = args.root

    scenarios: list[tuple[str, int, dict[str, str], dict[str, str]]] = []
    rows: list[tuple[str, str, str, str]] = []  # kind, item, where, evidence
    names: dict[str, int] = {}
    for n, line in enumerate(read(os.path.join(root, args.scenarios)).splitlines(), 1):
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        parts = [p.strip() for p in s.split("|")]
        name = parts[0]
        if name in names:
            rows.append(("duplicate-name", name, f"scenarios.txt:{n}", f"also at line {names[name]}"))
        names[name] = n
        if len(name) > 18:
            rows.append(("name-too-long", name, f"scenarios.txt:{n}", f"{len(name)} > 18 chars (login stalls)"))
        server = parse_env(parts[1]) if len(parts) > 1 else {}
        client = parse_env(parts[2]) if len(parts) > 2 else {}
        scenarios.append((name, n, server, client))

    cs = files(root, CODE_DIRS, ("*.cs",))
    sh = files(root, ("tools", "script", "deploy"), ("*.sh", "*.ps1", "Dockerfile*", "*.yml", "*.yaml"))
    sh.update({os.path.relpath(p, root): read(p) for p in glob.glob(os.path.join(root, "deploy", "*"))
               if os.path.isfile(p)})
    docs = files(root, (".",), ("*.md",))
    docs = {k: v for k, v in docs.items() if not k.startswith(("artifacts/", "tools/audit/"))}
    docs[args.scenarios] = read(os.path.join(root, args.scenarios))
    for k, v in sh.items():
        if k.startswith("deploy/"):
            docs[k] = v  # compose/env files document deploy knobs

    read_cs: dict[str, str] = {}
    for rel, text in cs.items():
        for m in re.finditer(r'"(CARBON_[A-Z0-9_]+)"', text):
            read_cs.setdefault(m.group(1), f"{rel}:{text.count(chr(10), 0, m.start()) + 1}")
    read_sh: dict[str, str] = {}
    for rel, text in sh.items():
        for m in re.finditer(r"\$\{?(?:env:)?(CARBON_[A-Z0-9_]+)", text):
            read_sh.setdefault(m.group(1), rel)
    read_any = {**read_sh, **read_cs}
    documented: dict[str, str] = {}
    for rel, text in docs.items():
        for m in re.finditer(r"\b(CARBON_[A-Z0-9_]*[A-Z0-9])\b", text):
            documented.setdefault(m.group(1), rel)

    # string literals and enum members C# understands
    literals = {m.lower() for t in cs.values() for m in re.findall(r'"([^"\\\n]{1,40})"', t)}
    prefix_literals = {lit for lit in literals if len(lit) > 2 and lit[-1] in "-:"}
    enum_members: set[str] = set()
    for t in cs.values():
        for body in re.findall(r"\benum\s+\w+[^{]*\{(.*?)\}", t, re.S):
            body = re.sub(r"//[^\n]*|/\*.*?\*/|\[[^\]]*\]", "", body, flags=re.S)
            enum_members |= {m.lower() for m in re.findall(r"^\s*(\w+)", body, re.M)}
    effect_keys: set[str] = set()
    se = os.path.join(root, "resources", "config", "status_effects.json")
    if os.path.exists(se):
        effect_keys = {e["key"].lower() for e in json.loads(read(se)).get("effects", [])}

    used_knobs: dict[str, list[str]] = {}
    used_values: dict[str, set[str]] = {}
    for name, n, server, client in scenarios:
        for side, env in (("server", server), ("client", client)):
            for k, v in env.items():
                used_knobs.setdefault(k, []).append(name)
                if k not in read_any:
                    rows.append(("knob-unread", f"{name}: {k}", f"scenarios.txt:{n} ({side})",
                                 "no C# literal or shell $-reference"))
                head = v.split(":")[0].split(",")[0]
                used_values.setdefault(k, set()).add(head.lower())
                for tok in re.split(r"[:,;|+]|(?<=\d)x(?=\d)", v):
                    t = tok.strip().lower()
                    if not t or re.fullmatch(r"-?[\d.]+", t) or t in ("v2",):
                        continue
                    if t in literals or t + ":" in literals or t in enum_members or t in effect_keys:
                        continue
                    if any(t.startswith(lit) for lit in prefix_literals):
                        continue  # e.g. "status-hover" handled by StartsWith("status-")
                    rows.append(("value-unhandled", f"{name}: {k}={v}", f"scenarios.txt:{n}",
                                 f"token '{tok}' is no C# literal, enum member or effect key"))

    # knobs that belong to the screenshot tool
    readme = read(os.path.join(root, "tools", "screenshots", "README.md"))
    screenshot_knobs = set(re.findall(r"`(CARBON_[A-Z0-9_]+)", readme))
    for rel, text in cs.items():
        if "/Capture/" in rel or rel.endswith("DesktopOptions.cs") or "DebugOverrides" in text:
            for m in re.findall(r'"(CARBON_(?:DEBUG_[A-Z0-9_]+|FOCUS_[A-Z_]+|CAPTURE_[A-Z_]+|[A-Z_]+_PREVIEW|OPEN_[A-Z_]+|'
                                r'MACRO_VIEW|AUTO_WALK|WEATHER))"', text):
                screenshot_knobs.add(m)
    harness = set(re.findall(r"\b(CARBON_[A-Z0-9_]+)=", read(os.path.join(root, "tools", "screenshots", "capture.sh"))))
    for k in sorted(screenshot_knobs - set(used_knobs) - harness):
        rows.append(("knob-never-listed", k, read_cs.get(k, documented.get(k, "?")),
                     "screenshot knob no scenario sets" + ("" if k in read_any else " (and not read)")))

    opts = next((t for r, t in cs.items() if r.endswith("DesktopOptions.cs")), "")
    accepted: dict[str, set[str]] = {}
    for m in re.finditer(r'get\("(CARBON_[A-Z_]+)"\)\s*switch\s*\{(.*?)\}', opts, re.S):
        accepted.setdefault(m.group(1), set()).update(a.lower() for a in re.findall(r'"([^"]+)"\s*=>', m.group(2)))
    for m in re.finditer(r"`(CARBON_[A-Z_]+)=([a-z][a-z\-]*(?:\\\|[a-z][a-z\-]*)+)", readme):
        accepted.setdefault(m.group(1), set()).update(a for a in m.group(2).split("\\|"))
    for knob, values in sorted(accepted.items()):
        for v in sorted(values - used_values.get(knob, set())):
            rows.append(("value-never-listed", f"{knob}={v}", read_cs.get(knob, "README"),
                         "accepted by code/README, no scenario uses it"))

    setters: dict[str, str] = {}
    for rel, text in sh.items():
        for m in re.finditer(r"(?:\$env:|\b)(CARBON_[A-Z0-9_]+)\s*=", text):
            setters.setdefault(m.group(1), rel)
    for k in sorted(set(read_any) - set(documented)):
        rows.append(("env-undocumented", k, read_any[k], "read in code, named in no *.md / deploy file; "
                     + (f"set by {setters[k]}" if k in setters else "set by no script either")))
    for k in sorted(set(documented) - set(read_any)):
        if k.endswith("_") or k == "CARBON":
            continue
        rows.append(("env-doc-unread", k, documented[k], "documented, but no C#/shell reads it"))

    if args.tsv:
        print("kind\titem\twhere\tevidence")
        for r in rows:
            print("\t".join(r))
        return 0
    print(f"# Screenshot scenario & env knob audit\n\n{len(scenarios)} scenarios, {len(read_any)} CARBON_* knobs read "
          f"in code, {len(documented)} named in docs.\n")
    for kind in ("knob-unread", "value-unhandled", "duplicate-name", "name-too-long", "knob-never-listed",
                 "value-never-listed", "env-undocumented", "env-doc-unread"):
        sel = [r for r in rows if r[0] == kind]
        print(f"## {kind} — {len(sel)}\n")
        if sel:
            print("| item | where | evidence |\n|---|---|---|")
            for _, item, where, ev in sel:
                print(f"| `{item}` | {where} | {ev} |")
            print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
