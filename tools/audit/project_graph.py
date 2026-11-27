#!/usr/bin/env python3
"""Carbon project-graph audit (read-only).

Parses every git-tracked *.csproj under the production layers, collects
ProjectReference / PackageReference / Reference(HintPath) items, and checks the
AGENTS.md rule-3 layer graph:

    contracts -> (nothing but contracts)
    engine    -> contracts, engine
    graphics  -> contracts, engine, graphics
    server    -> contracts, engine, server        (never graphics/desktop, never Silk.NET)
    desktop   -> contracts, engine, graphics, desktop
    wasm      -> contracts, engine, graphics, wasm

It also scans `using` directives in every tracked *.cs of each layer and maps the
imported namespace to the layer that owns it (via each project's RootNamespace),
reporting imports that cross a forbidden boundary.  This catches code that would
compile only through a transitive reference.

Usage:  python3 -I tools/audit/project_graph.py [--repo PATH] [--edges]
Exit code 1 if any violation is found.  Stdlib only; makes no changes.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

LAYERS = ["contracts", "engine", "graphics", "server", "desktop", "wasm"]
ALLOWED = {
    "contracts": {"contracts"},
    "engine": {"contracts", "engine"},
    "graphics": {"contracts", "engine", "graphics"},
    "server": {"contracts", "engine", "server"},
    "desktop": {"contracts", "engine", "graphics", "desktop"},
    "wasm": {"contracts", "engine", "graphics", "wasm"},
}
# Package id prefix -> layers allowed to reference it.
RESTRICTED_PACKAGES = {
    "Silk.NET": {"graphics", "desktop", "wasm"},
    "StbImageSharp": {"graphics", "desktop", "wasm"},
    "Npgsql": {"server"},
    "Microsoft.Data.Sqlite": {"server"},
    "SQLitePCLRaw": {"server"},
    "Nalix.Hosting": {"server"},
    "Nalix.Network": {"server"},
}
# Third-party namespaces that are restricted the same way in `using` directives.
RESTRICTED_NAMESPACES = {
    "Silk.NET": {"graphics", "desktop", "wasm"},
    "StbImageSharp": {"graphics", "desktop", "wasm"},
    "Npgsql": {"server"},
    "Microsoft.Data.Sqlite": {"server"},
    "Nalix.Network": {"server"},
    "Nalix.Hosting": {"server"},
}
USING_RE = re.compile(r"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_][\w]*\s*=\s*)?([A-Za-z_][\w.]*)\s*;", re.M)


def git_ls(repo: str, *patterns: str) -> list[str]:
    out = subprocess.run(["git", "-C", repo, "ls-files", "-z", "--", *patterns],
                         check=True, capture_output=True).stdout.decode()
    return sorted(p for p in out.split("\0") if p)


def layer_of(rel: str) -> str | None:
    top = rel.split("/", 1)[0]
    return top if top in LAYERS else None


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def parse_project(repo: str, rel: str) -> dict:
    path = os.path.join(repo, rel)
    root = ET.parse(path).getroot()
    d = os.path.dirname(path)
    name = os.path.splitext(os.path.basename(rel))[0]
    props = {local(e.tag): (e.text or "").strip() for e in root.iter() if local(e.tag) in ("RootNamespace", "AssemblyName")}
    proj, pkg, dll = [], [], []
    for e in root.iter():
        t = local(e.tag)
        inc = e.get("Include")
        if not inc:
            continue
        if t == "ProjectReference":
            proj.append(os.path.relpath(os.path.normpath(os.path.join(d, inc.replace("\\", "/"))), repo))
        elif t == "PackageReference":
            pkg.append(inc)
        elif t == "Reference":
            hint = next((c.text for c in e if local(c.tag) == "HintPath"), None)
            dll.append((inc, os.path.relpath(os.path.normpath(os.path.join(d, hint.replace("\\", "/"))), repo) if hint else None))
    return {"rel": rel, "layer": layer_of(rel), "name": props.get("AssemblyName") or name,
            "ns": props.get("RootNamespace") or props.get("AssemblyName") or name,
            "proj": proj, "pkg": pkg, "dll": dll}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    ap.add_argument("--edges", action="store_true", help="print every project edge")
    a = ap.parse_args()
    repo = a.repo

    projects = [parse_project(repo, r) for r in git_ls(repo, "*.csproj") if layer_of(r)]
    violations: list[str] = []
    notes: list[str] = []

    for p in projects:
        L = p["layer"]
        for ref in p["proj"]:
            tl = layer_of(ref)
            if a.edges:
                print(f"EDGE {p['rel']} -> {ref}")
            if tl is None or tl not in ALLOWED[L]:
                violations.append(f"PROJECT {p['rel']} ({L}) -> {ref} ({tl or 'outside layers'})")
        for pk in p["pkg"]:
            for prefix, allowed in RESTRICTED_PACKAGES.items():
                if pk.lower().startswith(prefix.lower()) and L not in allowed:
                    violations.append(f"PACKAGE {p['rel']} ({L}) -> {pk}")
            if L == "contracts":
                notes.append(f"contracts package dependency: {p['rel']} -> {pk}")
        for inc, hint in p["dll"]:
            notes.append(f"binary reference: {p['rel']} -> {inc} ({hint})")

    # Namespace ownership: longest RootNamespace prefix wins.
    ns_owner = sorted(((p["ns"], p["layer"], p["rel"]) for p in projects), key=lambda t: -len(t[0]))

    def owner(ns: str):
        for root_ns, lay, rel in ns_owner:
            if ns == root_ns or ns.startswith(root_ns + "."):
                return lay, rel
        return None

    using_hits = 0
    for f in git_ls(repo, "*.cs"):
        L = layer_of(f)
        if L is None:
            continue
        try:
            text = open(os.path.join(repo, f), encoding="utf-8-sig").read()
        except OSError:
            continue
        for m in USING_RE.finditer(text):
            ns = m.group(1)
            line = text.count("\n", 0, m.start()) + 1
            o = owner(ns)
            if o and o[0] not in ALLOWED[L]:
                violations.append(f"USING {f}:{line} ({L}) imports {ns} owned by {o[1]} ({o[0]})")
                using_hits += 1
            for prefix, allowed in RESTRICTED_NAMESPACES.items():
                if (ns == prefix or ns.startswith(prefix + ".")) and L not in allowed:
                    violations.append(f"USING {f}:{line} ({L}) imports restricted {ns}")

    print(f"projects scanned: {len(projects)}")
    for layer in LAYERS:
        print(f"  {layer:9s} {sum(1 for p in projects if p['layer'] == layer)}")
    for n in notes:
        print("NOTE", n)
    for v in violations:
        print("VIOLATION", v)
    print(f"violations: {len(violations)}")
    return 1 if violations else 0


if __name__ == "__main__":
    sys.exit(main())
