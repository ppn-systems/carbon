# Localization

Player-facing text for the desktop client ([ADR-020](../../docs/adr/ADR-020-client-localization-and-vietnamese-font.md)).

- `en.json` is the source of truth and the fallback. `vi.json` must have exactly the same keys.
- Each file is a flat `key → string` map. Keys are lowercase and dot-namespaced, for example `hud.not_enough_mana`.
- Placeholders are named: `{count}`, `{name}`. Write `{{` and `}}` for literal braces. Every language must use the same placeholders as `en`.
- A plural string is two keys, `<key>.one` and `<key>.other`. Vietnamese has no plural, so give both the same text.
- Config-derived names use stable ids: `item.<code>.name`, `item.<code>.hint`, `monster.<id>.name` and `biome.<biome>`.
- `locales.json` lists the selectable languages and the font stack each one uses. Every character in a locale file must exist in its fonts.
- CI (`LocalizationTests`) enforces all of the above.

## Adding a string

1. Add `"area.my_key": "English text"` to `en.json` and the translation to `vi.json`.
2. In code: `GameText.Get("area.my_key")`, `GameText.Format("area.my_key", new TextArg("name", value))` or `GameText.Plural("area.my_key", count)`.
3. If you replaced a literal, delete its line from `tests/architecture/Carbon.Architecture.Tests/LocalizationLiteralAllowlist.txt`.
