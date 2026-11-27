# Gameplay configuration

Every gameplay config document lives here as `<name>.json`, one document per domain
(`items.json`, `monsters.json`, `loot.json`, `recipes.json`, `needs.json`, `structures.json`, `economy.json`,
`enhancement.json` (forge outcomes, [docs/enhancement.md](../../docs/enhancement.md)), `stamina.json` (action costs,
sprint and regen, [ADR-033](../../docs/adr/ADR-033-hardcore-stamina.md)), ...).

- The **server** loads this folder at startup (`Carbon.Server.Configuration.ConfigRepository`), hashes each
  document and sends clients a manifest. Clients download only documents whose cached hash differs.
- The host loads this folder (override with `CARBON_CONFIG_DIR`) and builds its own domains from it. It hot-reloads
  on file changes (`CARBON_CONFIG_HOT_RELOAD=0` disables): the running server tables and the manifest switch together,
  signed-in clients fetch only the changed documents, and an invalid edit is rejected without changing anything.
  Changes to `structures.json`, `monsters.json` or `spawning.json` need a server restart.
- Clients never ship or hardcode these files. They cache them (`CARBON_CONFIG_CACHE`) and read them from
  `IWorldSession.GameConfig` (`ConfigCatalog`) after sync.
- Names: `[a-z0-9_-]`, max 64 characters. Content: strict JSON (no comments, no trailing commas, no duplicate keys).
- Each domain owns its own file. CI (`ResourcesConfigTests`) rejects invalid JSON or names.

See [ADR-014](../../docs/adr/ADR-014-server-authoritative-config.md).
