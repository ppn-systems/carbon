# ADR-011: Server-Authoritative Gameplay Config with Hash-Based Sync

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `resources/config/`, `contracts/Carbon.Contracts.Config`, `engine/Carbon.Configuration`, `server/Carbon.Server.Configuration`, `tests/`

## Context

Gameplay data (items, monsters, loot, recipes, needs, structures, economy) is being added by several domains at once.
Without a rule, each domain would pick its own location and loading path, and the desktop client would end up with
hardcoded tables or bundled copies that drift from the server. The owner's rule is explicit: *the server decides and
never trusts the client; the server sends configs to the client, the client stores and uses them; when a hash differs
the server sends the new config; never hardcode config into desktop.*

Existing building blocks:
- `engine/Carbon.Data/Serialization/JsonDefinitionLoader.cs` loads a UTF-8 JSON array into a `DefinitionRegistry`.
- `contracts/Carbon.Contracts.Maps/C2S/MapChunkRequest.cs` and `S2C/MapChunkDelta.cs` set the request/stream packet pattern
  (`[SerializeOrder]` layout, `IPacketStreamable` + `IPacketStreamTerminator`).
- `Directory.Packages.props` has no hashing package (`System.IO.Hashing` is not allowed), and `engine/Carbon.Primitives/Hashing/`
  only contains `SpatialHash`.

## Decision

1. **D1 — One folder, one document per domain.** All gameplay config lives in `resources/config/<name>.json`, one document per
   domain (`items.json`, `monsters.json`, `loot.json`, `recipes.json`, `needs.json`, `structures.json`, `economy.json`, ...).
   Names match `[a-z0-9_-]{1,64}` starting with a letter or digit (`ConfigDocumentName`). Content is strict JSON: no comments,
   no trailing commas, no duplicate keys. Each domain owns its file and its shape.
2. **D2 — The server is the only source.** `ConfigRepository` (`server/Carbon.Server.Configuration`) loads the folder at startup
   and fails fast on any invalid file. It canonicalizes each document (re-serialized without insignificant whitespace, one
   escaping policy, property order kept), hashes the canonical bytes, and keeps an immutable snapshot (manifest + `ConfigCatalog`).
   Server-side domain code reads `ConfigRepository.Catalog`.
3. **D3 — Content hash.** 64-bit FNV-1a (`Carbon.Primitives.Hashing.Fnv1a64`) over the canonical UTF-8 bytes, reached only through
   `ConfigContentHash.Compute`. It detects staleness and corruption; it is not a security boundary against a malicious server.
4. **D4 — Wire protocol** (`contracts/Carbon.Contracts.Config`, opcode band `0x0200–0x02FF`):
   - S2C `ConfigManifest` (`0x0210`): `ManifestVersion` + parallel arrays `Names`, `ContentHashes`, `ByteLengths`, `Versions`.
     Sent before world entry and pushed again whenever a reload changes the manifest.
   - C2S `ConfigRequest` (`0x0201`): `ManifestVersion` + `Names` of the documents whose cached hash differs or is missing.
   - S2C `ConfigDocument` (`0x0220`, stream): `Name`, `ContentHash`, `Version`, `TotalLength`, `Offset`, `Payload` chunks of at most
     16 KiB, ending with one terminator packet.
   - Limits (`ConfigProtocolLimits`): 64 documents, 64-character names, 4 MiB per document.
5. **D5 — The server never trusts requests.** `ConfigRepository.HandleRequestAsync` rejects, without throwing and without sending,
   requests with too many names, invalid or duplicate names, unknown names, or a stale manifest version (the host then resends the
   current manifest).
