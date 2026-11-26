# ADR-015: Survival Needs Persistence

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `server/`, `tests/`, `docs/`

## Context

PR #14 added server-authoritative hunger (`engine/Carbon.Survival`, `docs/survival-needs.md`). The player's `NeedsComponent` lived only in the world simulation, and `TryEnterWorld` always created it full (`NeedsComponent.CreateFull`). Logging out and back in therefore refilled the hunger meter and cleared malnutrition and food poisoning. This made starvation pointless and let players skip food poisoning by reconnecting.

ADR-012 added a reusable, additive `AddCharacterColumnsAsync` migration step and migration `0005_character_wallet`.

## Decision

1. **D1 — Record.** Append the trailing optional members `float NeedsFood = -1`, `float NeedsMalnutrition = 0` and `float NeedsPoisonSeconds = 0` to `CharacterRecord`, so existing call sites keep compiling. A negative `NeedsFood` is a sentinel that means "never saved".
2. **D2 — Schema.** Migration `0006_character_needs` uses the ADR-012 helper in both migrators. It adds the columns `needs_food REAL NOT NULL DEFAULT -1`, `needs_malnutrition REAL NOT NULL DEFAULT 0` and `needs_poison_seconds REAL NOT NULL DEFAULT 0`. `SqliteCharacterStore` and `PgCharacterStore` read and write them.
3. **D3 — Restore.**
   - On world entry, `AuthoritativeWorld.RestoreNeedsLocked` rebuilds the component. A negative `NeedsFood` (existing rows and new characters) gives a fully fed character.
   - Otherwise:
     - Food is clamped to `0..FoodMax`.
     - Malnutrition is clamped to `0..1`.
     - Poison is clamped to `0..PoisonDurationSeconds` of the current `needs.json`.
     - The hunger state is reclassified.
   - Mood is recomputed on the next tick.
4. **D4 — Save.** `BuildUpdatedRecordLocked` writes food, malnutrition and remaining poison. A hunger or mood threshold crossing, a finished meal and starvation damage each mark the character dirty. The record is also built on logout, before the entity despawns, so the latest values are saved without a per-tick write.
5. **D5 — Out of scope.** Timed mood memories and an in-progress meal are not persisted. A meal is cancelled on logout and its item is not consumed. Rest is not persisted while `restEnabled` is false.

## Alternatives Considered

- **Storing needs inside the progression blob.** This would avoid a schema change. It was rejected for the same reasons as ADR-012: it hides the values from SQL and couples them to a codec version.
- **A versioned `needs_blob` column.** This would be more extensible, but three scalars don't justify a codec. If needs grow (rest, memories), a later migration can add one.
- **Simulating needs decay while offline.** This was rejected. Players would come back starving after a weekend. Needs pause while logged out, as in most MMORPG survival modes.

## Consequences

- Logging out no longer resets hunger, malnutrition or poisoning.
- The migration is additive and idempotent, and existing characters spawn fed, as before.
- Values are clamped on load, so changing `needs.json` (for example lowering `foodMax`) cannot load out-of-range state.
