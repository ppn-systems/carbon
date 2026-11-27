#!/usr/bin/env python3
"""Naming audit for data: JSON property case, i18n key shape, and art/catalog id shape.

    python3 -I tools/audit/data_naming.py            # summary
    python3 -I tools/audit/data_naming.py <check>... # rows: json, jsonid, i18n, art, policy

json   - JSON object keys under resources/ that are not camelCase (snake_case, PascalCase, kebab-case).
jsonid - string *values* of id-like keys ("id", "*Id", "*Ids") grouped by shape (snake_case, kebab, dotted, camel).
i18n   - locale keys whose segments are not lower_snake or that mix separators; key prefixes per file.
art    - art file names and catalog ids whose shape differs from the dominant shape.
policy - C# JsonSerializerOptions / [JsonSourceGenerationOptions] naming policies and [JsonPropertyName] values that
         are not camelCase.
"""
from __future__ import annotations

import configparser
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict

ROOT = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()

CAMEL = re.compile(r"^[a-z][a-zA-Z0-9]*$")
SNAKE = re.compile(r"^[a-z][a-z0-9]*(?:_[a-z0-9]+)+$")
LOWER = re.compile(r"^[a-z][a-z0-9]*$")
KEBAB = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$")
PASCAL = re.compile(r"^[A-Z][a-zA-Z0-9]*$")
DOTTED = re.compile(r"^[a-z0-9_]+(?:\.[a-z0-9_]+)+$")


def shape(s: str) -> str:
    for name, rx in (("lower", LOWER), ("camel", CAMEL), ("snake", SNAKE), ("kebab", KEBAB), ("Pascal", PASCAL), ("dotted", DOTTED)):
        if rx.match(s):
            return name
    return "other"


def tracked(pattern: str) -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z", "--", pattern], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    return sorted({p for p in out.split("\0") if p})


def walk(node, path, out):
    if isinstance(node, dict):
        for k, v in node.items():
            out.append((path, k, v))
            walk(v, f"{path}.{k}", out)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            walk(v, f"{path}[]", out)


def json_files() -> list[str]:
    return [p for p in tracked("resources/*.json") if not p.endswith(".schema.json")] + tracked("resources/*.schema.json")


def check_json():
    for p in json_files():
        try:
            doc = json.load(open(os.path.join(ROOT, p), encoding="utf-8"))
        except json.JSONDecodeError as ex:
            yield p, f"invalid json: {ex}"
            continue
        items = []
        walk(doc, "$", items)
        bad = Counter()
        sample = {}
        for path, k, _ in items:
            sh = shape(k)
            if sh not in ("lower", "camel") and not k.startswith("$"):
                # Keys of a map (dictionary) are data, not property names: report them separately.
                bad[(sh, path)] += 1
                sample.setdefault((sh, path), k)
        for (sh, path), n in sorted(bad.items()):
            yield p, f"{sh} key x{n} at {path} (e.g. {sample[(sh, path)]!r})"


def check_jsonid():
    shapes: dict[str, Counter] = defaultdict(Counter)
    examples: dict[tuple[str, str], str] = {}
    for p in json_files():
        try:
            doc = json.load(open(os.path.join(ROOT, p), encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        items = []
        walk(doc, "$", items)
        for _, k, v in items:
            if not (k == "id" or k.endswith("Id") or k.endswith("Ids")):
                continue
            values = v if isinstance(v, list) else [v]
            for value in values:
                if isinstance(value, str):
                    sh = shape(value)
                    shapes[p][sh] += 1
                    examples.setdefault((p, sh), f"{k}={value}")
    # lower (one word) and snake are the same convention: lower_snake. Report the totals per folder and every other shape.
    folders: dict[str, Counter] = defaultdict(Counter)
    for p, counter in shapes.items():
        folders[os.path.dirname(p)].update(counter)
        for sh in counter:
            if sh not in ("lower", "snake"):
                yield p, f"{sh} id x{counter[sh]} (e.g. {examples[(p, sh)]})"
    for folder, counter in sorted(folders.items()):
        yield folder, "id shapes: " + ", ".join(f"{sh}={n}" for sh, n in counter.most_common())


def check_i18n():
    for p in tracked("resources/localization/*.ini"):
        text = open(os.path.join(ROOT, p), encoding="utf-8").read()
        section = ""
        prefixes = Counter()
        odd = []
        for i, line in enumerate(text.splitlines(), 1):
            line = line.strip()
            if not line or line.startswith((";", "#")):
                continue
            if line.startswith("["):
                section = line.strip("[]")
                continue
            if "=" not in line:
                continue
            key = line.split("=", 1)[0].strip()
            segments = key.split(".")
            prefixes[(section, segments[0])] += 1
            for seg in segments:
                if not re.match(r"^[a-z0-9]+(?:_[a-z0-9]+)*$", seg):
                    odd.append((i, key, seg))
                    break
        for i, key, seg in odd:
            yield f"{p}:{i}", f"segment {seg!r} of {key!r} is not lower_snake"
        yield p, "sections/prefixes: " + ", ".join(f"[{s}] {pre}.*={n}" for (s, pre), n in sorted(prefixes.items()))


def check_art():
    files = tracked("resources/art/*")
    names = Counter()
    ex = {}
    for f in files:
        stem = os.path.basename(f).split(".", 1)[0]
        sh = shape(stem)
        names[(f.split("/")[2] if f.count("/") > 2 else "-", sh)] += 1
        ex.setdefault((f.split("/")[2] if f.count("/") > 2 else "-", sh), f)
    for (folder, sh), n in sorted(names.items()):
        yield f"resources/art/{folder}", f"file stem {sh} x{n} (e.g. {ex[(folder, sh)]})"
    catalog = os.path.join(ROOT, "resources/art/catalog.cvg.json")
    if os.path.exists(catalog):
        doc = json.load(open(catalog, encoding="utf-8"))
        for section, value in doc.items():
            if isinstance(value, dict):
                c = Counter(shape(k) for k in value)
                yield f"catalog.cvg.json:{section}", "map keys " + ", ".join(f"{s}={n}" for s, n in c.most_common())


def check_policy():
    rx_policy = re.compile(r"(PropertyNamingPolicy\s*=\s*[\w.]+|PropertyNamingPolicy\s*=\s*JsonKnownNamingPolicy\.\w+|JsonNamingPolicy\.\w+)")
    rx_name = re.compile(r'\[JsonPropertyName\("(?P<n>[^"]+)"\)\]')
    for p in tracked("*.cs"):
        text = open(os.path.join(ROOT, p), encoding="utf-8").read()
        for m in rx_policy.finditer(text):
            yield f"{p}:{text.count(chr(10), 0, m.start()) + 1}", m.group(0)
        for m in rx_name.finditer(text):
            n = m.group("n")
            if shape(n) not in ("lower", "camel"):
                yield f"{p}:{text.count(chr(10), 0, m.start()) + 1}", f"[JsonPropertyName(\"{n}\")] is {shape(n)}"


CHECKS = {"json": check_json, "jsonid": check_jsonid, "i18n": check_i18n, "art": check_art, "policy": check_policy}


def main(argv: list[str]) -> int:
    wanted = argv or list(CHECKS)
    for name in wanted:
        if name not in CHECKS:
            print(f"unknown check {name}; choose from {', '.join(CHECKS)}", file=sys.stderr)
            return 2
        for where, detail in CHECKS[name]():
            print(f"{name}\t{where}\t{detail}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
