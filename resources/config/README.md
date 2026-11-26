# Gameplay configuration

Every gameplay config document lives here as `<name>.json`, one document per domain
(`items.json`, `monsters.json`, `loot.json`, `recipes.json`, `needs.json`, `structures.json`, `economy.json`, ...).

- The **server** loads this folder at startup (`Carbon.Server.Configuration.ConfigRepository`), hashes each
  document and sends clients a manifest. Clients download only documents whose cached hash differs.
- Clients never ship or hardcode these files; they read them from `ConfigCatalog` after sync.
- Names: `[a-z0-9_-]`, max 64 characters. Content: strict JSON (no comments, no trailing commas, no duplicate keys).
- Each domain owns its own file. CI (`ResourcesConfigTests`) rejects invalid JSON or names.

See [ADR-011](../../docs/adr/ADR-011-server-authoritative-config.md).
