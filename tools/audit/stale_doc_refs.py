#!/usr/bin/env python3
"""Stale documentation reference finder (read-only).

Extracts backticked tokens (`like/this.cs`, `TypeName`, `Type.Member()`) from:
    AGENTS.md, README.md, docs/*.md, docs/adr/*.md, .claude/skills/**/SKILL.md
and reports tokens that do not resolve in the repository at HEAD:

  * PATH   tokens (contain '/' or end in a known file extension) must match a
           git-tracked file or directory: repo-relative, doc-relative, or (for a
           bare file name) any tracked file with that basename.  Globs (`*`) and
           placeholders (`<name>`, `{x}`, `...`) are skipped.
  * SYMBOL tokens (PascalCase identifiers, optionally dotted / generic / called)
           must have their leading PascalCase identifier occur somewhere in a
           tracked *.cs file.  Symbols absent from the source may be external
           (Nalix, .NET), so they are reported in a separate section with the
           file:line of every mention.

Usage:  python3 -I tools/audit/stale_doc_refs.py [--repo PATH] [--include-audit]
          --include-audit  also scan docs/audit/*.md (historical reports; noisy)
Stdlib only; makes no changes.  Exit code 1 if a PATH token is missing.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from collections import defaultdict

EXTS = (".cs", ".csproj", ".json", ".md", ".props", ".targets", ".slnx", ".sln", ".py", ".sh", ".ps1",
        ".yml", ".yaml", ".txt", ".png", ".glsl", ".vert", ".frag", ".toml", ".xml", ".dll", ".cvg.json")
TICK_RE = re.compile(r"(?<!`)`([^`\n]+)`(?!`)")
FENCE_RE = re.compile(r"^\s*```")
IDENT_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
SYMBOL_RE = re.compile(r"^@?([A-Z][A-Za-z0-9_]*)(?:<[^>]*>)?(?:\.[A-Za-z_][\w]*(?:<[^>]*>)?)*(?:\([^)]*\))?;?$")
# Well-known framework/external names that are never expected in repo source.
EXTERNAL_HINT = {"Span", "ReadOnlySpan", "Memory", "Task", "ValueTask", "List", "Dictionary", "HashSet", "String",
                 "StringBuilder", "ArrayPool", "Interlocked", "Volatile", "CancellationToken", "IDisposable",
                 "FluentAssertions", "NSubstitute", "Xunit", "Fact", "Theory", "TreatWarningsAsErrors", "Nullable",
                 "ImplicitUsings", "LangVersion", "README", "AGENTS", "SKILL", "TODO", "FrozenDictionary",
                 "ConcurrentDictionary", "Lazy", "Func", "Action", "Vector2", "MathF", "Math", "GC", "LINQ",
                 "IsAotCompatible", "IsTrimmable", "PublishAot", "ThreadStatic", "Obsolete", "Flags", "Enum",
                 "BenchmarkDotNet", "SocketAsyncEventArgs", "IValueTaskSource", "ResourceManager", "Infinity",
                 "Proposed", "Accepted", "Superseded", "Disconnected", "MethodUnderTest_Scenario_ExpectedBehavior"}


def git_ls(repo: str, *patterns: str) -> list[str]:
    out = subprocess.run(["git", "-C", repo, "ls-files", "-z", "--", *patterns],
                         check=True, capture_output=True).stdout.decode()
    return sorted(p for p in out.split("\0") if p)


def doc_files(repo: str, include_audit: bool) -> list[str]:
    pats = ["AGENTS.md", "README.md", "docs/*.md", "docs/adr/*.md", ".claude/skills/**/SKILL.md", ".claude/skills/*/SKILL.md"]
    if include_audit:
        pats.append("docs/audit/*.md")
    files = set(git_ls(repo, *pats))
    return sorted(f for f in files if include_audit or not f.startswith("docs/audit/"))


def tokens(text: str):
    in_fence = False
    for n, line in enumerate(text.splitlines(), 1):
        if FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for m in TICK_RE.finditer(line):
            yield n, m.group(1).strip()


def is_placeholder(tok: str) -> bool:
    return any(c in tok for c in "*<>{}$|") or "..." in tok or "…" in tok or " " in tok.strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    ap.add_argument("--include-audit", action="store_true")
    a = ap.parse_args()
    repo = a.repo

    tracked = git_ls(repo)
    tracked_set = set(tracked)
    dirs = {"/".join(p.split("/")[:i]) for p in tracked for i in range(1, p.count("/") + 1)}
    top_dirs = {p.split("/", 1)[0] for p in tracked if "/" in p} | {"wasm", "analyzer", "docs", "tools"}
    by_base = defaultdict(list)
    for p in tracked:
        by_base[os.path.basename(p)].append(p)
    idents: set[str] = set()
    for f in tracked:
        if f.endswith(".cs"):
            try:
                idents.update(IDENT_RE.findall(open(os.path.join(repo, f), encoding="utf-8-sig", errors="replace").read()))
            except OSError:
                pass

    missing_paths: list[str] = []
    missing_symbols: dict[str, list[str]] = defaultdict(list)
    for doc in doc_files(repo, a.include_audit):
        text = open(os.path.join(repo, doc), encoding="utf-8", errors="replace").read()
        ddir = os.path.dirname(doc)
        for line, tok in tokens(text):
            raw = tok.split(":")[0] if re.search(r":\d", tok) else tok  # strip file:line suffixes
            raw = raw.rstrip("/.,;")
            if not raw or raw.startswith(("http", "dotnet ", "git ", "-", "--")):
                continue
            base = raw.rsplit("/", 1)[-1]
            first = raw.split("/", 1)[0]
            has_ext = raw.endswith(EXTS) and not base.startswith(".")
            rooted = "/" in raw and (first in top_dirs or first.startswith("."))
            looks_path = (has_ext or rooted) and not raw.startswith("@")
            if looks_path:
                if raw.startswith(("~", "/")) or "=" in raw:
                    continue
                if is_placeholder(raw):
                    # Check the fixed prefix of a placeholder path, e.g. `engine/Systems/<Domain>/` -> engine/Systems.
                    cut = min([i for i in (raw.find(c) for c in "*<>{}$|") if i >= 0] or [len(raw)])
                    prefix = raw[:cut].rsplit("/", 1)[0] if "/" in raw[:cut] else ""
                    if prefix and "/" in prefix and prefix not in dirs and not any(d.endswith("/" + prefix) for d in dirs):
                        missing_paths.append(f"{doc}:{line}  `{tok}` (prefix {prefix}/ missing)")
                    continue
                cand = raw[2:] if raw.startswith("./") else raw
                norm = os.path.normpath(os.path.join(ddir, cand)).replace("\\", "/")
                ok = (cand in tracked_set or cand in dirs or norm in tracked_set or norm in dirs
                      or ("/" not in cand and cand in by_base)
                      or any(p.endswith("/" + cand) for p in tracked) or any(d.endswith("/" + cand) for d in dirs))
                if not ok:
                    missing_paths.append(f"{doc}:{line}  `{tok}`")
                continue
            m = SYMBOL_RE.match(raw)
            if m and not is_placeholder(raw.split("(")[0]):
                head = m.group(1)
                if len(head) < 4 or head in EXTERNAL_HINT or head.isupper():
                    continue
                if not re.search(r"[a-z]", head) or not re.search(r"[A-Z].*[A-Z]|[a-z]", head[1:]):
                    continue
                if head not in idents:
                    missing_symbols[head].append(f"{doc}:{line}")

    print("== PATH references that do not exist at HEAD ==")
    for p in missing_paths:
        print("MISSING-PATH", p)
    print(f"missing paths: {len(missing_paths)}")
    print("\n== SYMBOL references not found in any tracked .cs (stale or external) ==")
    for sym in sorted(missing_symbols):
        locs = missing_symbols[sym]
        print(f"MISSING-SYMBOL {sym}  ({len(locs)}x) " + ", ".join(locs[:6]) + (" ..." if len(locs) > 6 else ""))
    print(f"missing symbols: {len(missing_symbols)}")
    return 1 if missing_paths else 0


if __name__ == "__main__":
    sys.exit(main())
