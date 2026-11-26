# ADR-016: Perishable Food Freshness

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `engine/Carbon.Items`, `engine/Carbon.Survival`, `contracts/`, `server/`, `desktop/`, `resources/`, `tests/`

## Context

The RimWorld-style food chain needs raw food to spoil over time. `ItemSlot` (`engine/Carbon.Items/Containers/ItemSlot.cs`) had no field for a countdown. Reusing `Durability` was rejected during review: a field whose meaning depends on the item type is a trap for every reader, and the repair and wear sinks added in ADR-012 would quietly misread it.

## Decision

1. **D1 — Domain field.** `ItemSlot` gets a trailing optional `float FreshnessSeconds` (`0` = not perishable, or perishable but not stamped yet) and an `IsPerishable` helper.
   - **Merging stacks:** when stacks merge (`ItemSlot.MergeWith`, used by `InventoryContainer`, `InventoryDraft` and `PlayerInventoryLayout`), the result keeps the **lowest** non-zero freshness. A zero (unstamped) stack takes the other stack's value.
2. **D2 — Persistence.** `InventoryBinaryCodec` writes format **v3**: 27-byte entries, the v2 layout plus a trailing `float` freshness.
   - v2 and v1 payloads still load, as not perishable.
   - Negative or non-finite stored values load as `0`.
   - No database schema change: the inventory blob carries the field.
3. **D3 — Wire.**
   - `InventorySlotChanged` gets `FreshnessSeconds` at serialize order 5, and `InventorySlotItem` at order 7. Both are appended, so existing fields keep their positions.
   - `IWorldOutboundSink.SendInventorySlotChanged` gets a trailing optional `freshnessSeconds`.
4. **D4 — Data.** `food.json` entries get `spoilSeconds` (`0` = never spoils, at most one week) and `spoilsInto` (an item id, or empty to remove the stack).
   - Raw foods spoil into `rotten_food`: berries 40 min, raw meat 20 min, slime jelly 30 min of online time.
   - `rotten_food` is an edible, high-poison food in `items.json` and `food.json`, with a vector icon.
   - Validation rejects `spoilsInto` without a timer, a food that spoils into itself, and a spoil target that itself spoils.
5. **D5 — Authority and cadence.** `FoodSpoilage.Advance` is a pure step. The server runs it once per second over every online player's inventory.
   - **Stamped:** an unstamped stack of perishable food gets its full spoil time.
   - **Aging:** the countdown advances on the server only, with no packet.
   - **Spoiled:** the stack becomes its `spoilsInto` item with the same quantity.
   - Only stamps and spoils are sent to the client and mark the character dirty. Aging is saved when the character is next written: on logout, or with the next inventory change.
   - Food spoils only while its owner is online. Ground loot doesn't age; it expires instead.
6. **D6 — Client.** `MultiplayerSession` counts perishable stacks down locally between server updates and never reaches `0`. The server's spoil packet replaces the stack.
   - `SyncedFoodCatalog` reads the synced `food.json` (ADR-014).
   - The hotbar tooltip shows nutrition and the remaining time, for example `+25 food  Spoils in 12m`.

## Alternatives Considered

- **Reuse `Durability`.** Rejected; see Context.
- **Store an absolute spoil timestamp.** That would need a shared clock and offline aging. Players would log back in to rotten inventories, and the server's tick time isn't persisted. A countdown of online seconds is simpler and matches how needs pause while offline (ADR-015).
- **Send freshness every second.** Rejected. It would mean one packet per perishable stack per second. A local countdown plus authoritative spoil events is enough for a tooltip.
- **Average freshness weighted by quantity when stacks merge (RimWorld).** Rejected: taking the minimum is simpler, can't be exploited by topping up a stack with fresh food, and keeps `MergeWith` free of quantity math.

## Consequences

- Raw food now creates time pressure that cooking will relieve once the cooking recipes land (a follow-up PR).
- Inventory blobs grow by 4 bytes per occupied slot.
- Every reader of `ItemSlot` sees an explicit freshness field instead of an overloaded one.
