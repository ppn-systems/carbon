# ADR-012: Server-Authoritative Wallet Persistence, Sync & Vendor Sinks

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `contracts/`, `server/`, `desktop/`, `engine/Carbon.Economy`, `tests/`

## Context

PR #18 added `engine/Carbon.Economy`. It has a capped, overflow-safe `Wallet` with gold and diamonds (`engine/Carbon.Economy/Currency/Wallet.cs`) and loot rolls that credit currency to the killer (`server/Carbon.Application/World/AuthoritativeWorld.Economy.cs`). Three gaps remained:

1. Balances lived only in memory. `CharacterRecord` (`server/Carbon.Abstractions/Persistence/CharacterRecord.cs`) had no currency fields, so gold was lost on logout or restart. The diamond bad-luck protection counter (`LootPityState`) was lost the same way, which let players reset their pity.
2. Clients had no way to learn their balance: there was no packet.
3. The gold sinks (`VendorService.TryBuy/TrySell/TryRepair`) existed in the engine but had no wire requests, so they couldn't drain gold. That leaves a faucet with no sink (see `docs/economy.md` §1).

## Decision

1. **D1 — Persistence (`server/`).** Append the trailing optional members `long Gold`, `long Diamonds`, `ulong WalletSequence` and `int DiamondPity` to `CharacterRecord`, so existing call sites keep compiling. Both stores add the columns `gold`, `diamonds`, `wallet_sequence` and `diamond_pity` to `characters`, through migration `0005_character_wallet`:
   - SQLite uses `INTEGER`.
   - PostgreSQL uses `BIGINT`/`INTEGER`.
   - Each migrator gets a reusable `AddCharacterColumnsAsync(migrationId, (column, definition)[])` step. It is idempotent, additive and recorded in `schema_migrations`. Later column migrations, such as survival needs, call the same helper with the next id.
2. **D2 — Authority.** On world entry, `AuthoritativeWorld` restores the wallet from the record, clamped to the configured caps, and restores the pity counter. `BuildUpdatedRecordLocked` writes both back, and every wallet mutation marks the character dirty. Clients never send balances.
3. **D3 — Sync packet (`contracts/`).** Add `WalletSync` (S2C, `EntityOpCode.WalletSync = 0x1237`, in `Carbon.Contracts.Entities`) with `long Gold`, `long Diamonds` and `ulong Sequence`.
   - Sent on world entry, after each kill that changed the wallet, and after each vendor or repair operation.
   - Clients apply only a `Sequence` newer than the last one applied, which drops stale out-of-order pushes.
   - Sent through `IWorldOutboundSink.SendWalletSync`, double-buffered like the other `_pending*` queues.
4. **D4 — Vendor and repair requests (`contracts/`, `server/`).** Add three C2S requests in `Carbon.Contracts.Items`. Each is answered with `MutationResult`, and its status comes from `EconomyResult` (`EconomyError` → `OpStatus`).

   | Request | Opcode | Payload | Rules |
   | :--- | :--- | :--- | :--- |
   | `VendorBuyItem` | `0x4007` | `ItemId`, `Quantity` | Bag slots only, `1..999` units |
   | `VendorSellItem` | `0x4008` | `SlotIndex`, `Quantity` | Bag slots `0..35` only; equipped armor can't be sold |
   | `RepairInventoryItem` | `0x4009` | `SlotIndex` | Any slot |

   Buying and selling require a friendly town NPC within 175 px. Repair requires a forge station: a forge-artisan NPC or a placed anvil, the same rule as forging (ADR-008).
5. **D5 — HUD (`desktop/`).** `MultiplayerSession` stores the latest balance, and the HUD shows gold and diamonds next to the vitals, using `resources/art/ui/coin.cvg.json` and `diamond.cvg.json`.

## Alternatives Considered

- **Storing currency inside the inventory or progression blob.** This would avoid a schema change. It was rejected because it hides the balance from SQL audits and couples the currency to a binary codec version. Wallets are also deliberately not items (`docs/economy.md` §2).
- **Folding gold into `PlayerStatsSync`.** This was rejected. That packet carries a large payload (progression blob and cooldown arrays) and is sent for unrelated reasons. A small dedicated packet with a sequence number is cheaper, and it can be ordered on its own.
- **A separate `character_wallets` table.** This was rejected for now. It would cost an extra join or round trip per load and save, for four scalars that share the character's lifecycle. A ledger table can come later for audits, fed by `ICurrencyLedger`.

## Consequences

- Gold and diamonds survive relogs and restarts, and pity can no longer be reset by reconnecting.
- Vendor spread, repair and buying from vendors now actually remove gold, which closes the faucet/sink loop.
- Adding a new packet and opcodes is additive and keeps the wire format backward compatible. Old clients ignore `0x1237`.
- Not done yet: the server doesn't persist per-mutation ledger entries. `CurrencyLedgerBuffer` exists, but nothing drains it to the database yet.
