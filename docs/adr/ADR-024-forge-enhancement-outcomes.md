# ADR-024: Forge Enhancement Outcomes, Protection Scrolls and Result Packets

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amended by**: [ADR-026](ADR-026-server-secret-gameplay-entropy.md) — gameplay rolls use a server-only secret, not the world seed
- **Scope**: `contracts/`, `engine/Carbon.Items`, `engine/Carbon.Economy`, `server/`, `desktop/`, `resources/config/enhancement.json`, `tests/`, `docs/`

## Context

The forge raised `ItemSlot.UpgradeLevel` (+0..+10) with chances and materials hardcoded in
`ItemEnhancementPolicy.CanUpgrade`. A failure only lost the materials. The server rolled with `Random.Shared` unless a
test seed was passed, charged no gold, and replied with a bare `MutationResult`, so the desktop could not show what
happened. The owner asked for MMO-style outcomes: a failure can leave the item unchanged, drop a level, or destroy it.
Design and numbers: [docs/enhancement.md](../enhancement.md).

## Decision

1. **D1 — Config.** A new document `resources/config/enhancement.json` holds one row per current level: success,
   no-change, downgrade and destroy weights (per mille, sum 1000), materials and base gold. It also holds
   `downgradeMinLevel` and `destroyMinLevel` (safe levels), gold percent per rarity, the protection scrolls, and the
   announcement thresholds. `EnhancementTable` (`engine/Carbon.Items/Enhancement`) compiles and validates it against
   `items.json`. A row that would downgrade or destroy below its threshold is rejected. It is part of `ItemRules`
   (built by `GameEconomy` on the server and `SyncedItemCatalog` on the desktop) and is **not** server-only, so the
   forge window shows the server's chances. It hot-reloads with the economy (ADR-014 D12).
2. **D2 — Engine.** `ForgeEnhancer.TryEnhance` (`engine/Carbon.Economy/Enhancement`) runs one attempt atomically.
   It checks every cost first, then spends the gold fee (`CurrencyReason.Enhancement = 11`), the materials and the
   optional scroll exactly once, rolls on a `DeterministicRandom` from `CreateRandom(worldSeed, tick, player, nonce)`,
   and writes the outcome to the slot. `ItemEnhancementPolicy.CanUpgrade` is replaced by `TryGetStep` / `GetGoldFee`.
   The stat and durability multipliers are unchanged.
3. **D3 — Wire (append-only).**
   - `UpgradeInventoryItem` (`0x4005`) appends `[SerializeOrder(1)] ushort ProtectionItemId` (0 = none). It still carries
     no outcome or seed.
   - New S2C `EnhancementResult` (`0x4031`) replaces the `MutationResult` reply to `0x4005`. It carries `Status`,
     `SlotIndex`, `ItemId`, `Outcome`, `PreviousLevel`, `NewLevel`, `IsDestroyed` and `ProtectionConsumed`.
   - New S2C `EnhancementAnnouncement` (`0x4032`): `PlayerNetworkId`, `PlayerName`, `ItemId`, `Outcome`, `Level`.
   - New contract enum `EnhancementOutcome` (`None`, `Success`, `NoChange`, `Downgrade`, `Destroyed`) in
     `Carbon.Contracts.Common`.
   - An old client waiting for `MutationResult` on `0x4005` times out. Client and server ship together.
4. **D4 — Server.** The handler queues the attempt (ADR-017). `AuthoritativeWorld.TryEnhanceInventoryItem` returns the
   outcome packed into `WorldMutationOutcome.Revision` (`ForgeEnhancementReceipt`), so the command queue stays
   allocation-free. The owner gets slot, stats and wallet syncs. A success reaching `announce.successMinLevel` (+7), or a
   destroyed item, is sent to every player in the world.
5. **D5 — Persistence.** No schema change. The level is already in the inventory blob (`InventoryBinaryCodec` v2), a
   destroyed item is an empty slot, and gold is in the ADR-012 wallet columns. Both are marked dirty in the same
   mutation, so they are saved together.
6. **D6 — Items.** New items `forge_ward_scroll` (205, vendor) and `blessed_forge_scroll` (206, alchemy recipe 60), each
   with vector art (ADR-005).

## Alternatives Considered

- **Keep `MutationResult` and encode the outcome in `Revision`.** Rejected: the meaning of `Revision` would depend on
  the request opcode on the client, and the destroyed and scroll flags would still need packing there.
- **Client-side roll for instant feedback.** Rejected: the server is authoritative. The client plays the charge-up
  animation while it waits for the result.
- **Scrolls applied automatically when owned.** Rejected: players must choose to spend a sink item, and the server needs
  an explicit, checkable request.
- **Break-on-fail from +5.** Rejected: too punishing next to the current gold income (docs/economy.md). Breaking is
  limited to +8 and up.

## Consequences

- Positive: every forge number is in config and validated; the desktop shows the same table the server runs;
  attempts are replayable from the seed, tick, player and nonce; gold and scrolls add sinks at the top of progression.
- Trade-off: the per-world attempt nonce is in memory. After a restart the streams differ, which is fine because
  outcomes are only replayable within one server run.
- Debt: `EnhancementAnnouncement` has no chat log on the client yet; it is shown as a HUD notification.
