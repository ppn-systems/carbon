# Localization

Player-facing text for the desktop client ([ADR-023](../../docs/adr/ADR-023-client-localization-and-vietnamese-font.md)).

- `en.ini` is the source of truth and the fallback. `vi.ini` must have exactly the same keys.
- Files are UTF-8 without a BOM. A `[section]` namespaces the keys under it: `[hud]` + `not_enough_mana = ...` defines `hud.not_enough_mana`.
- A line starting with `;` or `#` is a comment. Values are raw text with `\n`, `\t` and `\\` escapes, and a trailing `\` continues the value on the next line.
- Duplicate keys or sections, and malformed lines, fail to load with the file and line.
- **Never start a second copy of a `[section]`.** Add new keys inside the existing section. When two branches both add keys to one section, resolve the git conflict by keeping both sets of lines.
- Placeholders are named: `{count}`, `{name}`. Write `{{` and `}}` for literal braces. Every language must use the same placeholders as `en`.
- A plural string is two keys, `<key>.one` and `<key>.other`. Vietnamese has no plural, so give both the same text.
- Derived names use stable ids: `item.<code>.name`, `item.<code>.hint`, `monster.<id>.name`, `biome.<biome>` and `structure.<structure>`. Read them with `GameText.ItemName`, `MonsterName`, `BiomeName` and `StructureName`. `SyncedItemCatalog.GetDisplayName` already returns localized item names.
- `locales.ini` lists the selectable languages and each one's font stack (`fonts = file@pixelSize, ...`). Every character in a locale file must exist in its fonts.
- CI (`LocalizationTests`, `LocalizationLiteralTests`) enforces all of the above.

## Adding a string

1. Add the key to `en.ini` and the translation to `vi.ini`, in the same `[section]`:

   ```ini
   [hud]
   not_enough_mana = Not enough Mana!
   ```

2. In code, use one of:
   - `GameText.Get("hud.not_enough_mana")`
   - `GameText.Format("hud.picked_up", new TextArg("name", itemName))`
   - `GameText.Plural("hud.coins", count)`

   The namespace is `Carbon.Desktop.Abstractions.Localization`.
3. If you replaced a literal, delete its line from `tests/architecture/Carbon.Architecture.Tests/LocalizationLiteralAllowlist.txt`.
