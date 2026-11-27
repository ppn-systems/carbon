#!/usr/bin/env python3
"""Dead-data audit: vector art (resources/art) versus the game data that selects it.

Run (read-only, standard library only):

    python3 -I tools/audit/art_entries.py            # markdown report
    python3 -I tools/audit/art_entries.py --tsv
    python3 -I tools/audit/art_entries.py --root /path/to/carbon

The client (``CarbonVectorArtLibrary``) loads only what ``resources/art/catalog.cvg.json`` lists and looks art up by
a numeric key, never by file name:

=========== ======================== ==========================================================
section     lookup key               source of live keys
=========== ======================== ==========================================================
characters  ``preset``               every preset in the catalog (no other source; listed only)
monsters    ``archetypeId``          ``resources/config/monsters.json`` ``monsters[].archetypeId``
npcs        ``archetypeId``          ``public const ushort X = 2nn`` in ``WorldActorCatalog.cs``
items       ``itemId``               ``resources/config/items.json`` ``items[].id`` (and ``code``)
props       ``propType``             ``enum MapPropType`` (``None`` excluded)
biomes      ``biomeId``              ``enum BiomeType``
structures  ``structureType``        ``enum StructureType`` (``None`` excluded)
uiIcons     ``id`` (string)          ``TryGetUiIcon("x")`` literals + ``status_effects.json`` ``effects[].icon``
=========== ======================== ==========================================================

Findings
--------
* ``orphan-file``     a ``.cvg.json`` that no catalog entry, other art file or C# literal names (never loaded).
* ``missing-file``    a catalog entry whose ``file`` does not exist.
* ``dead-entry``      a catalog entry whose key no live data produces (art never shown).
* ``no-art``          a live key with no catalog entry (client falls back: default rig / no icon).
* ``id-mismatch``     catalog string ``id`` differs from the data's name for the same key (art drawn for the wrong
                      thing, or a stale name).
* ``unread-field``    a catalog entry field whose DTO property is never read outside ``CvgJsonModels.cs``.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys


def snake(name: str) -> str:
    return re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", name).replace(" ", "_").lower()


def parse_enum(text: str, enum: str) -> dict[int, str]:
    m = re.search(r"enum\s+" + enum + r"\b[^{]*\{(.*?)\}", text, re.S)
    out: dict[int, str] = {}
    if not m:
        return out
    body = re.sub(r"//[^\n]*|/\*.*?\*/|\[[^\]]*\]", "", m.group(1), flags=re.S)
    value = -1
    for part in body.split(","):
        part = part.strip()
        if not part:
            continue
        mm = re.match(r"(\w+)\s*(?:=\s*(\d+))?", part)
        if not mm:
            continue
        value = int(mm.group(2)) if mm.group(2) else value + 1
        out[value] = mm.group(1)
    return out


def read_cs(root: str, dirs=("engine", "desktop", "graphics", "server", "contracts", "wasm")) -> dict[str, str]:
    out: dict[str, str] = {}
    for d in dirs:
        for p in glob.glob(os.path.join(root, d, "**", "*.cs"), recursive=True):
            rel = os.path.relpath(p, root)
            if "/obj/" in rel or "/bin/" in rel:
                continue
            with open(p, encoding="utf-8", errors="replace") as fh:
                out[rel] = fh.read()
    return out


def snake_lower(name: str) -> str:
    """CvgCatalogDto property name → catalog JSON key (camelCase): ``UiIcons`` → ``uiIcons``."""
    return name[0].lower() + name[1:]


def enclosing_block_end(text: str, idx: int) -> int:
    """Index of the ``}`` closing the block that contains ``idx``."""
    depth = 0
    for i in range(idx, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            if depth == 0:
                return i
            depth -= 1
    return len(text)


def find_text(sources: dict[str, str], needle: str) -> str:
    for rel, text in sources.items():
        if needle in text:
            return rel
    return ""


def main() -> int:
    ap = argparse.ArgumentParser(description="Vector art dead-data audit")
    ap.add_argument("--root", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    ap.add_argument("--tsv", action="store_true")
    args = ap.parse_args()
    root = args.root
    art = os.path.join(root, "resources", "art")
    cfg = os.path.join(root, "resources", "config")
    with open(os.path.join(art, "catalog.cvg.json"), encoding="utf-8") as fh:
        catalog = json.load(fh)
    sources = read_cs(root)
    all_cs = "\n".join(sources.values())
    rows: list[tuple[str, str, str, str, str]] = []  # kind, section, item, where, evidence

    def enum_from(file_hint: str, enum: str) -> tuple[dict[int, str], str]:
        for rel, text in sources.items():
            if re.search(r"enum\s+" + enum + r"\b", text):
                return parse_enum(text, enum), rel
        return {}, "?"

    live: dict[str, dict[object, str]] = {}
    live_where: dict[str, str] = {}
    with open(os.path.join(cfg, "monsters.json"), encoding="utf-8") as fh:
        live["monsters"] = {m["archetypeId"]: m["id"] for m in json.load(fh)["monsters"]}
    live_where["monsters"] = "resources/config/monsters.json"
    with open(os.path.join(cfg, "items.json"), encoding="utf-8") as fh:
        live["items"] = {i["id"]: i["code"] for i in json.load(fh)["items"]}
    live_where["items"] = "resources/config/items.json"
    actor = next((r for r in sources if r.endswith("WorldActorCatalog.cs")), "")
    npcs: dict[object, str] = {}
    if actor:
        consts = dict(re.findall(r"public const ushort (\w+) = (2\d\d);", sources[actor]))
        for name, value in consts.items():
            spec = re.search(r"new\(" + name + r',\s*"([^"]+)"', sources[actor])
            npcs[int(value)] = snake(spec.group(1)) if spec else snake(name)
    live["npcs"], live_where["npcs"] = npcs, actor
    for section, enum in (("props", "MapPropType"), ("biomes", "BiomeType"), ("structures", "StructureType")):
        values, rel = enum_from("", enum)
        live[section] = {k: snake(v) for k, v in values.items() if v != "None"}
        live_where[section] = rel
    icons: dict[object, str] = {}
    for rel, text in sources.items():
        for name in re.findall(r'TryGetUiIcon\(\s*"(\w+)"', text):
            icons[name] = rel
    with open(os.path.join(cfg, "status_effects.json"), encoding="utf-8") as fh:
        for e in json.load(fh).get("effects", []):
            if e.get("icon"):
                icons[e["icon"]] = "resources/config/status_effects.json"
    live["uiIcons"] = {k: k for k in icons}
    live_where["uiIcons"] = "TryGetUiIcon literals + status_effects.json"

    key_field = {"characters": "file", "monsters": "archetypeId", "npcs": "archetypeId", "items": "itemId",
                 "props": "propType", "biomes": "biomeId", "structures": "structureType", "uiIcons": "id"}
    referenced_files: set[str] = set()
    for section, entries in catalog.items():
        if not isinstance(entries, list):
            continue
        kf = key_field.get(section)
        seen_keys = set()
        for e in entries:
            f = e.get("file", "")
            referenced_files.add(os.path.normpath(f))
            label = f"{e.get('id', '?')} ({kf}={e.get(kf)})"
            if f and not os.path.exists(os.path.join(art, f)):
                rows.append(("missing-file", section, label, f"catalog.cvg.json → {f}", "file not on disk"))
            if section not in live or kf is None:
                continue
            key = e.get(kf)
            seen_keys.add(key)
            data = live[section]
            if key not in data:
                rows.append(("dead-entry", section, label, f"resources/art/{f}",
                             f"no {kf} {key} in {live_where[section]}"))
            elif section != "uiIcons" and data[key] != e.get("id"):
                rows.append(("id-mismatch", section, label, live_where[section],
                             f"data name for {kf} {key} is '{data[key]}', art id is '{e.get('id')}'"))
        if section in live:
            for key, name in sorted(live[section].items(), key=lambda kv: str(kv[0])):
                if key not in seen_keys:
                    rows.append(("no-art", section, f"{name} ({kf}={key})", live_where[section],
                                 "no catalog.cvg.json entry; client falls back"))

    for p in sorted(glob.glob(os.path.join(art, "**", "*.cvg.json"), recursive=True)):
        rel = os.path.normpath(os.path.relpath(p, art))
        if rel == "catalog.cvg.json" or rel in referenced_files:
            continue
        stem = os.path.basename(rel)[: -len(".cvg.json")]
        hit = find_text(sources, os.path.basename(rel)) or find_text(sources, f'"{stem}"')
        rows.append(("orphan-file", rel.split(os.sep)[0], stem, f"resources/art/{rel}",
                     f"not in catalog; C# mention: {hit or 'none'}"))

    # Catalog entry fields the loader never reads. Reads are scoped per section: the DTO type of each
    # CvgCatalogDto list property, and variables declared with that type (``CvgItemCatalogDto entry = ...``).
    models = next((r for r in sources if r.endswith("CvgJsonModels.cs")), "")
    section_type = dict((snake_lower(n), t) for t, n in
                        re.findall(r"public\s+List<(\w+)>\s+(\w+)\s*\{", sources.get(models, "")))
    for section, entries in catalog.items():
        if not isinstance(entries, list) or section not in section_type:
            continue
        dto = section_type[section]
        regions: list[str] = []
        for rel, text in sources.items():
            if rel == models:
                continue
            for m in re.finditer(r"\b" + dto + r"\??\s+(\w+)\s*(?:=|in\b)", text):
                var = m.group(1)
                block_end = enclosing_block_end(text, m.start())
                regions.append((var, text[m.start():block_end]))
            sec = section[0].upper() + section[1:]
            for m in re.finditer(r"\.\s*" + sec + r"\[[^\]]+\]", text):
                regions.append(("", text[m.end():m.end() + 80]))
        fields = sorted({k for e in entries for k in e})
        for field in fields:
            prop = field[0].upper() + field[1:]
            read = False
            whole = False
            for var, region in regions:
                if var:
                    if re.search(r"\b" + var + r"\s*\.\s*" + prop + r"\b(?!\s*=[^=>])", region):
                        read = True
                    if re.search(r"[(,]\s*" + var + r"\s*[,)]", region):
                        whole = True
                elif re.match(r"\s*\.\s*" + prop + r"\b", region):
                    read = True
            if not read:
                rows.append(("unread-field", section, field, f"{models} ({dto})",
                             f"loaded from all {len(entries)} entries, never read"
                             + ("; entry is also passed whole to a method (verify)" if whole else "")))

    if args.tsv:
        print("kind\tsection\titem\twhere\tevidence")
        for r in rows:
            print("\t".join(r))
        return 0
    print("# Vector art audit\n")
    for kind in ("orphan-file", "missing-file", "dead-entry", "no-art", "id-mismatch", "unread-field"):
        sel = [r for r in rows if r[0] == kind]
        print(f"## {kind} — {len(sel)}\n")
        if sel:
            print("| section | item | where | evidence |\n|---|---|---|---|")
            for _, section, item, where, ev in sel:
                print(f"| {section} | `{item}` | {where} | {ev} |")
            print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
