# Gameplay configuration

Every gameplay config document lives here as `<name>.json`, one document per domain
(`items.json`, `monsters.json`, `loot.json`, `recipes.json`, `needs.json`, `structures.json`, `economy.json`, ...).

- The **server** loads this folder at startup (`Carbon.Server.Configuration.ConfigRepository`), hashes each
  document and sends clients a manifest. Clients download only documents whose cached hash differs.
- The host loads this folder (override with `CARBON_CONFIG_DIR`) and builds its own domains from it. With
  `CARBON_CONFIG_HOT_RELOAD=1` it reloads on file changes; signed-in clients receive the new manifest and fetch only
  the changed documents.
- Clients never ship or hardcode these files. They cache them (`CARBON_CONFIG_CACHE`) and read them from
  `IWorldSession.GameConfig` (`ConfigCatalog`) after sync.
- Names: `[a-z0-9_-]`, max 64 characters. Content: strict JSON (no comments, no trailing commas, no duplicate keys).
- Each domain owns its own file. CI (`ResourcesConfigTests`) rejects invalid JSON or names.

See [ADR-014](../../docs/adr/ADR-014-server-authoritative-config.md).
