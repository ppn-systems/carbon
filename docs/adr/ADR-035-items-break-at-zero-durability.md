# ADR-035: Items Break and Are Destroyed at Zero Durability

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-028](ADR-028-material-repair-per-item.md) (an item is repairable only while its durability is above 0)
- **Scope**: `contracts/Carbon.Contracts.Common`, `contracts/Carbon.Contracts.Items`, `engine/Carbon.Items`, `server/`, `desktop/`, `resources/config/repair.json`, `resources/localization/`, `tests/`, `docs/`

## Context

The owner's feedback: when an item's durability runs out, destroy it completely.

How durability worked before this ADR (all on `master` at `11dfed6`):

- **Where durability drops.** Every wear step goes through two methods of `PlayerActionService`
  (`server/Carbon.Application/World/Actions/PlayerActionService.cs`):
  - `WearActiveTool` wears the held hotbar item. It is called for a melee hit on a monster
    (`ToolUsagePolicy.EvaluateCombatStrike`: 1.5 for a weapon, 2.2 for a tool), for every bow or staff shot (0.8), and
    for every strike on a world prop through `PropHarvestService.Strike` (`EvaluatePropStrike`: 1.0 for the right tool,
    up to 6.0 for a sword on stone). Both the tick-path swing and the harvest key (`TryHarvestProp`) strike props.
  - `WearArmor` wears each equipped armor piece (slots `36..39`) by 0.5 whenever a monster hit lands on the player
    (`AuthoritativeWorld.TickAndAoi.cs`, after dodge resolution). Accessories (`40..41`) never wear.
  - Building and demolition do not wear the held tool. The enhancement forge (`ForgeEnhancer`) never lowers durability:
    a success refills it to the new maximum and a downgrade only caps it at the lower maximum. A forge "Destroyed"
    outcome already empties the slot.
  - `docs/economy.md` still said armor does not wear; it did.
- **At 0.** The item stayed in its slot as "worn out". `ToolUsagePolicy.IsWornOut` was true, a worn-out weapon hit for
  `max(8, 45 %)` of its damage and could not crit, a worn-out tool could only forage tier-0 bushes, and
  `CharacterStatCalculator.SumGear` ignored worn-out armor and a worn-out held weapon. The desktop outlined the hotbar
  slot red and showed "Worn Out!" when a slot change crossed 0, and "Mended!" when it rose from 0.
- **Stats.** `CharacterStatCalculator.SumGear` sums armor and accessories in slots `36..41` and the held weapon. The
  server recomputes stats through `IPlayerActionHost.RecomputeDerivedStats`.
- **Replication.** The owner learns every slot change through `InventorySlotChanged` (`0x4030`), staged as
  `OutboundSlotChange` in the tick's `OutboundTickBuffer`. The harvest-key path sends its slot changes directly after the
  world lock.
- **Persistence.** `InventoryBinaryCodec` stores the durability float of every slot. A worn-out item was saved at
  `0` and loaded back at `0`.
- **Repair (ADR-028).** `RepairTable.TryQuote` accepted any durable item with missing durability, including one at 0.

## Decision

1. **D1 — A wear step that reaches 0 destroys the item.** `DurabilityWear.Apply` (`engine/Carbon.Items/Catalog`) is the
   one wear rule. The step that takes a durable item to `<= 0` returns `ItemSlot.Empty`. `WearActiveTool` and
   `WearArmor` write that into the slot in the same tick, so every wear source (melee, bow, staff, tool strike, harvest
   key, armor hit) breaks items the same way. On a break the server:
   - empties the bag slot or armor slot;
   - recomputes the derived stats and sets `StatsDirty` (a held weapon and armor feed `SumGear`);
   - broadcasts the new look (`EntityEquipmentChanged`) to nearby players;
   - marks the character dirty, so the empty slot is saved;
   - pushes the empty slot to the owner (`InventorySlotChanged`) and a break alert (D3).
   The strike that breaks an item still lands with full effect. It is the item's last strike.
2. **D2 — Every durable item breaks, enhanced ones too.** Tools, weapons and armor follow D1, whatever their
   enhancement level. The owner chose this knowingly; see Consequences for the risk.
3. **D3 — `ItemDurabilityAlert` (`0x4090`, S2C, owner only).** Fields: `Kind` (`DurabilityAlertKind`: `AboutToBreak` = 1,
   `Broke` = 2, in `Carbon.Contracts.Common`), `SlotIndex`, `ItemId`, `UpgradeLevel`. Ids only, no prose. The buffer
   dispatches it right after the tick's slot changes, so the client already holds the empty slot when it shows the
   break. On the harvest-key path the slot change goes out at once and the alert with the next tick.
