# Inventory Audit — November 2026

| Field | Value |
| :--- | :--- |
| **Audited commit** | `14546c5` (master, PR #67) |
| **Scope** | `engine/Carbon.Items` (containers, layout, codec), `server/Carbon.Application/World/Inventory`, `AuthoritativeWorld.InventoryAndLoot.cs`, `server/Carbon.Handlers/Items/ItemHandlers.cs`, `contracts/Carbon.Contracts.Items`, the desktop inventory modal, hotbar and input code |
| **Method** | Manual read-through against AGENTS.md, ADR-006, ADR-014, ADR-016 and ADR-017, then a check of each finding with a unit test where one could be written |
| **Status** | Fixes in [ADR-020](../adr/ADR-020-inventory-commands-and-accessory-slots.md). The status column says what landed. |

Severity: **High** = items can be lost, duplicated or used by mistake; **Medium** = a rule or UX gap that players will hit; **Low** = cleanup.

## Summary

The core is sound. Every mutation runs on the tick under the world lock (ADR-017). Moves, merges and swaps conserve
quantity. Crafting and vendor trades are all-or-nothing (`InventoryDraft`). The character save batch is one
transaction (`SqliteCharacterStore.SaveBatchAsync`, `PgCharacterStore.SaveBatchAsync`), so a drop followed by another
player's pickup cannot be saved half-way. **No duplication path was found.**

The weak spots are at the edges: commands name a *slot*, not an *item*, so a stale or replayed command acts on
whatever now sits in that slot; a non-stackable stack with quantity > 1 can be added as one slot; the codec trusts
the floats it loads. The UX is missing the basics of an MMO bag: no split, no sort, no destroy, no confirmation
before dropping rare gear, a two-line tooltip, no rarity colour, and nowhere to put accessories.

## Correctness

| ID | Severity | Finding | Where | Status |
| :--- | :--- | :--- | :--- | :--- |
| INV-1 | High | **Commands are bound to a slot, not an item.** `MoveInventorySlot` and `DropInventoryItem` carry slot indices only. If the slot changed between the click and the tick (a pickup merged in, an earlier move in the same tick reordered the bag, or the packet was replayed), the server acts on a different item. Example: drag a sword out of the window to drop it while a queued move puts a rare crown in that slot, and the crown lands on the ground. Not a duplication, but a loss the player did not ask for. | `contracts/Carbon.Contracts.Items/C2S/MoveInventorySlot.cs:16`, `DropInventoryItem.cs:16`, `InventoryService.cs:303` | **Fixed.** Commands carry `ExpectedItemId`; a mismatch is `Conflict` and changes nothing (ADR-020 D2). |
| INV-2 | Medium | **Non-stackable entries with quantity > 1 are added as one slot.** The non-stackable branch of `TryAddBagItem` and `InventoryContainer.TryAdd` writes the whole entry into the first empty slot, so a loot entry of two swords becomes one slot holding "2 swords" that can never be split or equipped as a pair. `InventoryDraft.Add` already does it right (one unit per slot). | `PlayerInventoryLayout.cs:220`, `InventoryContainer.cs:135` | **Fixed.** Both place one unit per empty slot, like `InventoryDraft.Add`. |
| INV-3 | Medium | **The codec trusts loaded floats.** A NaN, infinite or negative durability from a corrupted blob or a bad migration is loaded as is, and `ToolUsagePolicy` then treats the item as never worn out (`NaN <= 0` is false). Freshness is already sanitised; durability is not. Two entries with the same slot index silently keep the last one. | `InventoryBinaryCodec.cs:103`, `:124` | **Fixed.** Non-finite or negative durability loads as `0`, and a repeated slot index keeps the first entry. Format unchanged (still v3). |
| INV-4 | Low | **Merging ignores durability.** `ItemSlot.MergeWith` keeps the target's durability. No shipped item has both `maxDurability > 0` and `maxStack > 1` (checked against `resources/config/items.json`), so this is latent. A config that adds one would let a worn copy merge into a fresh stack and heal. | `ItemSlot.cs:63` | Open, documented. A data guard is the cheaper fix if such an item is ever added. |
| INV-5 | Low | **Every inventory mutation re-serializes the whole bag.** `CharacterPersistence.MarkDirty` snapshots (and so serializes, one `byte[]`) on each move, not once per save batch. Correct, but wasteful under drag-spam. | `CharacterPersistence.cs:102` | Open. Rate-limited by the 16-command queue cap (ADR-017 D4). |
| INV-6 | Low | **Rejected moves are silent.** The client sends moves fire-and-forget and ignores the `MutationResult`, so a refused move gives no feedback. Nothing to roll back (the client does not predict), but the player sees nothing happen. | `MultiplayerSession.Actions.cs:270` | Open (UX polish). |

Checked and found correct:

- **Races.** All mutations run inside the tick drain (`WorldCommandQueue.Drain`), under `_gate`. Pickup is queued as `PendingPickupLootId` and applied on the tick. Two players picking up the same stack in one tick are serialised and `GroundLootService.ApplyPickup` keeps the remainder.
- **Merge edge cases.** A merge never pushes a stack past `maxStack` (`InventoryContainer.TryMoveOrSwap`, `:247`). An oversized stack (config lowered `maxStack`) swaps instead of merging. Upgraded and instanced items never stack (`ItemSlot.IsStackable`). Freshness merges keep the stalest countdown (ADR-016).
- **Equipment validation.** `PlayerInventoryLayout.TryMoveOrEquipSlot` checks both directions of a swap, so a bag item cannot be swapped into an armor slot it does not fit.
- **Persistence.** In-flight saves stay visible until they commit (TS-3, #36); the batch is atomic.

## UX

| ID | Severity | Finding | Where | Status |
| :--- | :--- | :--- | :--- | :--- |
| UX-1 | High | **No stack split.** There is no way to take 10 of 64 arrows. | — | **Fixed.** `SplitInventoryStack` (server) and a split dialog. |
| UX-2 | High | **Drag outside the window drops the whole stack with no confirmation**, including +7 epic gear. `X` drops one at a time with no confirmation either. | `WorldGameplayScene.cs:732`, `:755` | **Fixed.** Dropping rare-or-better, enhanced or equipped items asks first. |
| UX-3 | Medium | **No destroy.** Junk can only be dropped on the ground, where it lingers as loot. | — | **Fixed.** `DestroyInventoryItem` with a confirmation dialog. |
| UX-4 | Medium | **No sort.** 27 backpack slots fill up in pickup order. | — | **Fixed.** Server-side `SortInventory`: merges partial stacks, then orders the backpack by category, rarity and item. The hotbar is never touched. |
| UX-5 | Medium | **Tooltips are two lines.** Name and one stat line; no rarity, durability, value, freshness or hint. | `InventoryModalWidget.cs:315`, `:371` | **Fixed.** Name in rarity colour with `+N`, category and rarity, stats, durability, value and the item hint. |
| UX-6 | Medium | **Rarity is invisible.** Every slot has the same border. | `InventoryModalWidget.cs:96` | **Fixed.** Slot borders take the rarity colour. |
| UX-7 | Medium | **Accessories have nowhere to go.** `blood_amulet` and `slime_ring` exist (category `accessory`) but no slot accepts them. | `resources/config/items.json`, `PlayerInventoryLayout.cs:41` | **Fixed.** Two accessory slots (`40`, `41`) on the paper doll. They hold the items; stat bonuses are for the character-progression owner (`CharacterProgressionPolicy` is not changed here). |
| UX-8 | Low | **Hotbar binding needs a drag.** | — | **Fixed.** Hover an item and press `1`–`9` to put it on that hotbar slot (swap if taken). |
| UX-9 | Low | **The held weapon is not on the paper doll.** The weapon is whatever the selected hotbar slot holds; the doll does not say so. | `InventoryModalWidget.cs:118` | **Fixed.** A read-only "Held" slot mirrors the selected hotbar item. |

### Not done, on purpose

- **Dedicated weapon and off-hand slots.** Combat, harvesting, stats and the replicated look all read the weapon from the selected hotbar slot (`CharacterProgressionPolicy.ComputeStats`, `EquipmentLook`, `ToolUsagePolicy`). Moving it to a fixed slot changes combat feel and touches code owned by the parallel progression and enhancement work. No item uses `OffHand`. The "Held" mirror (UX-9) gives the clarity without the rewrite.
- **Weight limit.** Items have no weight, and nothing in the design calls for one. The 36-slot bag is the limit.
