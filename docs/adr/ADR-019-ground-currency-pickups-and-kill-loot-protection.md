# ADR-019: Ground Currency Pickups and Kill-Loot Owner Protection

- **Date**: 2026-11-27
- **Status**: Proposed
- **Scope**: `contracts/Carbon.Contracts.Entities`, `engine/Carbon.Economy`, `server/`, `desktop/`, `resources/config/economy.json`, `tests/`, `docs/`

## Context

The owner asked for coins to drop on the ground, and for loot nobody picks up to become free for others after a few minutes.

Before this change:
- `EconomyService.TryRollLoot` (`server/Carbon.Application/World/Commerce/EconomyService.cs`) spawned a kill's item stacks as ground loot and credited gold and diamonds straight to the killer's `Wallet`, then queued a `WalletSync` (ADR-012 D3).
- `GroundLootService` (`server/Carbon.Application/World/Loot/GroundLootService.cs`) kept ground stacks for a fixed `LootLifetimeTicks` (120 s at 30 Hz). Stacks had no owner, so anyone could take a kill's drops at once.
- `EntitySpawn` (`contracts/Carbon.Contracts.Entities/S2C/EntitySpawn.cs`, opcode `0x1230`) replicated a `LootDrop` as an item id plus `LootCount`. It could not express a currency pile or an owner.
- Coin and diamond vector art already existed as HUD icons (`resources/art/ui/coin.cvg.json`, `diamond.cvg.json`, under `uiIcons` in `catalog.cvg.json`).

## Decision

1. **D1 — Kill currency drops on the ground.** `TryRollLoot` no longer credits the wallet. It spawns one gold pile and one diamond pile (each only when its amount is above zero) next to the corpse, through `GroundLootService.SpawnCurrency`. A pile is a ground-loot entry with `Currency` and `CurrencyAmount` and an empty item. It never merges with other stacks.
2. **D2 — Pickup credits the wallet.** `InventoryService.TryPickup` handles both the manual pickup (`EntityPickupLoot`, `0x1203`, 84 px) and auto-collect (36 px, after the 12-tick drop grace), so piles follow the same rules as item stacks. Picking up a pile credits the whole amount with `CurrencyReason.MonsterLoot`, queues a `WalletSync` and marks the character dirty, so the balance is saved (ADR-012 D2). A pile the wallet can't take whole, because it would pass the cap, stays on the ground.
3. **D3 — Owner protection for kill loot.** Every drop from an economy kill, items and currency alike, carries a `LootClaim`: the killer's character id, the killer's player network id at the kill, `ProtectedUntilTick` and `LifetimeTicks`. While `tick < ProtectedUntilTick`, only the owning character may pick it up. Ownership is checked by character id, so a killer who logs out and back in during the window keeps the reservation. After that, anyone may. Claimed stacks never merge, and free drops never merge into them, so a reserved stack can't be topped up or unlocked by someone else. The legacy fallback drops (no economy loaded) stay unowned.
4. **D4 — Config, server-only.** Add `economy.json → groundLoot` with `ownerProtectionSeconds` (60) and `killLootLifetimeSeconds` (180), modelled as `GroundLootRules` on `EconomyConfig`. Validation requires `1 <= killLootLifetimeSeconds <= 3600` and `0 <= ownerProtectionSeconds <= killLootLifetimeSeconds`. Seconds are converted to ticks with the world's tick rate. `economy.json` stays server-only (ADR-014 D10), so the desktop never holds these numbers. Other ground loot (player drops, harvests, construction refunds) keeps its 120 s lifetime and has no owner.
5. **D5 — Wire (append-only).** Append three members to `EntitySpawn`. Existing orders don't change.

   | Order | Member | Meaning |
   | :--- | :--- | :--- |
   | 19 | `LootCurrency LootCurrency` | `None` = item stack, `Gold`, `Diamond`; `LootCount` is then the amount |
   | 20 | `ulong LootOwnerNetworkId` | The owner's current player network id while protected (the id at the kill if the owner is offline); 0 = anyone |
   | 21 | `uint LootProtectionMs` | Protection left when the spawn was sent; 0 = anyone |

   Add the contract enum `LootCurrency : byte` (`None = 0`, `Gold = 1`, `Diamond = 2`, append-only). It matches `CurrencyKind`. No new opcode. `EntitySpawnSnapshot` and `SpawnIdentity` carry the same three values. `GroundLootService.TryGetSpawnIdentity` builds the loot identity, which moves that code out of `AuthoritativeWorld`.
6. **D6 — Desktop.** `MultiplayerSession` stores the three values per remote entity and counts the protection down locally. `RemoteEntityView` gains `LootCurrency` and `IsLootLocked` (owned by someone else and still protected). `WorldSceneRenderer` draws a currency pile with the existing `coin` / `diamond` UI vector art (ADR-005, no raster). Locked loot is drawn faded, with a grey ring and no light beam. The manual pickup key skips locked loot. All of this is cosmetic: the server alone decides ownership and amounts (D2, D3).

## Alternatives Considered

- **Gold as an inventory item.** A "gold coin" item would reuse item stacks end to end. It was rejected because ADR-012 and `docs/economy.md` §2 keep currency out of inventory slots, so it can't be dropped, duplicated by inventory bugs or stacked past a cap.
- **A new `EntityKind.CurrencyDrop`.** This would also identify piles. It was rejected because an old client would treat an unknown kind as an actor. With `LootDrop` plus `LootCurrency`, an old client still shows a generic loot marker.
- **Replicating `ProtectedUntilTick`.** Clients don't share the server's tick clock, so a remaining duration is simpler and is right at the moment it is sent. A spawn is re-sent when a stack changes or re-enters the area of interest, which refreshes the countdown.
- **Owning loot by player network id.** The first version did this, because clients already know network ids. A reconnect gives the killer a new entity and network id, so the reservation was lost early. The server now checks the character id. It replicates the owner's current network id, resolved through `PlayerRegistry.GetNetworkIdOfCharacter`, so a reconnected owner's client still draws the loot as its own. Character ids never go on the wire.
- **Putting the timings in `loot.json` per table.** One global rule was enough for the request. A per-table override can be added later without a wire change.

## Consequences

- Gold and diamonds become visible, contestable drops. Unclaimed kill loot opens up after 60 s and disappears after 180 s instead of 120 s.
- Kill income now needs a pickup, so a player who walks away forfeits it. The balance simulation (`EconomySimulator`) still credits loot at the kill. It models an engaged player who picks every drop up within the protection window, so gold and diamonds per hour are unchanged.
- The `EntitySpawn` change is additive. New fields sit at the end, and old clients that ignore them see currency piles as generic loot.
- Tests: `KillLootPickupTests` (spawn without crediting, owner pickup, refusal during and after protection, reconnect during the window, auto-collect, 180 s expiry, replicated identity for an online, reconnected or offline owner, no merge into reserved stacks, config validation) and `KillLootWorldTests` (kill → pile → pickup → `WalletSync` → SQLite persistence, with and without a reconnect inside the window).
