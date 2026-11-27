# ADR-028: Material Repair of One Item at a Station or Blacksmith

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `contracts/`, `engine/Carbon.Items`, `engine/Carbon.Economy`, `server/`, `desktop/`, `resources/config/repair.json`, `resources/config/economy.json`, `tests/`, `docs/`

## Context

The owner's feedback: repair needs an animation, the player must choose the one item to repair instead of repairing
everything at once, and repair needs materials and maybe other requirements.

How repair worked before this ADR:

- **Gold only, always full.** `VendorService.TryRepair` (`engine/Carbon.Economy/Vendor/VendorService.cs`) restored the
  item to its effective maximum and charged `PriceBook.GetRepairCost`: `basePrice × 30 % × rarity % × missing/max`,
  from `economy.json → repair`. No material was spent, and a partial repair was impossible.
- **The client could not price it.** `economy.json` is server-only (ADR-014 D10), so the only quote the client had was
  the `TraderStock.RepairSlots` / `RepairCosts` arrays a blacksmith sends (ADR-025 D17). Those cover bag slots
  `0..35` only, so equipped armor could not be repaired from the window. A forge anvil sent no quote at all.
- **Click-to-repair.** In the blacksmith window's Repair mode, every click on a quoted bag slot sent
  `RepairInventoryItem` (`0x4009`) at once (`desktop/Carbon.Desktop/Scenes/TradeInteraction.cs:127`). There was no
  item detail, no confirmation and no animation, and a few clicks repaired the whole bag.
- **One rule for every venue.** The server accepted `0x4009` next to any forge artisan NPC or intact forge anvil
  (`InventoryService.IsNearForgeStation`), and charged the same gold at a player-built anvil as at the NPC. A
  workbench could not repair anything, and gear of every tier was repaired at every venue.
- **Wear today.** Only the held weapon or tool wears (`ToolUsagePolicy`). Armor has durability but does not wear yet.

## Decision

1. **D1 — Config `resources/config/repair.json`.** A new document, synced to clients like `enhancement.json` (it is
   not server-only). It holds:
   - `stepPercent` (10): the durability one repair step restores, in percent of the item's maximum. It must divide 100.
   - `tierPercent`: a material multiplier per `items.json` tier (index = tier): `100, 100, 150, 200, 300, 400`.
   - `stations`: the tier of each venue (`workbench` 1, `forgeAnvil` 2, `blacksmith` 3) and their reach
     (`stationReachTiles` 5, `blacksmithReach` 175 px), so the client shows the same "in reach" the server checks.
   - `blacksmithFee`: `percentOfBasePrice` 30, `minimum` 1, `rarityPercent` (the old `economy.json → repair` values).
   - `families`: each has an `id`, a `stationTier`, the `materials` of a full repair of a tier-100 % item, and its
     `items`. Wood tools take logs and plant fibre, stone tools stone and fibre, leather armor leather, iron gear iron
     ingots, crystal gear mana crystals, demon gear iron ingots and demon embers.

   `RepairTable` (`engine/Carbon.Items/Repair`) compiles and validates it against `items.json`: every item with
   durability is in exactly one family, materials are known items without durability, and each family's tier is a
   venue's tier. It is part of `ItemRules` (built by `GameEconomy` on the server and `SyncedItemCatalog` on the
   desktop), so the repair window shows the server's numbers. The `repair` section leaves `economy.json`, and
   `RepairRules` and `PriceBook.GetRepairCost` are removed.
