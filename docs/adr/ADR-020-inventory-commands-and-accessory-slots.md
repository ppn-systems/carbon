# ADR-020: Inventory Split, Sort and Destroy Commands, Item-Bound Slot Commands, and Accessory Slots

- **Date**: 2026-11-27
- **Status**: Proposed
- **Scope**: `contracts/Carbon.Contracts.Items`, `engine/Carbon.Items`, `server/Carbon.Application/World`, `server/Carbon.Handlers/Items`, `desktop/`, `resources/config/items.json`
- **Audit**: INV-1…INV-3, UX-1…UX-9 in `docs/audit/2026-11-inventory-audit.md`

## Context

The inventory has 40 slots: hotbar `0..8`, backpack `9..35`, armor `36..39` (`PlayerInventoryLayout.cs`). The client
can move, drop, upgrade, repair and sell, and every mutation is tick-queued (ADR-017). The audit found:

1. **Slot commands name a slot, not an item** (INV-1). `MoveInventorySlot` and `DropInventoryItem` carry slot indices
   only. When the slot changes before the tick runs the command (a pickup merges in, an earlier command in the same tick
   reorders the bag, a packet is replayed), the server acts on whatever is there now. The worst case is dropping a rare
   item the player never dragged.
2. **No split, sort or destroy** (UX-1, UX-3, UX-4). These need new requests; the client cannot fake them with moves
   (a split is not a sequence of whole-slot moves).
3. **Accessories have no slot** (UX-7). `blood_amulet` and `slime_ring` exist in `items.json` with category
   `accessory`, and `EquipmentSlotKind` already has `Accessory1`/`Accessory2`, but no inventory slot accepts them.

## Decision

1. **D1 — Three new C2S packets** in the Items band, each answered with `MutationResult` and run through the tick queue
   (ADR-017):

   | Opcode | Packet | Fields | Rule |
   | :--- | :--- | :--- | :--- |
   | `0x400A` | `SplitInventorySlot` | `FromSlot`, `ToSlot`, `Quantity` (`ushort`), `ExpectedItemId` | Both slots in the bag (`0..35`). `1 <= Quantity < stack`. Target empty, or the same stackable item with room for all of `Quantity`. |
   | `0x400B` | `SortInventory` | — | Merges partial stacks, then orders the backpack (`9..35`) by category, rarity (best first), item id, enhancement, durability, quantity, freshness (stalest first). The hotbar and equipment are untouched. |
   | `0x400C` | `DestroyInventoryItem` | `SlotIndex`, `Quantity` (`int`, `<= 0` = all), `ExpectedItemId` | Removes the units permanently. No loot is spawned. |

   All three conserve or remove quantity; none can create items. Invalid requests are `Invalid`, an empty source is
   `NotFound`.
2. **D2 — Slot commands are bound to the item the client saw.** `MoveInventorySlot` and `DropInventoryItem` get an
   appended field `[SerializeOrder(2)] ushort ExpectedItemId`; the new packets carry it too. When it is non-zero and the
   source slot holds a different item, the server answers `Conflict` and changes nothing. `0` skips the check, so
   tests and tools that predate the field still work. This is append-only (`carbon-packets`): existing field orders are
   unchanged.
3. **D3 — Sort runs on the server**, in `InventorySorting.SortBackpack` (`engine/Carbon.Items/Catalog`). It is
   deterministic (total order on every slot field), stack-only (`stackalloc`, no heap allocation) and reports the changed
   slots like any other mutation.
4. **D4 — Two accessory slots.** `PlayerInventoryLayout.TotalCapacity` goes from 40 to 42: `Accessory1Slot = 40`,
   `Accessory2Slot = 41`. An item whose `equipSlot` is `Accessory1` or `Accessory2` fits either slot. `blood_amulet` and
   `slime_ring` get `"equipSlot": "Accessory1"`. Quick-equip fills the first empty accessory slot.
   Stats: `CharacterProgressionPolicy.ComputeStats` still reads armor slots `36..39` and the held item only, so
   accessories carry no bonus yet; adding one is the character-progression owner's call.
5. **D5 — Persistence format unchanged.** `InventoryBinaryCodec` stays at v3: entries carry their own `ushort` slot index,
   so slots `40..41` need no format change, and older blobs load into the larger container as before. The decoder now
   loads non-finite or negative durability as `0` and keeps the first entry for a repeated slot index (INV-3).
6. **D6 — Non-stackable entries take one slot per unit** when added to a bag (`PlayerInventoryLayout.TryAddBagItem`,
   `InventoryContainer.TryAdd`), matching `InventoryDraft.Add` (INV-2).
7. **D7 — Client confirmation is a client concern.** Drop and destroy confirmation, the split dialog, rarity borders and
   tooltips live in the desktop client. The server trusts nothing from them beyond the request fields above.
8. **D8 — Enhancement tiers are a client data table.** `EnhancementTierStyles` maps `UpgradeLevel` to a tier (fill,
   badge accent, glow, shimmer). Rarity is the slot border and enhancement is the fill and badge, so the two never
   compete. Screenshots seed enhanced gear with the Debug-only `CARBON_DEBUG_STARTER_ITEMS` host variable (Release
   hosts ignore it, like the other `CARBON_DEBUG_*` overrides).

## Alternatives Considered

- **Bind commands with a per-slot revision counter** instead of the item id. Stronger (it also catches "same item,
  different stack"), but needs a revision per slot on the server, in every `InventorySlotChanged` and in the client
  model. The item id catches the harmful case (acting on a *different* item) with one appended field.
- **Server-side replay protection by `SequenceId`.** Replay is not a duplication risk: every inventory command conserves
  or removes quantity. Item binding covers the harm a replay can do (acting on a slot that changed).
- **Client-side sort as a series of moves.** Up to 27 round trips, a visible shuffle, and interleaving with pickups.
- **Dedicated weapon and off-hand slots.** Combat, harvesting, stats and the replicated look read the weapon from the
  selected hotbar slot; moving it changes combat feel and code owned by other work. No item uses `OffHand`. Deferred.
- **Weight limit.** Items have no weight and the design does not call for one; the 36-slot bag stays the limit.

## Consequences

- **Wire:** three new opcodes and one appended field on two packets. The appended field changes the packet length, so
  client and server are updated together, as they are today.
- **Rollback:** a server rolled back to 40 slots drops entries at slots `40..41` on load (the decoder skips indices past
  capacity), so equipped accessories would be lost. Unequip them before rolling back.
- **Accessories** are equippable but give no stats until `CharacterProgressionPolicy` reads slots `40..41`.
- `AuthoritativeWorld` routes move, drop, split, sort and destroy through one `ExecuteSlotCommand` path, which keeps it
  under the SOLID-1 line cap (`ClassSizeTests`).
