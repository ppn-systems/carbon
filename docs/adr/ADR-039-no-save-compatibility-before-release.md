# ADR-039: No Save Compatibility Before Release

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-016](ADR-016-perishable-food-freshness.md) (D2: v1 and v2 inventory payloads no longer load),
  [ADR-023](ADR-023-client-localization-and-vietnamese-font.md) (the legacy `ui_settings.cfg` is no longer read),
  [ADR-035](ADR-035-items-break-at-zero-durability.md) (D7: no 1-durability fix-up on entering the world),
  [ADR-040](ADR-040-tree-regrowth-stages-saplings-and-permanent-stone.md) (D5: chunk delta formats 1–3 are no longer read)
- **Scope**: `engine/Carbon.Items`, `engine/Carbon.Maps`, `server/Carbon.Application`, `desktop/Carbon.Desktop.Rendering`, `tests/`, `docs/`

## Context

Carbon is still in development. There are no players and no live saves, only developer databases and settings files.
The owner's direction: legacy-compatibility code is dead code and is deleted. The one rule that stays is that a server
started on an existing developer database must not crash. Resetting or migrating an old row is acceptable.

Four readers still exist only for data written by older builds:

- `InventoryBinaryCodec.TryDeserializeInto` reads format v1 and v2 inventory blobs as well as the current v3 (ADR-016 D2,
  and ADR-007 before it). The v1 path also moves a small `InstanceId` into `UpgradeLevel`.
- `UiSettings.LoadOrDefault` reads a legacy `ui_settings.cfg` when no `settings.ini` exists (ADR-023).
- `AuthoritativeWorld.TryEnterWorld` calls `DurabilityWear.RestoreLegacyBrokenItems`, which gives 1 durability to an item
  saved at 0 in the state ADR-035 retired (ADR-035 D7). It also logs a warning and marks the character dirty.
- `ChunkDelta.Deserialize` reads chunk delta storage formats 1 to 3, which kept no respawn timers, and
  `ChunkWorld2D.ScheduleLegacyRespawns` reschedules their destroyed props once at boot (ADR-040 D5). Boot
  (`AuthoritativeWorld.InitializeFromStoreAsync`) also has no guard: any row `Deserialize` refuses stops the server.

## Decision

1. **D1 — Inventory blobs: v3 only.** `InventoryBinaryCodec` refuses any payload that is not format v3. A refused blob
   already takes the existing reset path. `CharacterPersistence.LoadInventory` grants the class starter loadout (an empty
   bag for a classless character) and flags `StarterLoadoutGranted`. Nothing throws.
2. **D2 — Client settings: `settings.ini` only.** The `ui_settings.cfg` reader is deleted. Without `settings.ini` the
   client starts with default settings, as on a first run.
3. **D3 — No worn-out fix-up.** `DurabilityWear.RestoreLegacyBrokenItems`, `LegacyRestoredDurability` and the entry-time
   call, warning and dirty mark are deleted. A dev row still holding an item at 0 durability loads it as is. Its next wear
   step takes it below 0, which destroys it through the normal break path (ADR-035 D1). Repair refuses it (ADR-035 D8).
4. **D4 — Chunk deltas: format 4 and later only.** `ChunkDelta.Deserialize` refuses formats 1 to 3 with a
   `FormatException` naming the retired format. `ScheduleLegacyRespawns` (on `ChunkDelta` and `ChunkWorld2D`) is
   deleted. `InitializeFromStoreAsync` catches a `FormatException` per row, logs a warning with the chunk and the reason,
   and skips that row. Its chunk comes back from the seed baseline, and the next save of that chunk replaces the row. This also
   covers a truncated or corrupt row, which used to stop boot.
5. **D5 — Out of scope.** The following stay as they are:
   - the ADR-029 status-effect text format and the needs-poison fallback, which still read a live column;
   - retired-opcode never-reuse guards (ADR-038).
   Each can be removed by a later change under this ADR.

## Alternatives Considered

- **Keep the readers until release.** Rejected: they guard data nobody needs, and every format change adds another branch
  that must be tested.
- **A one-off migration that rewrites old rows to v3.** Rejected: the reset path already exists and loses only developer
  test inventories.

## Consequences

- **Positive:** one inventory layout to read, and no load-time fix-ups. There are fewer branches to test.
- **Trade-off:** a developer database row with a v1 or v2 inventory loses that inventory and gets the starter loadout.
  An old `ui_settings.cfg` is ignored. A chunk saved in format 1 to 3 loses its edits: felled trees, mined rock and
  built structures in it reappear as generated.
- **Follow-up:** once there are real players, a format change needs a migration or a reader again, under its own ADR.
