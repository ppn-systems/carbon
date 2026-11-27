# ADR-038: Retire Dead Wire Opcodes and Fields

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-001](ADR-001-map-delta-sync.md) (`MapWorldInfo` and `MapTileChanged` are retired),
  [ADR-025](ADR-025-villages-from-real-structures-and-trader-npcs.md) (the single-line trader opcodes are retired, D18
  is the only trade path)
- **Scope**: `contracts/`, `server/`, `desktop/`, `engine/` (doc comments), `tests/`, `docs/`

## Context

The owner asked for the protocol to lose its redundant fields and packets. The audit
[docs/audit/2026-11-wire-protocol.md](../audit/2026-11-wire-protocol.md) (`tools/audit/WireAudit`) found packets that
later ADRs superseded but never deleted. Each one still has a live server handler:

- `MapWorldInfoRequest` / `MapWorldInfo` (`0x1101` / `0x1110`, ADR-001 D1). No client sends the request and no client
  reads the reply. `CharacterEnterWorld` carries the seed, tile size and frequency.
- `MapTileChanged` (`0x1130`, ADR-001 D5). Neither side references it. Tile edits travel in `MapChunkDelta`.
- `TraderBuyRequest` / `TraderSellRequest` (`0x4041` / `0x4042`, ADR-025). The client has sent the D18 batches since
  the basket amendment. The single-line handlers still accepted a trade with no basket total check.
- `VendorBuyItem` / `VendorSellItem` (`0x4007` / `0x4008`). Only the uncalled `IGameplayController.TryVendorBuy` and
  `TryVendorSell` send them (ADR-025 Context). Their handlers still ran the generic vendor: any friendly NPC within
  175 px sold every `vendorBuyable` item at a fixed price with unlimited stock, outside the traders' stock and restock.

The audit also found about fifteen fields that the receiver never reads (W-5 … W-12). Nothing so far stopped a packet
from losing its sender or its handler: `ProtocolContractGuardTests` checks uniqueness and bands only.

## Decision

1. **D1 — What counts as dead.**
   - **Dead opcode.** An opcode is dead when production code on its sending side never sends it, or production code on
     its receiving side never handles it. For a C2S packet, the sender is the client and the handler is the server's
     `[PacketOpcode]` method. For every other packet, the sender is the server and the handler is the client's
     `On<T>` / `RequestAsync<T>` / `StreamAsync<T>`.
   - **Dead field.** A field is dead when the receiving side never reads it, or the sending side never writes it, *and*
     no Accepted ADR describes a receiver behaviour that uses it. A field an ADR promises a consumer for (class B in
     the audit, e.g. `CombatHit.StatusEffectLevel`, ADR-036 D4) is an unfinished feature, not a dead field. The owner
     decides whether to build the consumer or retire the field in a new ADR.
   - **Not dead.** A field a library interface requires is live (`IPacketStreamable.IsEndOfStream`, which the Nalix
     SDK reads), even when no Carbon code reads it.
2. **D2 — Guard.** `tests/architecture/Carbon.Architecture.Tests/OpcodeWiringTests.cs` asserts the positive rule:
   every packet has a sender on its sending side and a handler on its receiving side. A side sends `T` when its
   sources rent `PacketFactory<T>`, call `T.Create()` or `new T`, or pass `T` to one of their own generic helpers that
   rents its type parameter. The guard has no allowlist. If a packet ever needs an exception, it is marked in
   `contracts/` with an attribute that carries the reason, and the guard reads that attribute. None needs one today.
   The guard is syntactic, so it does not see whether the sending method is ever called. `tools/audit/wire-audit.sh`
   reports that during an audit (the vendor opcodes were caught that way).
3. **D3 — Retiring an opcode.**
   - The packet type and its constant are deleted, and the constant becomes a comment
     (`// 0xNNNN was X, retired by ADR-038: never reuse it.`).
   - The value is added to `RetiredOpCodeTests`, which fails if any opcode class declares it or any packet binds it.
   - The server path that only the retired handler reaches goes in the same PR: the port method, the command kind and
     the domain operation. The client sender goes too. Both sides are removed together.
   - Tests that drove the retired path move to the live path (e.g. `TraderWorldTests` trades through one-line
     batches).
4. **D4 — Removing a field.** The property is deleted. The other `[SerializeOrder]` numbers keep their values, so no
   field is renumbered and the gap stays. A round-trip contract test covers every packet whose layout changes.
5. **D5 — Version bump policy.** `CarbonProtocol.Version` goes up by one per removal PR, not per packet. Its doc
   comment records what each number removed.
   - A client on an older version is refused at login with `AuthStatus.ProtocolMismatch`
     (`server/Carbon.Application/Identity/AuthService.cs:118`), so it never sends a retired opcode or parses a shorter
     layout.
   - If two removal PRs are open at once, the one merged second takes the next number when it merges `origin/master`.
   - Tests assert a minimum version (`>=`), never an exact one, so a later bump does not mean editing earlier tests.
6. **D6 — Removals.** Each batch is one PR.

   | Batch | Version | Removed |
   | :--- | :--- | :--- |
   | 1 | 4 | Every dead opcode: `0x1101` `MapWorldInfoRequest`, `0x1110` `MapWorldInfo`, `0x1130` `MapTileChanged`, `0x4007` `VendorBuyItem`, `0x4008` `VendorSellItem`, `0x4041` `TraderBuyRequest`, `0x4042` `TraderSellRequest`. Server paths only they reached: `MapHandlers.HandleWorldInfoAsync` and the `IWorldMapService` world parameters; the vendor handlers, `IWorldInventoryService.SubmitVendorBuyAsync` / `SubmitVendorSellAsync`, `AuthoritativeWorld.TryVendorBuy` / `TryVendorSell`, `EconomyService.RunVendorOperation`, `WorldCommandKind.VendorBuy` / `VendorSell`; `IWorldTradeService.SubmitTraderBuyAsync` / `SubmitTraderSellAsync`, `TradeService.BuyLocked` / `SellLocked`, `WorldCommandKind.TraderBuy` / `TraderSell`. Client: `IGameplayController.TryVendorBuy` / `TryVendorSell` |

   Later batches add their rows here: the dead fields W-6 … W-12, and `EntityDeltaBatch.DirtyMasks`.
   The auth fields (W-13) wait for the login redesign (ADR-037).

## Alternatives Considered

- **Keep dead opcodes for compatibility.** Rejected. The version check already refuses every older client, so a
  retired opcode serves nobody. Its live handler is an attack surface (the single-line trades skipped the basket
  total check).
- **An allowlist of opcodes exempt from the guard.** Rejected. An allowlist grows by one line per new exception and
  hides why each one exists. An attribute on the packet keeps the reason next to the code.
- **A semantic (reachability) guard in CI.** Rejected for CI. It needs a full Roslyn compilation of every production
  source, about 30 s. The audit tool keeps that check for audits.
- **Renumber the remaining `[SerializeOrder]` fields.** Rejected. Renumbering hides the removal in a diff, and a gap
  costs nothing on the wire.

## Consequences

- Seven opcodes and their handlers are gone, and so is the generic vendor. Village traders (ADR-025) are the only
  place to buy or sell. An old client is refused at login, as for any other bump.
- A new packet now needs both ends before CI is green, so a half-finished feature cannot ship a dead opcode.
- Single-line trades are no longer possible. Every trade goes through the D18 batches with their `ExpectedGold` check.
- The class B fields stay on the wire until the owner decides. They cost bytes until then.
