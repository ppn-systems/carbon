#!/usr/bin/env python3
"""Duplicate-rule audit: candidates for "the same rule implemented twice" in the Carbon C# sources.

Run from anywhere (read-only, standard library only):

    python3 -I tools/audit/find_duplicate_rules.py                 # all three reports
    python3 -I tools/audit/find_duplicate_rules.py bodies          # one report
    python3 -I tools/audit/find_duplicate_rules.py --root /path/to/carbon --tests consts

Reports
-------
* ``bodies``  Method bodies (3..40 lines) that are identical after normalisation, in two or more files. Comments are
              dropped, string literals become ``"S"``, whitespace is collapsed; identifiers and numbers are kept, so a
              hit is the same code with only messages changed (e.g. three ``RequireRange`` helpers).
* ``consts``  Gameplay-looking numeric constants (names containing Range/Radius/Reach/Speed/Distance/Drain/Regen/
              Cost/Fee/Seconds/Ticks) and squared literals ``Nf * Nf``, grouped by value, when one value appears in two
              or more files. Same value in client and server code is a latent duplicate; a near value is worth a look.
* ``walkup``  Loops that walk ``.Parent`` directories looking for ``resources/...`` (repeated repo-root finders).

Only files tracked by git are scanned; ``obj/``, ``bin/`` and ``*.g.cs`` are skipped, and ``tests/`` unless ``--tests``.
Every row is a CANDIDATE: read both copies before calling it a duplicate rule.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import os
import re
import subprocess
import sys

SIG = re.compile(r"^\s*(?:(?:public|private|internal|protected|static|readonly|override|sealed|unsafe)\s+)+"
                 r"[\w<>\[\],.? ]+\s+(\w+)\s*\([^;]*$")
STRING = re.compile(r'\$?@?"(?:[^"\\]|\\.)*"')
LINE_COMMENT = re.compile(r"//.*$")
CONST = re.compile(r"\b(?:const|static\s+readonly)\s+(?:float|double|int|long|uint)\s+(\w+)\s*=\s*([-\d.]+)f?\s*;")
SQUARED = re.compile(r"\b(\d+(?:\.\d+)?)f\s*\*\s*\1f\b")
RULE_NAME = re.compile(r"Range|Radius|Reach|Speed|Distance|Drain|Regen|Cost|Fee|Seconds|Ticks", re.I)


def tracked_cs(root: str, include_tests: bool) -> list[str]:
    out = subprocess.run(["git", "-C", root, "ls-files", "*.cs"], capture_output=True, text=True, check=True).stdout
    files = []
    for rel in out.splitlines():
        if "/obj/" in rel or "/bin/" in rel or rel.endswith(".g.cs") or rel.startswith("tools/"):
            continue
        if rel.startswith("tests/") and not include_tests:
            continue
        files.append(rel)
    return files


def read(root: str, rel: str) -> list[str]:
    with open(os.path.join(root, rel), encoding="utf-8", errors="replace") as fh:
        return fh.read().splitlines()


def report_bodies(root: str, files: list[str]) -> None:
    groups: dict[str, list[tuple[str, int, str]]] = collections.defaultdict(list)
    for rel in files:
        lines = read(root, rel)
        i = 0
        while i < len(lines):
            m = SIG.match(lines[i])
            if not m or "=>" in lines[i]:
                i += 1
                continue
            j, depth, started, body = i, 0, False, []
            while j < len(lines) and j - i < 60:
                text = LINE_COMMENT.sub("", STRING.sub('"S"', lines[j]))
                depth += text.count("{") - text.count("}")
                started = started or "{" in text
                if j > i:
                    body.append(text.strip())
                if started and depth <= 0:
                    break
                j += 1
            norm = " ".join(t for t in body if t)
            if started and 3 <= len(body) <= 40 and len(norm) > 60:
                groups[hashlib.sha1(norm.encode()).hexdigest()].append((rel, i + 1, m.group(1)))
            i = j + 1 if started else i + 1
    print("## bodies: identical normalised method bodies in 2+ files\n")
    for hits in sorted(groups.values(), key=len, reverse=True):
        if len({h[0] for h in hits}) >= 2:
            print("- " + ", ".join(f"`{r}:{ln}` {name}" for r, ln, name in hits))
    print()


def report_consts(root: str, files: list[str]) -> None:
    by_value: dict[str, list[str]] = collections.defaultdict(list)
    for rel in files:
        for n, line in enumerate(read(root, rel), 1):
            code = LINE_COMMENT.sub("", line)
            for m in CONST.finditer(code):
                if RULE_NAME.search(m.group(1)):
                    by_value[str(float(m.group(2)))].append(f"`{rel}:{n}` {m.group(1)}")
            for m in SQUARED.finditer(code):
                by_value[str(float(m.group(1)))].append(f"`{rel}:{n}` {m.group(1)}f*{m.group(1)}f")
    print("## consts: one gameplay value in 2+ files\n")
    for value, hits in sorted(by_value.items(), key=lambda kv: float(kv[0])):
        if len({h.split(":")[0] for h in hits}) >= 2:
            print(f"- **{value}**: " + "; ".join(hits))
    print()


def report_walkup(root: str, files: list[str]) -> None:
    print("## walkup: repo-root finders (`.Parent` loops near `resources`)\n")
    for rel in files:
        lines = read(root, rel)
        for n, line in enumerate(lines, 1):
            if ".Parent" in line:
                window = " ".join(lines[max(0, n - 12):n + 4])
                if "resources" in window or "Carbon.slnx" in window or ".git" in window:
                    print(f"- `{rel}:{n}`")
                    break
    print()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("report", nargs="*", choices=["bodies", "consts", "walkup"], default=None)
    parser.add_argument("--root", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    parser.add_argument("--tests", action="store_true", help="also scan tests/")
    args = parser.parse_args()
    files = tracked_cs(args.root, args.tests)
    reports = args.report or ["bodies", "consts", "walkup"]
    for name in reports:
        {"bodies": report_bodies, "consts": report_consts, "walkup": report_walkup}[name](args.root, files)
    return 0


if __name__ == "__main__":
    sys.exit(main())