2. **D2 — Quote.** `RepairTable.TryQuote(slot, steps, venue)` returns a `RepairQuote` (allocation-free struct):
   - steps needed = `ceil(missing‰ / (stepPercent × 10))`, where `missing‰ = ceil(missing × 1000 / max)`;
   - a full repair restores exactly the missing durability; a partial one restores `steps × stepPercent %` of max;
   - each material = `ceil(qty × tierPercent × steps / (100 × steps per full repair))`;
   - the blacksmith fee = `max(minimum, ceil(basePrice × percentOfBasePrice % × rarity % × restored‰ / 1000))`. A full
     repair costs exactly what it cost before, so the economy report does not move. A player station charges no gold.
   - The max is the enhanced maximum (`ItemEnhancementPolicy.GetEffectiveMaxDurability`), as before (#88).
3. **D3 — One item, atomically.** `RepairWorkshop.TryRepair` (`engine/Carbon.Economy/Repair`) checks, in order, the
   slot and the expected item id, the quote, a venue in reach, the station tier, every material and the fee. Only then
   does it spend the fee (`CurrencyReason.Repair`) and the materials, exactly once, and write the new durability to the
   selected slot. Any refusal changes nothing.
4. **D4 — Partial repair is allowed, but only when asked for.** The request names a step count from 1 to the steps
   needed. The server never shrinks a request to what the bag holds: a request it cannot pay is refused with nothing
   spent. The window offers "repair fully" and, when materials are short, "repair what I can" (the largest affordable
   count, `RepairTable.GetAffordableSteps`).
   Why: materials come from gathering, not a shop, so a player in the field with two logs should be able to make an
   axe usable again. Asking for the count explicitly keeps the all-or-nothing rule for each request, and the player
   always sees exactly what will be spent.
5. **D5 — No maximum-durability loss per repair (for now).** Decided against the RimWorld/Diablo-style loss.
   - It needs a per-instance maximum on `ItemSlot`, so a new inventory codec version, a persistence migration and a
     wider slot sync.
   - It would punish enhanced gear (ADR-024). Enhancement can already destroy an item from +8, and players put many
     attempts into those items.
   - Repairs already sink materials and gold. Tier progression replaces old gear anyway.
   - Revisit with its own ADR if the economy report shows gear never leaves circulation.
6. **D6 — Venues.** The request names its venue:
   - `Blacksmith`: a forge artisan NPC within `blacksmithReach` (175 px), tier `stations.blacksmith`, fee charged.
   - `Station`: an intact forge anvil within `stationReachTiles` (5 tiles, tier `forgeAnvil`), else a workbench in the
     same reach (tier `workbench`). No fee.
   - None in reach is `OutOfRange`. A tier below the family's `stationTier` is `StationTierTooLow`. With the shipped
     numbers, wood, stone, leather and bone gear repair at a workbench, copper, iron and arcane gear need an anvil, and
     crystal and demon gear need the blacksmith.
7. **D7 — Wire (opcodes `0x4050–0x4057`, append-only).**
   - C2S `RepairItemRequest` (`0x4050`): `SlotIndex`, `ExpectedItemId`, `Steps`, `Venue`. It carries no price or
     amount.
   - S2C `RepairItemResult` (`0x4051`): `Status`, `Error`, `SlotIndex`, `Steps`. The repaired slot, the spent
     materials and the wallet arrive first, as `InventorySlotChanged` and the wallet sync.
   - New contract enums in `Carbon.Contracts.Common`: `RepairVenue` (`None`, `Station`, `Blacksmith`) and
     `RepairError` (`None`, `InvalidRequest`, `EmptySlot`, `ItemMismatch`, `NotRepairable`, `NotDamaged`,
     `TooManySteps`, `OutOfRange`, `StationTierTooLow`, `MissingMaterials`, `NotEnoughGold`). The client maps each
     error to a `[repair]` key in `en.ini` and `vi.ini`; the server sends no text.
   - `0x4052–0x4057` stay free for this feature.
   - `RepairInventoryItem` (`0x4009`) is retired: the server no longer handles it, the packet and constant are deleted
     with the client change, and a comment in `ItemOpCode` keeps the value from being reused.
     `TraderStock.RepairSlots` and `RepairCosts` (`SerializeOrder` 11, 12) stay in the layout but are always empty.
     Client and server ship together.
8. **D8 — Server.** The handler queues the request (ADR-017). `AuthoritativeWorld.TryRepairItem` runs it under the
   world lock and returns the error and steps packed into `WorldMutationOutcome.Revision` (`RepairReceipt`), so the
   command queue stays allocation-free. A refusal is `Conflict`, or `OutOfRange` when no venue is in reach. On success
   the owner gets the changed slots, a stats sync and, when a fee was paid, a wallet sync. The character is marked
   dirty, so the inventory and gold are saved together.
9. **D9 — Persistence.** No schema change and no migration. Durability is already in the inventory blob, and gold is
   in the ADR-012 wallet columns.
10. **D10 — Client.** A repair dialog opens at a blacksmith (from the trade window) or at a workbench or anvil. The
    player selects one damaged item from the bag or the equipment. The dialog shows the current and max durability,
    the amount restored, each material with what the bag holds (missing ones in red), the gold fee at the blacksmith,
    and the station tier the item needs. It reads every number from the synced `repair.json` through
    `ItemRules.Repair`. While the request is in flight it plays a hammer-on-anvil animation in the forge animation's
    style: hammer strikes, sparks, the durability bar filling, then a success flash. The art is vector art
    (`.cvg.json`, ADR-005). The animation is presentation only; the result comes from the server.

## Alternatives Considered

- **Keep gold-only repair and add an animation.** Rejected: the owner asked for materials, and a gold-only sink at a
  player-built anvil has no one to pay.
- **Server quote packet instead of synced config.** Rejected: every hover would need a round trip, and a forge anvil
  has no trader to send a quote. `enhancement.json` already shows that a synced table plus server checks works.
- **Silent partial repair (spend what the bag holds).** Rejected: the player could not tell beforehand what the
  request would cost, and "not enough materials" could not be refused cleanly.
- **One material list per item.** Rejected: 26 items today and more to come. Families keyed by material, scaled by
  tier, keep the table short and consistent.
- **Infer the venue on the server.** Rejected: next to both a blacksmith and an anvil the player must choose whether
  to pay the fee, and the request should state it.

## Consequences

- Positive:
  - Repair is a material sink for gathered resources, not only a gold sink.
  - Each request repairs one chosen item, so the dialog can show it and animate it.
  - The client shows exactly the numbers the server spends.
  - Equipped armor can be repaired.
  - Player stations become useful for low-tier gear.
- Trade-off: the economy simulator (`EconomySimulator`) still counts only the blacksmith's gold fee, because its
  player gathers materials rather than buying them. The fee formula is unchanged for a full repair, so the documented
  sink shares hold.
- Debt: armor does not wear yet, so armor repair has no use until it does. The old `TraderStock` repair arrays can be
  removed in a future protocol bump.
