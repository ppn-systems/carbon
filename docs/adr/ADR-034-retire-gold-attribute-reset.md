# ADR-034: Retire the Gold Attribute Reset from the UI and the Wire

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `contracts/Carbon.Contracts.Entities` (`PointAllocationKind`), `server/Carbon.Application/World`
  (`AuthoritativeWorld.TryAllocateCharacterPoint`), `desktop/Carbon.Desktop.Rendering/Hud` (character sheet),
  `desktop/Carbon.Desktop/Scenes` (click routing)
- **Amends**: [ADR-021](ADR-021-data-driven-character-progression.md) D6 (attribute reset on the existing packet).

## Context

The owner's request: "Remove the stat-reset button or UI; a respec scroll item will come later, so it must not appear
on the UI like that."

ADR-021 D6 added `PointAllocationKind.ResetAttributes = 2` on `EntityAllocatePoint` (`0x1204`). The character sheet
showed a two-click "Reset points (price)" button that sent it, and the server refunded every attribute point for the
`respec` gold price. The button was the only sender of kind `2`.

## Decision

1. **D1 — No UI path.** The character sheet loses the reset button, its arm/confirm state and its labels
   (`character.reset`, `character.reset_confirm`, `character.price_gold`, `character.price_free`). The scene no longer
   routes a click to a reset.
2. **D2 — No wire path.** `PointAllocationKind.ResetAttributes` is removed. The server no longer maps kind `2` to a
   reset; it falls through to the existing `OpStatus.Invalid` reply for unknown kinds. Value `2` stays reserved and must
   not be reused, so an old client can never trigger a different action with it. The packet layout and opcode are
   unchanged.
3. **D3 — Keep the refund logic.** `CharacterProgressionService.TryResetAttributes`, `RespecConfig` and
   `CurrencyReason.AttributeReset` stay, unreachable from the network, so the future respec-scroll item can reuse the
   refund. The scroll's own ADR decides whether it still charges gold.

## Alternatives Considered

- **Hide the button but keep kind `2` on the server.** Rejected: any client could still reset points over the wire,
  which is the UI path the owner wants gone.
- **Delete the refund logic too.** Rejected: the respec scroll needs exactly this refund; deleting it now only to add it
  back later is churn.

## Consequences

- Players cannot reset attribute points until the respec scroll ships.
- An outdated client that still sends kind `2` gets `Invalid` and no change.
