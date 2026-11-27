# ADR-023: Client Localization (en default, vi) and Vietnamese Font Coverage

- **Date**: 2026-11-27
- **Status**: Proposed
- **Scope**: `resources/localization/`, `desktop/Carbon.Desktop.Abstractions/Configuration` (INI), `resources/fonts/`, `desktop/Carbon.Desktop.Abstractions`, `desktop/Carbon.Desktop.Rendering`, `graphics/Carbon.Graphics.Fonts`, `tests/`, `tools/screenshots/`, `docs/`

## Context

The owner asked for real i18n: English stays the default, Vietnamese comes first, other languages later.

- **No i18n exists.** User-facing English literals live in desktop code, for example `_hudNotifications.Add("Not enough Mana!")` in `desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Actions.cs`, and the labels in `desktop/Carbon.Desktop.Rendering/Hud/SettingsModalWidget.cs`.
- **Gameplay names come from server config (ADR-014).** These are `items.json` `name` and `actionHint`, and `monsters.json` `displayName`. The server copies them into `EntitySpawn.Name` (`server/Carbon.Application/World/AuthoritativeWorld.cs:715-724`), so prose crosses the wire.
- **The font has no Vietnamese.** The only font, `resources/fonts/thaleah_fat.ttf`, maps 106 code points: ASCII plus a few Latin-1 symbols. It has none of `ă â đ ê ô ơ ư` or the precomposed tone letters U+1EA0–U+1EF9.
- **The rasterizer bakes ASCII only.** `TrueTypeFontLoader.RasterizeAscii` (`graphics/Carbon.Graphics.Fonts/TrueType/TrueTypeFontLoader.cs:162`) bakes code points 32..126 into a 256 px wide atlas.
- **Constraints.** Native AOT and trimming (no reflection-based loading, source-generated `System.Text.Json`). No hot-path allocations. `graphics/` and `desktop/` must never be referenced by `server/`.

## Decision

