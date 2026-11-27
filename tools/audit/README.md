# Audit tools

Reusable scripts behind [`docs/audit/2026-11-round3.md`](../../docs/audit/2026-11-round3.md). They are dev-only. No
product project, solution or CI step references them. Each one reports **candidates**. A person still checks every row
against the ADRs before deleting or moving anything (for example, ADR-034 D3 keeps `TryResetAttributes` on purpose).

| Tool | What it finds | Run |
| :--- | :--- | :--- |
| `config_keys.py` | JSON keys in `resources/config` that no DTO reads, DTO properties that only tests read, properties no file sets | `python3 -I tools/audit/config_keys.py` |
| `i18n_keys.py` | Locale keys no C# builds, beyond what `LocalizationTests` already guards; in-flight keys listed separately | `python3 -I tools/audit/i18n_keys.py` |
| `art_entries.py` | Orphan or missing art, catalog ids without art, catalog fields the loader never reads | `python3 -I tools/audit/art_entries.py` |
| `screenshot_scenarios.py` | `CARBON_*` knobs: read but undocumented, documented but unread, handled but never used by a scenario | `python3 -I tools/audit/screenshot_scenarios.py` |
| `find_duplicate_rules.py` | Identical method bodies (`bodies`), the same gameplay constant in several files (`consts`), `resources/` walk-up loops (`walkup`) | `python3 -I tools/audit/find_duplicate_rules.py consts` |
| `field_clusters.py` | Per class: which members read and write which private fields; hub fields; clusters of members that share fields | `python3 -I tools/audit/field_clusters.py MultiplayerSession desktop/Carbon.Desktop.Networking/Session/MultiplayerSession*.cs` |
| `project_graph.py` | Project, package, `HintPath` and `using` edges checked against the AGENTS.md layer rules | `python3 -I tools/audit/project_graph.py` |
| `stale_doc_refs.py` | Backticked paths and symbols in AGENTS.md, README, `docs/` and the skills that no longer exist | `python3 -I tools/audit/stale_doc_refs.py` |
| `naming.py` | Namespace ↔ folder ↔ project drift, vague suffixes (`Manager`, `Helper`), abbreviations, Hungarian names, interface prefix, `Async` suffix, boolean property phrasing, files with several top-level types or a mismatched name, non-English identifiers, dumping-ground folders, duplicate type names. Used by [`docs/audit/2026-11-naming.md`](../../docs/audit/2026-11-naming.md) | `python3 -I tools/audit/naming.py [check ...]` |
| `data_naming.py` | JSON keys that are not camelCase, id value shapes, locale key segments, art file stems, C# JSON naming policies | `python3 -I tools/audit/data_naming.py [json jsonid i18n art policy]` |
| `DeadCode/` | Source-declared types and members that no code references, or that only tests or generated code reference. Also unused parameters of non-virtual methods that are not delegate targets. Covers the public and internal surface that IDE0051/IDE0052/IDE0060/CA1823 cannot see, because those already fail the product build for private members. | `dotnet run --project tools/audit/DeadCode -c Release -- . /tmp/deadcode.tsv` (about 2 minutes, after a restore) |

`DeadCode` loads every `*.csproj` with `MSBuildWorkspace` and binds every expression once. A type counts as used only
when something **outside** its own declaration names it, so a class whose members only call each other is still
reported. Known blind spots:

- reflection and JSON property binding;
- members that only Nalix's packet generator uses (`StaticOpCode`, `[SerializeOrder]` members). Members with any
  attribute are skipped.
- an interface that is implemented but never consumed. The base list counts as a use.

`DeadCode/` has its own `Directory.Build.props` and `Directory.Packages.props`, so the Roslyn workspace packages never
reach the product's central package list. `WireAudit/` and `wire-audit.sh` belong to the protocol session's
[wire audit](../../docs/audit/2026-11-wire-protocol.md).
