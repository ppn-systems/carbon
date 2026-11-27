# ADR-039: No Save Compatibility Before Release

- **Date**: 2026-11-27
- **Status**: Proposed
- **Amends**: [ADR-016](ADR-016-perishable-food-freshness.md) (D2: v1 and v2 inventory payloads no longer load),
  [ADR-023](ADR-023-client-localization-and-vietnamese-font.md) (the legacy `ui_settings.cfg` is no longer read),
  [ADR-035](ADR-035-items-break-at-zero-durability.md) (D7: no 1-durability fix-up on entering the world)
- **Scope**: `engine/Carbon.Items`, `server/Carbon.Application`, `desktop/Carbon.Desktop.Rendering`, `tests/`, `docs/`

## Context

Carbon is still in development. There are no players and no live saves, only developer databases and settings files.
The owner's direction: legacy-compatibility code is dead code and is deleted. The one rule that stays is that a server
started on an existing developer database must not crash. Resetting or migrating an old row is acceptable.

Three readers still exist only for data written by older builds:

- `InventoryBinaryCodec.TryDeserializeInto` reads format v1 and v2 inventory blobs as well as the current v3 (ADR-016 D2,
  and ADR-007 before it). The v1 path also moves a small `InstanceId` into `UpgradeLevel`.
- `UiSettings.LoadOrDefault` reads a legacy `ui_settings.cfg` when no `settings.ini` exists (ADR-023).
- `AuthoritativeWorld.TryEnterWorld` calls `DurabilityWear.RestoreLegacyBrokenItems`, which gives 1 durability to an item
  saved at 0 in the state ADR-035 retired (ADR-035 D7). It also logs a warning and marks the character dirty.

## Decision

1. **D1 — Inventory blobs: v3 only.** `InventoryBinaryCodec` refuses any payload that is not format v3. A refused blob
   already takes the existing reset path. `CharacterPersistence.LoadInventory` grants the class starter loadout (an empty
   bag for a classless character) and flags `StarterLoadoutGranted`. Nothing throws.
2. **D2 — Client settings: `settings.ini` only.** The `ui_settings.cfg` reader is deleted. Without `settings.ini` the
   client starts with default settings, as on a first run.
3. **D3 — No worn-out fix-up.** `DurabilityWear.RestoreLegacyBrokenItems`, `LegacyRestoredDurability` and the entry-time
   call, warning and dirty mark are deleted. A dev row still holding an item at 0 durability loads it as is. Its next wear
   step takes it below 0, which destroys it through the normal break path (ADR-035 D1). Repair refuses it (ADR-035 D8).
4. **D4 — Out of scope.** The following stay as they are:
   - the pre-format-4 `ChunkDelta` rows and their respawn rescheduling, which the ADR-040 work owns;
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
  An old `ui_settings.cfg` is ignored.
- **Follow-up:** once there are real players, a format change needs a migration or a reader again, under its own ADR.