4. **D4 — An early warning before the break.** `repair.json` gains
   `"durabilityWarning": { "lowPercent": 25, "criticalPercent": 10 }`, validated as
   `1 <= critical < low < 100`. It is already synced to clients (ADR-014), so the desktop hardcodes no threshold.
   `RepairTable.GetDurabilityBand` classifies durability against the enhanced maximum: `Healthy`, `Low` (≤ 25 %) or
   `Critical` (≤ 10 %).
   - The server sends `AboutToBreak` when a wear step takes an item from above the critical share to at or below it.
     That fires once per descent with no per-item state. A repair above 10 % arms it again.
   - A step that goes straight from above 10 % to 0 sends only `Broke`. That is possible only for an item whose 10 %
     is smaller than one wear step.
5. **D5 — Client feedback.**
   - `Broke`: a localized "{item} broke!" toast, the same text floating over the player, and an `ItemBreakEvent`. The
     event makes `CombatFeedbackTracker` throw shatter shards (steel, splinter and spark particles, more for an
     enhanced item) at the player. The particles are procedural geometry, not raster art, in the existing
     allocation-free debris ring.
   - `AboutToBreak`: a localized "{item} is about to break, repair it" toast.
   - Durability bars in the inventory, the hotbar and the held-item card, and the tooltip's durability line, are yellow
     in `Low` and red in `Critical`. The hotbar outlines a critical item red, where it used to outline a worn-out one.
   - The text lives in a new `[durability]` section of `en.ini` and `vi.ini`.
6. **D6 — The worn-out path is removed.** `ToolUsagePolicy.IsWornOut`, `ToolStrikeOutcome.WornOutTool` (value 3,
   engine-only, never on the wire), the degraded combat and prop strikes, the worn-out exclusions in `SumGear`, the
   desktop's slot-diff "Worn Out!"/"Mended!" toasts and the `tooltip.broken` line are deleted. Their localization keys
   are deleted from every locale.
7. **D7 — Legacy saves: restore 1 durability, do not delete.** On entering the world, every durable item stored at
   `<= 0` (the retired worn-out state) is given `1` durability, capped at its maximum
   (`DurabilityWear.RestoreLegacyBrokenItems`). This runs before stats are computed. The server logs one warning per
   character with the count, and the character is marked dirty so the fix is saved. Nothing throws. Why restore instead
   of delete:
   - Under the old rules 0 was a stable, repairable state. Players kept worn-out items, often enhanced ones, on
     purpose to repair them later. Deleting them on login would destroy property the player never lost under the
     rules they played by, with no feedback at the moment it happens.
   - At 1 durability the item obeys the new rule at once. It shows red, and its next wear step breaks it with the
     normal alert and effect, unless the player repairs it first.
   - Both options are equally simple. Restoring needs no new alert type for "deleted while offline".
8. **D8 — Repair needs durability above 0.** `RepairTable.TryQuote` returns `NotRepairable` for an item at `<= 0`. After
   D1 and D7 no such item exists. The rule only states the invariant, so a stray 0 can never be repaired back into play.

## Alternatives Considered

- **Keep the worn-out state and only delete on a further strike.** Rejected: the owner asked for destruction at 0, and
  a second hidden threshold would need its own UI.
- **Spare enhanced items (break to +0 or to 1 durability).** Rejected by the owner. It also makes enhanced items
  strictly safer than plain ones, which turns the forge into durability insurance.
- **Delete legacy 0-durability items on load.** Rejected (D7): it punishes players for a state that was legal when they
  saved.
- **Derive the warning on the client from slot changes.** Rejected for the toast. The server already knows the exact
  step, the client can miss a slot change on reconnect, and an id-only alert keeps one source of truth. The bar colours
  are derived on the client from the synced shares, since they only depend on the current durability.
- **A per-item "warned" flag.** Rejected: items have no persistent identity beyond their slot, and the threshold
  crossing gives "once per descent" without storing anything.

## Consequences

- Breaking change for players. Gear is now a consumable that must be repaired in time. This raises the value of the
  repair sink (ADR-028) and of carrying spare tools.
- **Risk: enhanced items are lost for good.** A +10 item that reaches 0 is gone with all the gold and materials spent
  on it, which can be days of play. The warning (D4), red bars and hotbar outline are the safeguards. Watch for player
  reports. If needed, a later ADR can raise `criticalPercent` for enhanced items or add an "auto-unequip at critical"
  option, without changing the wire.
- Armor breaking mid-fight removes its defense and health at once. `RecomputeDerivedStats` clamps current health to the
  new maximum, so a player can lose the health that armor provided.
- `IWorldOutboundSink.SendItemDurabilityAlert` and `IWorldReadModel.DrainItemBreakEvents` are additive with empty
  defaults. Existing sinks and read models compile unchanged.
- The world tick stays allocation-free: the alert list is a reused `OutboundBatch` list, and `DurabilityWear` is a pure
  struct-returning function.
- Opcodes `0x4091–0x4097` stay free in the ADR-035 band. No SQL migration is needed: the save format is unchanged and
  D7 fixes legacy values in memory on load.