6. **D6 — The client never trusts bytes.** `ConfigSyncClient` (`engine/Carbon.Configuration`) compares each manifest entry with a
   hash-verified cache (`ConfigDocumentStore` over `IConfigCacheStorage`, with file-system and in-memory implementations).
   It requests only mismatched or missing documents, accepts only chunks of requested documents whose metadata matches the manifest
   and whose offsets are contiguous, and accepts a finished document only when its hash matches the manifest. Rejected documents
   are re-requested at the end of the stream, up to 3 attempts, then the sync faults. Verified documents are persisted and exposed
   through `ConfigCatalog`; the cache is re-hashed on every read, so a corrupt cache file is a miss, never an input.
7. **D7 — Hot reload.** `ConfigRepository.Reload()` builds a new snapshot. Unchanged documents keep their version; changed ones get
   `version + 1`; any change bumps `ManifestVersion` and raises `ManifestChanged` so the host pushes the new manifest. An invalid
   file during reload throws and the previous snapshot stays live.
8. **D8 — Desktop holds no config.** Client code obtains config only from `IGameConfigSource` (`IWorldSession.GameConfig`,
   `GameConfigChanged`, `WaitForGameConfigAsync`), reading documents through `ConfigCatalog.GetUtf8` or `ConfigCatalog.LoadRegistry`.
   No config file is bundled with or hardcoded into `desktop/` or `wasm/`.
9. **D9 — Host wiring.**
   - `Carbon.Host` loads `ConfigRepository` from `CARBON_CONFIG_DIR`, or else from the first `resources/config` found above the
     working or executable directory. It fails fast on invalid config.
   - `ConfigHotReloadWatcher` reloads it, debounced 500 ms, when a `*.json` file changes. An invalid edit is logged and ignored.
   - `IdentityHandlers` sends the manifest right after a successful login or register. `ConfigManifestBroadcaster` pushes it to
     every signed-in stream connection on `ManifestChanged`.
   - `ConfigHandlers` answers `ConfigRequest`. A rejected request still gets a stream terminator, and a stale one gets the
     current manifest first.
   - `MultiplayerSession` subscribes to `ConfigManifest`, runs one sync pump (a newer manifest supersedes an older one) over a
     `FileSystemConfigCacheStorage` in `DesktopOptions.ConfigCacheDirectory` (`CARBON_CONFIG_CACHE`). `EnterWorldAsync` refuses to
     enter the world until the current connection's config is verified.
   - Clients sharing one cache directory write through unique temp files, and a failed cache write never fails a sync.

## Alternatives Considered

- **Bundle config with the client build.** Rejected: drifts from the server, needs a client release for every balance change, and
  breaks the owner's rule.
- **Send all config on every login.** Rejected: wasted bandwidth; hashes make reconnects free.
- **`System.IO.Hashing` (XxHash64).** Rejected for now: it is a new package dependency; FNV-1a-64 is a few lines, platform-independent
  and sufficient for change detection.
- **Hash raw file bytes.** Rejected: formatting-only edits would force every client to re-download.
- **Per-document request/response packets.** Rejected: one request plus one stream matches the existing map/inventory stream pattern.

## Consequences

- Positive: every domain has one place for its data, CI (`ResourcesConfigTests`) rejects invalid JSON or names in
  `resources/config/`, clients download only what changed, and nothing on the client is trusted.
- Positive: client sync logic is transport-agnostic and fully unit-tested without a network.
- Trade-off: entering the world now waits for config sync (up to 15 s). A server that sends no manifest cannot be joined.
- Debt: shared gameplay tables are still compiled into `engine/Carbon.Items/Catalog` (`StarterGameCatalog`, `WorldActorCatalog`,
  `SkillCatalog`, `ItemEnhancementPolicy`, `ToolUsagePolicy`, `CharacterProgressionPolicy`), and the desktop reads them directly.
  Each domain moves its table into `resources/config/<domain>.json` and loads it from `ConfigCatalog` on both sides.
- Trade-off: FNV-1a is not cryptographic; integrity relies on the server connection, not on the hash.
- Debt: no compression; documents up to 4 MiB are sent as plain chunks.