1. **D1 — Locale files (INI).**
   - `resources/localization/en.ini` is the source of truth and the fallback. `vi.ini` is the first translation.
   - Files are UTF-8 without a BOM. A `[section]` namespaces the keys under it, so `[hud]` + `not_enough_mana = Not enough Mana!` defines `hud.not_enough_mana`.
   - Full keys are lowercase and dot-namespaced (`^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`), for example `hud.not_enough_mana`, `item.iron_sword.name` and `biome.lush_plains`.
   - `resources/localization/locales.ini` lists the selectable locales. Each `[locale.<code>]` section gives a native `name` and a font stack in `fonts` (D5). Adding a language means adding one section and one `<code>.ini` file.
   - Values are NFC-normalized, so Vietnamese uses precomposed letters (`ế`, not `e` + U+0302 + U+0301).
   - **The parser** is `IniDocument` in `desktop/Carbon.Desktop.Abstractions/Configuration`. It is small, hand-written and AOT-safe, and needs no package. Its rules:
     - a line starting with `;` or `#` is a comment, and values are otherwise raw text;
     - whitespace around `=` is trimmed;
     - values support the escapes `\n`, `\t` and `\\`, plus a trailing `\` for line continuation;
     - section and key names match `[a-z0-9_.]+`;
     - duplicate sections, duplicate keys, malformed lines, unknown escapes and a BOM are load errors that name the file and line.
   - **Why INI.** The owner asked for INI. It suits hand-edited player text: translators edit it without JSON quoting or escaping, comments can carry context for a string, and sections remove the repeated key prefixes, so diffs stay one line per string. Gameplay configs in `resources/config` stay JSON (ADR-014); that is outside this ADR.
2. **D2 — Placeholders and plurals.**
   - **Placeholders.** Values use named placeholders such as `{count}` and `{name}`. `{{` and `}}` escape literal braces.
   - **Precompiled.** Each value is parsed once at load into a `TextTemplate`, a list of literal and argument segments. Formatting never re-parses the string.
   - **Arguments.** Arguments are `TextArg` structs passed as `params ReadOnlySpan<TextArg>`, so a call allocates no argument array. Numbers are formatted with the invariant culture.
   - **Plurals.** A plural string is stored as `<key>.one` and `<key>.other`. `Plural(key, count)` picks the form from the language's rule: English uses `one` for exactly 1 and `other` otherwise. Vietnamese has no grammatical plural and always picks `other`, but `vi.ini` still carries both forms so the key sets match exactly (D7).
3. **D3 — Localizer (client only).**
   - **Where.** `ILocalizer` and `Localizer` live in `desktop/Carbon.Desktop.Abstractions/Localization`. Both desktop networking and rendering can use them; the server cannot.
   - **Loading.** Locale files are read with `IniDocument` into `Dictionary<string, string>`. There is no reflection, no serializer and no embedded-resource lookup.
   - **Fallback.** A missing key falls back to `en`, then to the key itself. Each missing key is logged once per language.
   - **Language choice.** The `CARBON_LANG` environment variable wins. Otherwise the saved setting applies, otherwise `en`. An unknown code falls back to `en`.
   - **User settings file.** Per-user client settings are INI too: `settings.ini` in the user's local config directory (`%LOCALAPPDATA%/Carbon`, or `~/.local/share/Carbon` on Linux). It holds `[general] language = vi` and the `[display]` toggles. The legacy `ui_settings.cfg` is read once when `settings.ini` does not exist yet.
   - **Switching.** The Settings modal has a Language row that cycles through the locales in `locales.ini`. The choice is saved, and the HUD reloads its font for the new font stack.
   - **Ambient access.** `GameText.Current` is the process-wide localizer, so widgets created with `new()` need no constructor changes.
   - **Allocation rules.** `Get` returns the string stored at load, so there is no allocation per frame. `Format` and `Plural` allocate one string, so per-frame callers cache the result and re-format only when an argument or `ILocalizer.Version` changes.
4. **D4 — Server stays language-neutral.**
   - The server never sends translated prose. It sends stable ids, or message keys with arguments, and the client localizes them.
   - Display keys are derived from stable config ids: `item.<code>.name`, `item.<code>.hint`, `monster.<id>.name` and `biome.<biome_snake_case>`.
   - Config `name`, `displayName` and `actionHint` fields stay as the English fallback for tools and logs.
   - Moving `EntitySpawn.Name` and server notices to keys is a follow-up PR. It changes what the field means but not the wire layout, and it gets its own section in this ADR when it lands.
5. **D5 — Fonts.**
   - **Bundled font.** `resources/fonts/vt323.ttf` ships with its license in `resources/fonts/OFL-VT323.txt`. VT323 (Copyright 2011 The VT323 Project Authors) is licensed under the SIL Open Font License 1.1, which allows redistribution in a public repository. It covers ASCII, Latin-1 Supplement, Latin Extended-A, the Vietnamese letters of Latin Extended-B, the combining marks used by Vietnamese, and all of U+1EA0–U+1EF9. Nothing is copied from `resources/thirdparty/`.
   - **Multi-face rasterizing.** `TrueTypeFontLoader.Rasterize` takes an ordered list of faces and code-point ranges. A face supplies only the glyphs that the faces before it lack. Every face is aligned to the primary face's baseline and line metrics.
   - **Atlas growth.** The atlas widens to 512 px when more than 256 glyphs are baked.
   - **Baked ranges.** Basic Latin, Latin-1 Supplement, Latin Extended-A and B, Combining Diacritical Marks, Latin Extended Additional (U+1E00–U+1EFF), and General Punctuation (U+2010–U+203A).
   - **Per-locale font stacks** in `locales.ini` (`fonts = file@pixelSize, ...`, primary first):
     - `en` keeps `thaleah_fat.ttf` at 16 px, so the English look does not change. VT323 is a fallback at 12.5 px (cap height 7 px, the same as Thaleah's) for stray accented characters such as player names.
     - `vi` uses VT323 at 20 px for every glyph, so tone marks stay legible and every word comes from one face.
6. **D6 — Migration in follow-up PRs.**
   - This ADR's first PR ships only the infrastructure: localizer, locale files, fonts, guards and the Language setting.
   - Each later PR replaces a group of desktop and graphics UI literals with keys and removes their allowlist entries. The groups are notifications, login and character screens, HUD and modals, and server-originated text.
7. **D7 — CI guards** (`tests/architecture/Carbon.Architecture.Tests/Localization*`):
   - locale files parse with `IniDocument` (no duplicates or malformed lines);
   - every key literal used in `desktop/`, `graphics/` or `server/` code exists in `en.ini`;
   - every locale has exactly the `en` key set;
   - placeholders match `en` for every key;
   - every `en` key is used, either as a literal in code or derived from a config id or `BiomeType` name;
   - values are NFC and use valid template syntax;
   - every character in a locale file has a glyph in that locale's font stack;
   - no new hardcoded user-facing literals appear in desktop or graphics UI code. A Roslyn scan checks this against `LocalizationLiteralAllowlist.txt`, and an allowlist entry that no longer matches fails the test, so the list can only shrink.
8. **D8 — Screenshots.** `tools/screenshots/capture.sh` gains `--lang <code>`, which sets `CARBON_LANG` for the client, so the main screens can be captured in both `en` and `vi`.

## Alternatives Considered

- **Flat JSON (`en.json`).** It was the first draft of this ADR. Rejected at the owner's request in favour of INI (D1): JSON has no comments, needs quoting and escaping for every string, and repeats the full key prefix on every line.
- **.NET `.resx` / `ResourceManager`.** Rejected: satellite-assembly lookup relies on reflection and trimming roots. Non-developers also edit `.resx` with more difficulty than INI, and the files cannot be shared with a future WASM client as plain data.
- **An INI NuGet package.** Rejected: the format needs about 200 lines to parse, and a package would add a dependency and AOT review for no gain.
- **ICU MessageFormat (`{count, plural, one {...} other {...}}`).** Rejected for now: it needs a much larger parser than two languages require. The `.one`/`.other` key suffix maps one-to-one onto CLDR categories, so a later ICU migration stays mechanical.
- **Server-side translation by the player's language.** Rejected: it ties server output to client settings, breaks serialize-once AoI broadcasts, and conflicts with D4.
- **VT323 as the only font for every locale.** Rejected: it would change the established English look for no gain. Per-locale stacks keep English as it is.
- **Tiny5 (OFL, Vietnamese subset).** Rejected: its 5 px cap height leaves too little vertical room for stacked Vietnamese tone marks, and its variable-font file is three times larger.
- **A compile-time generated key class.** Deferred: string keys plus the D7 guards catch the same mistakes without adding a source generator to every desktop project.

## Consequences

- **Positive:** Vietnamese renders with correct diacritics, and other sessions can adopt keys right away. The server stays language-neutral and AOT-safe, and missing or stale translations fail CI instead of shipping.
- **Trade-off:** In Vietnamese the UI uses a mixed-case terminal-style face instead of the all-caps Thaleah face, and 20 px lines are taller than 16 px. Fixed-width widgets may need wider slots, which the screenshot pass (D8) checks.
- **Trade-off:** `GameText.Current` is ambient mutable state in the desktop layer. This is acceptable on the client, where `CodeHygieneTests` does not apply, and it keeps widget constructors unchanged.
- **Debt:** The literal allowlist starts with every current UI literal. It must reach zero through the D6 follow-up PRs.
