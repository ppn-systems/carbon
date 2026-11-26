# ADR-018: Rest Need — Sleep and Sit

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `contracts/`, `engine/Carbon.Survival`, `server/`, `desktop/`, `resources/config/needs.json`, `tests/`, `docs/`

## Context

PR #14 shipped the rest need (`NeedsComponent.Rest`, Tired / VeryTired / Exhausted) but disabled it in `needs.json`. The game had no way to recover rest, so an enabled need would only ever drain. The building system already provides a `wood_bed` structure and `BuildingGrid.IsRoofedAt` (enclosed and roofed, via `RoomDetector`). ADR-015 added additive survival columns and the restore/clamp pattern for needs.

## Decision

1. **D1 — Wire.**
   - Add the C2S `EntityRestRequest` (`EntityOpCode.RestRequest = 0x1205`) with `bool Rest`. It is tick-queued (ADR-017) and answered with `MutationResult`.
   - The server chooses the mode, so clients can't claim a bed they're not at.
   - Add the contract enum `RestMode` (`None`, `Sitting`, `Sleeping`, `SleepingComfortable`).
   - Append `Rest`, `MaxRest`, `RestState` and `RestMode` to `PlayerStatsSync` (orders 25–28). Existing fields keep their positions.
2. **D2 — Engine.**
   - Add `NeedsActivity.Sleeping` and `NeedsComponent.SleepComfort`. `Resting` now means sitting.
   - New `needs.json` values: `sitRecoveryMultiplier` (0.3), `comfortRecoveryMultiplier` (1.5), `bedReachTiles` (1.6) and `veryTiredSpeedScale` (0.93).
   - Recovery is `restRecoveryPerMinute` (10) times 0.3 when sitting, 1 when sleeping, and 1.5 when sleeping comfortably.
   - Penalties: VeryTired slows movement and attacks to ×0.93. Exhausted slows them to ×0.85 and **blocks health regen**.
   - Food drains at the resting rate while sleeping.
   - `restEnabled` becomes `true`.
3. **D3 — Server authority (`AuthoritativeWorld.Survival.cs`).**
   - **Choosing the mode:** `TrySetResting(rest: true)` picks the nearest intact `wood_bed` within `bedReachTiles`. If the bed tile is roofed and enclosed (`IsRoofedAt`) the mode is `SleepingComfortable`, otherwise `Sleeping`. With no bed in reach the player sits.
   - **Waking:** the player stands up on movement input, a real attack, harvest or build action (not eating), damage, death, or when the bed is destroyed.
   - **Each tick:** the server writes the matching `Activity` and `SleepComfort` into the player's `NeedsComponent`.
4. **D4 — Persistence.**
   - Append `CharacterRecord.NeedsRest` (default `-1` = never saved, spawns fully rested).
   - Migration `0007_character_rest` adds `needs_rest REAL NOT NULL DEFAULT -1` in SQLite and PostgreSQL, through the ADR-012 `AddCharacterColumnsAsync` step.
   - On load the value is clamped to `0..RestMax`. Rest pauses while offline.
5. **D5 — Client.**
   - `T` toggles rest through `IGameplayController.TryToggleRest`. The key is listed in the Settings controls.
   - The vitals cluster gains a fifth bar for rest, with labels such as `Zzz+ 45%`.

## Alternatives Considered

- **Client-sent mode (sit vs. sleep).** Rejected: the server would still have to check the bed, and the client would need a second round trip whenever its guess was wrong.
- **Movement-locked sleep (ignore input while asleep).** Rejected: in an MMO, being unable to flee is a griefing vector. Input wakes the player instead.
- **A per-room comfort score (RimWorld beauty and space).** Deferred. Being enclosed and roofed is a clear first rule, and the room code already computes and caches it.

## Consequences

- Rest now matters: players who stay out for two hours slow down and stop regenerating health until they sleep. Building a roofed bedroom pays off.
- Additive wire and schema changes. Old rows spawn fully rested.
- `PlayerNeedsSnapshot` gains four trailing optional members, so existing sink implementations compile unchanged.
- The handler calls `IWorldEntityCommandSink.SubmitSetRestingAsync`, which queues a `WorldCommandKind.SetResting` command. The tick runs it through the world-side `TrySetResting` (ADR-017 D7), so the mutation never happens on an I/O thread.
