#!/usr/bin/env python3
"""Naming audit: namespace/folder/assembly drift and type/member naming smells across every tracked C# file.

    python3 -I tools/audit/naming.py              # summary counts per check
    python3 -I tools/audit/naming.py <check>...   # rows for the named checks (tsv: check, path:line, detail)
    python3 -I tools/audit/naming.py all          # every row

Checks: namespace, rootns, suffix, pairs, abbrev, hungarian, iface, async, bool, multitype, filename, nonascii,
nonenglish, folders, dupnames.

Regex-based, so it reports candidates. A person checks each row before renaming (for example `Async` is not wanted on
an override whose base name is fixed, and `bool` fields named after a verb can still read well).
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

ROOT = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()

TYPE_RE = re.compile(
    r"^(?P<indent>[ \t]*)(?:\[[^\]]*\][ \t]*)*(?P<mods>(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*)"
    r"(?P<kind>class|struct|interface|enum|record(?:\s+struct|\s+class)?)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)",
    re.M,
)
NS_RE = re.compile(r"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]", re.M)
ASYNC_RE = re.compile(r"^\s*(?:(?:public|internal|private|protected|static|override|virtual|sealed|new)\s+)*async\s+[\w<>\[\],.? ]+?\s+(?P<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\(", re.M)
TASK_RE = re.compile(r"^\s*(?:(?:public|internal|protected)\s+)(?:(?:static|override|virtual|abstract|sealed|new)\s+)*(?:ValueTask|Task)(?:<[^;{=]*>)?\s+(?P<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\(", re.M)
BOOL_RE = re.compile(r"^\s*(?:(?:public|internal|protected)\s+)(?:(?:static|readonly|override|virtual|abstract|required|new)\s+)*bool\??\s+(?P<name>[A-Z]\w*)\s*(?P<tail>[{;=(]|=>)", re.M)
FIELD_RE = re.compile(r"^\s*(?:(?:private|internal|public|protected)\s+)?(?:(?:static|readonly|const|volatile)\s+)*[\w<>\[\],.?]+\s+(?P<name>(?:m_|g_|_?(?:str|int|bool|arr|lst|dict|obj|fl|dbl|sz|lp|psz|dw|n|b|f|i)(?=[A-Z][a-z]))\w*)\s*[;=]", re.M)

VAGUE_SUFFIXES = ("Manager", "Helper", "Helpers", "Utils", "Util", "Utilities", "Info", "Data", "Stuff", "Misc", "Common", "Base")
ROLE_SUFFIXES = ("Service", "System", "Controller", "Manager", "Handler", "Handlers", "Processor", "Coordinator", "Director", "Session", "Engine")
BOOL_PREFIXES = ("Is", "Has", "Can", "Should", "Was", "Were", "Will", "Allow", "Allows", "Use", "Uses", "Show", "Shows", "Supports", "Contains", "Needs", "Did", "Does", "Must", "Try", "Enable", "Enabled", "Accepts", "Requires", "Include", "Includes", "Exists", "Equals")
# Abbreviations the guidelines allow: well-known acronyms and Id/Ok. Anything else that looks shortened is reported.
ABBREV_TOKENS = {
    "Mgr", "Ctrl", "Cfg", "Conf", "Btn", "Cnt", "Num", "Idx", "Pos", "Dir", "Dst", "Src", "Tmp", "Temp", "Msg", "Pkt",
    "Res", "Req", "Resp", "Ctx", "Impl", "Obj", "Str", "Buf", "Len", "Val", "Var", "Prev", "Cur", "Curr", "Max", "Min",
    "Calc", "Init", "Util", "Utils", "Misc", "Info", "Desc", "Def", "Defs", "Attr", "Attrs", "Ent", "Comp", "Sys",
    "Hp", "Mp", "Xp", "Exp", "Atk", "Def", "Dmg", "Spd", "Lvl", "Inv", "Eq", "Txt", "Img", "Tex", "Tx", "Rx",
    "Gfx", "Sfx", "Vfx", "Ui", "Hud", "Npc", "Ai", "Db", "Io", "Ms", "Px", "Vb", "Ib", "Vao", "Vbo", "Fbo",
}
# Tokens that are fine in Carbon (domain acronyms used consistently). Reported only in the `abbrev` detail, never
# as violations: the report decides.
ALLOWED_ACRONYMS = {"Hud", "Npc", "Ai", "Ui", "Io", "Vfx", "Sfx", "Hp", "Xp", "Id", "Ids", "Ok", "Gl", "Rng", "Aoi", "Tcp", "Udp"}
VIETNAMESE = re.compile(r"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", re.I)
VIET_WORDS = re.compile(r"\b(?:Nguoi|Choi|Quai|Vat|Pham|Ban|Do|Nha|Lang|Tien|Vang|Kiem|Giap|Mau|Suc|Khoe|Danh|Sach|Cua|Hang|Thanh|Pho)[A-Z]\w*")


def tracked(pattern: str) -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z", "--", pattern], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    return sorted({p for p in out.split("\0") if p})


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    text = re.sub(r'@?"(?:[^"\\\n]|\\.)*"', '""', text)
    return text


def line_of(text: str, index: int) -> int:
    return text.count("\n", 0, index) + 1


def split_words(name: str) -> list[str]:
    return re.findall(r"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+", name)


class Project:
    def __init__(self, path: str):
        self.path = path
        self.dir = os.path.dirname(path)
        tree = ET.parse(os.path.join(ROOT, path))
        props = {el.tag.split("}")[-1]: (el.text or "").strip() for el in tree.iter()}
        self.file_name = os.path.splitext(os.path.basename(path))[0]
        self.assembly = props.get("AssemblyName") or self.file_name
        self.root_ns = props.get("RootNamespace") or self.assembly


def load_projects() -> list[Project]:
    return [Project(p) for p in tracked("*.csproj")]


def owning_project(projects: list[Project], file: str) -> Project | None:
    best = None
    for p in projects:
        if file.startswith(p.dir + "/") and (best is None or len(p.dir) > len(best.dir)):
            best = p
    return best


class Source:
    def __init__(self, path: str, project: Project | None):
        self.path = path
        self.project = project
        with open(os.path.join(ROOT, path), encoding="utf-8") as fh:
            self.raw = fh.read()
        self.code = strip_comments(self.raw)
        self.generated = path.endswith((".g.cs", ".Designer.cs")) or "<auto-generated" in self.raw[:500]
        self.types = []  # (line, indent, mods, kind, name)
        for m in TYPE_RE.finditer(self.code):
            self.types.append((line_of(self.code, m.start()), len(m.group("indent").expandtabs(4)), m.group("mods"), m.group("kind").split()[0], m.group("name")))

    @property
    def layer(self) -> str:
        return self.path.split("/", 1)[0]

    @property
    def top_level_types(self):
        if not self.types:
            return []
        base = min(t[1] for t in self.types)
        return [t for t in self.types if t[1] == base]


def check_namespace(sources, projects):
    for s in sources:
        if s.project is None or s.generated:
            continue
        m = NS_RE.search(s.code)
        if not m:
            continue
        rel_dir = os.path.relpath(os.path.dirname(s.path), s.project.dir)
        expected = s.project.root_ns if rel_dir == "." else s.project.root_ns + "." + rel_dir.replace("/", ".")
        if m.group(1) != expected:
            yield s.path, line_of(s.code, m.start()), f"declares {m.group(1)}; folder implies {expected}"


def check_rootns(sources, projects):
    for p in projects:
        if p.root_ns != p.assembly:
            yield p.path, 1, f"RootNamespace {p.root_ns} != AssemblyName {p.assembly}"
        if p.assembly != p.file_name:
            yield p.path, 1, f"AssemblyName {p.assembly} != project file {p.file_name}"
        folder = os.path.basename(p.dir)
        if folder != p.file_name:
            yield p.path, 1, f"project folder {folder} != project file {p.file_name}"


def check_suffix(sources, projects):
    for s in sources:
        for line, _, mods, kind, name in s.types:
            for suffix in VAGUE_SUFFIXES:
                if name.endswith(suffix) and name != suffix:
                    yield s.path, line, f"{kind} {name} ({suffix})"
                    break


def check_pairs(sources, projects):
    """Same stem with different role suffixes, e.g. TradeService + TradeSystem + TradeController."""
    stems: dict[str, set[tuple[str, str]]] = defaultdict(set)
    for s in sources:
        if s.layer == "tests":
            continue
        for _, _, _, kind, name in s.types:
            if kind == "interface":
                continue
            for suffix in ROLE_SUFFIXES:
                if name.endswith(suffix) and len(name) > len(suffix):
                    stems[name[: -len(suffix)]].add((name, s.path))
    for stem, items in sorted(stems.items()):
        suffixes = {n[len(stem):] for n, _ in items}
        if len(suffixes) > 1:
            yield sorted(items)[0][1], 1, f"{stem}: " + ", ".join(f"{n} ({p})" for n, p in sorted(items))
    by_suffix = Counter()
    for items in stems.values():
        for n, p in items:
            for suffix in ROLE_SUFFIXES:
                if n.endswith(suffix):
                    by_suffix[(p.split("/", 1)[0], suffix)] += 1
                    break
    for (layer, suffix), count in sorted(by_suffix.items()):
        yield layer, 0, f"role suffix census: {suffix} x{count}"


def check_abbrev(sources, projects):
    for s in sources:
        for line, _, _, kind, name in s.types:
            hits = [w for w in split_words(name) if w in ABBREV_TOKENS]
            if hits:
                allowed = all(h in ALLOWED_ACRONYMS for h in hits)
                yield s.path, line, f"{kind} {name}: {','.join(hits)}" + (" (domain acronym)" if allowed else "")


def check_hungarian(sources, projects):
    for s in sources:
        if s.generated:
            continue
        for m in FIELD_RE.finditer(s.code):
            yield s.path, line_of(s.code, m.start()), m.group("name")


def check_iface(sources, projects):
    for s in sources:
        for line, _, _, kind, name in s.types:
            if kind == "interface" and not re.match(r"I[A-Z]", name):
                yield s.path, line, f"interface {name}"
            if kind != "interface" and re.match(r"I[A-Z][a-z]", name):
                yield s.path, line, f"{kind} {name} looks like an interface name"


def check_async(sources, projects):
    for s in sources:
        if s.generated or s.layer == "tests":
            continue
        for regex in (ASYNC_RE, TASK_RE):
            for m in regex.finditer(s.code):
                name = m.group("name")
                if not name.endswith("Async") and name not in ("Main",):
                    line_text = s.code[m.start(): s.code.find("\n", m.start())]
                    if " override " in f" {line_text} " or "[Fact" in line_text:
                        continue
                    yield s.path, line_of(s.code, m.start()), name


def check_bool(sources, projects):
    for s in sources:
        if s.generated or s.layer == "tests":
            continue
        for m in BOOL_RE.finditer(s.code):
            name = m.group("name")
            # Methods that return bool for success (TryX, Resolve, Update) follow the Try pattern, not this rule.
            if m.group("tail") == "(" or name.startswith(BOOL_PREFIXES):
                continue
            yield s.path, line_of(s.code, m.start()), f"property/field {name}"


def check_multitype(sources, projects):
    for s in sources:
        if s.generated:
            continue
        top = s.top_level_types
        public = [t for t in top if "public" in t[2] or ("internal" not in t[2] and "file" not in t[2] and "private" not in t[2] and s.layer != "tests")]
        if len(top) > 1:
            yield s.path, top[1][0], f"{len(top)} top-level types ({len(public)} public): " + ", ".join(t[4] for t in top)


def check_filename(sources, projects):
    for s in sources:
        if s.generated:
            continue
        top = s.top_level_types
        if not top:
            continue
        stem = os.path.basename(s.path)[:-3]
        primary = stem.split(".", 1)[0]
        names = {t[4] for t in top}
        if primary not in names:
            yield s.path, top[0][0], f"file {stem}.cs declares " + ", ".join(sorted(names))


def check_nonascii(sources, projects):
    ident = re.compile(r"\b[A-Za-z_]*[^\x00-\x7f\s\"'`]+[A-Za-z_0-9]*\b")
    for s in sources:
        for i, line in enumerate(s.code.split("\n"), 1):
            for m in ident.finditer(line):
                if VIETNAMESE.search(m.group(0)) or re.search(r"[^\x00-\x7f]", m.group(0)):
                    yield s.path, i, m.group(0)


def check_nonenglish(sources, projects):
    for s in sources:
        for i, line in enumerate(s.code.split("\n"), 1):
            for m in VIET_WORDS.finditer(line):
                yield s.path, i, m.group(0)


def check_folders(sources, projects):
    """Folders holding many files directly (dumping grounds) and folder names reused across projects."""
    per_dir = Counter(os.path.dirname(s.path) for s in sources if s.layer != "tests")
    for d, n in per_dir.most_common():
        if n >= 25:
            yield d, 0, f"{n} files directly in folder"
    leaf_names: dict[str, set[str]] = defaultdict(set)
    for s in sources:
        if s.project is None or s.layer == "tests":
            continue
        rel = os.path.relpath(os.path.dirname(s.path), s.project.dir)
        if rel != ".":
            leaf_names[rel.split("/")[0]].add(s.project.file_name)
    for name, owners in sorted(leaf_names.items()):
        if len(owners) >= 3:
            yield name, 0, f"top folder '{name}' in {len(owners)} projects: " + ", ".join(sorted(owners))


def check_dupnames(sources, projects):
    """The same simple type name declared in two production namespaces."""
    seen: dict[str, list[str]] = defaultdict(list)
    for s in sources:
        if s.layer == "tests" or s.generated:
            continue
        for line, indent, _, kind, name in s.top_level_types:
            seen[name].append(f"{s.path}:{line}")
    for name, where in sorted(seen.items()):
        dirs = {os.path.dirname(w) for w in where}
        if len(dirs) > 1:
            yield where[0].split(":")[0], 0, f"{name}: " + ", ".join(where)


CHECKS = {
    "namespace": check_namespace,
    "rootns": check_rootns,
    "suffix": check_suffix,
    "pairs": check_pairs,
    "abbrev": check_abbrev,
    "hungarian": check_hungarian,
    "iface": check_iface,
    "async": check_async,
    "bool": check_bool,
    "multitype": check_multitype,
    "filename": check_filename,
    "nonascii": check_nonascii,
    "nonenglish": check_nonenglish,
    "folders": check_folders,
    "dupnames": check_dupnames,
}


def main(argv: list[str]) -> int:
    projects = load_projects()
    sources = [Source(p, owning_project(projects, p)) for p in tracked("*.cs") if not p.startswith("tools/")]
    wanted = argv or []
    if wanted == ["all"]:
        wanted = list(CHECKS)
    if not wanted:
        for name, fn in CHECKS.items():
            rows = list(fn(sources, projects))
            by_layer = Counter(r[0].split("/", 1)[0] for r in rows if r[1] != 0 or name in ("folders", "dupnames", "pairs"))
            print(f"{name:11} {len(rows):5}  " + " ".join(f"{k}={v}" for k, v in sorted(by_layer.items())))
        return 0
    for name in wanted:
        if name not in CHECKS:
            print(f"unknown check {name}; choose from {', '.join(CHECKS)}", file=sys.stderr)
            return 2
        for path, line, detail in CHECKS[name](sources, projects):
            print(f"{name}\t{path}:{line}\t{detail}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
