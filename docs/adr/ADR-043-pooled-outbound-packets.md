# ADR-043: Pool Outbound Packets Through IObjectPoolManager

- **Date**: 2026-11-27
- **Status**: Accepted (by the orchestrator, with the conditions in D2, D3 and D6)
- **Amends**: AGENTS.md rule 4 and the `carbon-packets`, `carbon-handlers`, `do-not-do` and `performance-aot` skills, which
  name `PacketFactory<T>.Acquire()` as the zero-allocation way to send a packet
- **Scope**: `server/Carbon.Host` (`PacketPool`, `WorldOutboundSink`, `Program`), `tests/server`, `AGENTS.md`, `.claude/skills/`

## Context

Every server send leases its packet with `PacketFactory<T>.Acquire()` and returns it on dispose: 22 calls in
`WorldOutboundSink.cs`, plus the handlers. AGENTS.md rule 4 relies on that lease being allocation-free.

Audit round 3, ALLOC-1, measured the steady world tick through the real `WorldOutboundSink`. The setup was 8 players,
each on a hand-written connection whose sends complete synchronously and are only counted (`RealOutboundSinkTests`).
Result: **974 B/tick** at 10 frames/tick. With the null sink the same tick costs 1.2 B, and the guard's budget is 32 B.

The cost is the packet objects, not async-`Task` boxing, since every send completed synchronously. An in-process
`EventListener` on the runtime's GC allocation-tick events sampled only `EntityDeltaBatch` (96 B per send) and
`EntityDespawn` (40 B per send). Each send costs a fixed amount, whatever the batch size.

`PacketFactory<T>` allocates on its own, with no send at all:

| per call, `EntityDespawn` | bytes |
|---|---|
| `PacketFactory<T>.Acquire()` + dispose | 40 |
| the same, with an `ObjectPoolManager` registered in `InstanceManager` | 40 |
| the same, in a fresh process with the manager registered before any packet type is touched | 40 |
| the same, after `SetMaxCapacity<T>(64)` and `Prealloc<T>(16)` | 40 |
| `IObjectPoolManager.Get<T>()` + `Return<T>()` | **0** |

Two consecutive leases never return the same instance. `PacketFactory<T>`'s public surface is only `Acquire` and
`AcquireBatch`, with no configuration point. The packets implement `IPoolable` and `IPoolRentable`. Why the factory does
not pool in our setup is not visible through the public API. The server never registers a pool manager itself.

## Decision

1. **D1 — One pool for outbound packets.** `server/Carbon.Host/PacketPool.cs` wraps an `IObjectPoolManager`. The host
   creates one `PacketPool` (over its own `ObjectPoolManager`) for its lifetime and passes it to `WorldOutboundSink`.
   No global registration is needed: the sink is the only user. `Rent<T>()` returns a `PacketLease<T>` struct over
   `Get<T>()`, and disposing the lease returns the packet. All 22 sink dispatchers use it in place of
   `PacketFactory<T>.Acquire()`. Every lease is per connection: no packet instance is shared between recipients, so a
   packet always goes back after its one send.
2. **D2 — Return after the send completes, exactly once.** The lease is a `using` declaration in the dispatcher, around
   the awaited send and inside its `try/catch`. The packet goes back only when the send has completed, failed or been
   cancelled, never while the transport may still read it, and exactly once. Pooled delta columns (PERF-3) are cleared
   before the return, as before. `PacketPoolTests` hold sends pending on a controlled connection: the packet stays
   rented until the send ends, a second send gets a different instance, and failed and cancelled sends return the packet
   once.
3. **D3 — Reset on return.** `PacketPool` calls `IPoolable.ResetForPool` itself before returning a packet. It does
   not rely on the manager, so a pool manager that does not reset cannot leak data. `PacketResetCoverageTests` checks,
   by reflection over every server-to-client packet type in the contracts assemblies, that no `[SerializeOrder]` field
   keeps a previous value after `ResetForPool`. It found no leftover in the 31 types. A field with an initializer
   (`Level = 1`, `XpToNextLevel = 100`, `EatProgress = -1`) resets to its type default instead. Two more tests cover
   this. One requires a reset packet of every pooled type to equal a fresh one, except an explicit list of known
   deviations, so a new field with an initializer fails until someone looks at it. The other requires the sink to set
   every such field on every send, so a pooled packet never sends 0 where a fresh one would send the initializer.
4. **D4 — Guard.** `RealOutboundSinkTests.StepFixedTick_RealSinkWithSynchronousConnections_StaysWithinTheTickAllocationBudget`
   measures the steady tick through the real sink with the shared `AllocationProbe` and asserts the existing 32 B/tick
   budget. It measured 974 B/tick before and 1.2 B/tick after, the same as with the null sink.
5. **D5 — Docs.** AGENTS.md rule 4 and the `carbon-packets`, `carbon-handlers`, `do-not-do` and `performance-aot` skills
   name `PacketPool.Rent<T>()` for outbound packets. Handler replies keep `PacketFactory<T>.Acquire()`: one packet per
   reply, which is accepted because replies are not per tick.
6. **D6 — Open item.** Ask Nalix's maintainers whether a setup step makes `PacketFactory<T>.Acquire()` pool. If one
   exists, the handlers can pool too, and `PacketPool` may become unnecessary. This does not block this decision.

## Alternatives Considered

- **Keep `PacketFactory<T>` and accept the cost.** Rejected: about 1 KB per tick for 8 players grows with players and
  observers, and contradicts rule 4.
- **Find the missing `PacketFactory<T>` setup.** Preferred if it exists, but it is not visible through the public API.
  Worth asking Nalix's maintainers. If a setup step makes `Acquire` pool, D1 shrinks to that step, and D2 to D5 stand.
- **A hand-rolled per-type pool in Carbon.** Rejected: `IObjectPoolManager` already pools without allocating and
  is the library's own mechanism.
- **A per-connection send channel (PERF-3).** Orthogonal: it bounds what is in flight but does not remove the packet
  object.

## Consequences

- **Positive:** the outbound path can be held to the same budget as the tick, through the real sink.
- **Trade-off:** pooled packets live as long as the slowest send. A stalled connection holds its packets until the send
  completes or fails. Meanwhile `Get` creates new instances, so a stall costs allocations, never correctness.
- **Risk:** a packet returned while still in use would corrupt another recipient's frame. D2 keeps the existing scoping;
  the guard and the dispatch tests cover it.
