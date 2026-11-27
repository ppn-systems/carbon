# ADR-043: Pool Outbound Packets Through IObjectPoolManager

- **Date**: 2026-11-27
- **Status**: Proposed
- **Amends**: AGENTS.md rule 4 and the `carbon-packets`, `carbon-handlers`, `do-not-do` and `performance-aot` skills, which
  name `PacketFactory<T>.Acquire()` as the zero-allocation way to send a packet
- **Scope**: `server/Carbon.Host` (`WorldOutboundSink`), `server/Carbon.Handlers`, `tests/server`, `AGENTS.md`, `.claude/skills/`

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

1. **D1 — One pool for outbound packets.** Add `PacketPool` to `server/Carbon.Host`. It wraps one
   `IObjectPoolManager` (`ObjectPoolManager`), created at startup and registered in `InstanceManager` before any packet
   type is used. It hands out a lease struct over `Get<T>()` that calls `Return<T>()` on dispose. Each type's capacity
   is set once with `SetMaxCapacity<T>` (64 by default; a delta batch needs one per observer in flight).
2. **D2 — Return after the send completes.** The lease stays scoped around the awaited send, as the dispatchers already
   are (`using … Acquire(); await connection.SendAsync(…)`). The packet goes back only when the send finished or
   failed, never while the transport may still read it. Pooled delta columns (PERF-3) are cleared before the return, as
   now.
3. **D3 — Reset on return.** Every dispatcher already sets every field it sends. The pool manager also calls
   `IPoolable.ResetForPool` when a packet is returned (its documented contract), so a field a future dispatcher forgets
   cannot leak the previous recipient's data.
4. **D4 — Guard.** `RealOutboundSinkTests` asserts the existing 32 B/tick budget through the real sink, without raising
   it. Today it asserts only that every send arrives and none fails.
5. **D5 — Docs.** AGENTS.md rule 4 and the skills name `PacketPool` for server sends. They keep `PacketFactory<T>` only
   where its allocation is acceptable (cold paths), or until it is shown to pool.

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
  completes or fails, bounded by the per-type capacity (beyond it `Get` allocates, as today).
- **Risk:** a packet returned while still in use would corrupt another recipient's frame. D2 keeps the existing scoping;
  the guard and the dispatch tests cover it.
