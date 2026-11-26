# ADR-017: Tick-Queued World Commands for Packet Handlers

- **Date**: 2026-11-26
- **Status**: Proposed
- **Scope**: `server/Carbon.Abstractions/World`, `server/Carbon.Application/World`, `server/Carbon.Handlers`, `tests/`
- **Audit**: TS-1 in `docs/audit/2026-11-codebase-audit.md`

## Context

The `carbon-handlers` skill and `do-not-do` say: *do not mutate entity state across threads outside the World Tick loop;
enqueue client commands to the World Tick queue and process them in the tick.* Movement and attacks already work that way:
`EnqueueMoveInput` / `EnqueueAttackInput` (`server/Carbon.Application/World/AuthoritativeWorld.cs:510,530`) only store
input, and `StepFixedTick` (`AuthoritativeWorld.TickAndAoi.cs:32`) applies it.

Every other gameplay request mutates the world **synchronously on the network I/O thread**, under the same `_gate` lock
that `StepFixedTick` holds for the whole tick:

| Handler | World call |
| :--- | :--- |
| `MapHandlers.cs:118,131,149,162` | `TryHarvestProp`, `TryPlaceStructure`, `TryDemolishStructure`, `TryToggleDoor` |
| `ItemHandlers.cs:72,84,99,114,128,143,155,167` | `TrySelectHotbarSlot`, `TryCraftRecipe`, `TryMoveInventorySlot`, `TryUpgradeInventoryItem`, `TryDropInventoryItem`, `TryVendorBuy`, `TryVendorSell`, `TryRepairItem` |
| `EntityHandlers.cs:84` | `TryAllocateCharacterPoint` |

Consequences:

1. **Ordering is non-deterministic.** A mutation lands between two ticks or in the middle of the next one, depending on
   which thread wins `_gate`; replays and tests cannot reproduce it.
2. **Outbound packets bypass the tick dispatch.** These methods send slot updates and chunk deltas directly, outside the
   ordered `Dispatch*` phase, so a client can see a slot update older than a tick-queued `_pendingSlotChanges` entry for
   the same slot.
3. **I/O threads block on the tick lock** for up to a full tick under load, and the tick blocks on them in turn.
4. Handlers read the pooled `IPacketContext<T>` after `await` (audit TS-4), which this change also removes.

## Decision

1. **D1 — Commands, not calls.** Each mutating request becomes a small command value (`WorldCommand` with a kind, the
   connection id and the request fields) pushed onto one lock-free `ConcurrentQueue<WorldCommand>` owned by
   `AuthoritativeWorld`. Pushing does not take `_gate`.
2. **D2 — Drained at the start of the tick.** `StepFixedTick` drains the queue first, inside `_gate`, in arrival (FIFO)
   order, and runs each command through the **existing** `Try*` logic. Results and outbound packets go through the normal
   tick dispatch, so ordering is the tick's ordering.
3. **D3 — Handlers await the result.** The `IWorld*` ports expose `ValueTask<WorldMutationOutcome> Submit*Async(...)`
   (or `ValueTask<OpStatus>`). The returned task completes when the tick has processed the command; the handler then sends
   the `MutationResult` reply as today. Handlers copy `context.Connection` / `context.Sender` into locals before the
   first `await`. Added latency is at most one tick (33 ms at 30 Hz).
4. **D4 — Backpressure.** At most 16 queued commands per connection; beyond that `Submit*Async` returns
   `OpStatus.RateLimited` immediately without queueing. Commands from a connection that left the world complete with
   `OpStatus.Forbidden`. On shutdown, queued commands complete with `OpStatus.Conflict`.
5. **D5 — No wire change.** Packets, opcodes and `MutationResult` replies are unchanged; only reply timing moves to the
   end of the next tick.
6. **D6 — Synchronous `Try*` stays for the tick and tests.** The `Try*` methods remain on `AuthoritativeWorld` (the tick
   calls them, and tests use them directly) but are removed from the `IWorld*` ports, so `Carbon.Handlers` can only submit.
7. **D7 — Guard.** An architecture test asserts that `Carbon.Handlers` does not call `Try*` mutators on any world port.
8. **D8 — Incremental rollout**, one small PR each, behavior otherwise unchanged:
   1. Map mutations: harvest, place, demolish, door. Includes the queue, D4 and the D7 guard.
   2. Inventory: hotbar select, craft, move, upgrade, drop.
   3. Economy and progression: vendor buy/sell, repair, point allocation.

   Session lifecycle (`TryEnterWorld`, `LeaveWorld`, `TryGetPendingCharacterSnapshot`) stays synchronous: it must answer
   before the world has a player to tick.

## Alternatives Considered

- **Keep the lock (status quo).** Thread-safe, but leaves non-deterministic ordering, out-of-band sends and lock
  contention (Context 1–3). Rejected.
- **Fire-and-forget commands with replies sent from the tick.** No awaiting in handlers, but it needs a new reply path in
  `IWorldOutboundSink` for every request type and changes the handler contract more than D3. Rejected for now; D3 can move
  to it later without a wire change.
- **One actor (mailbox) per player.** Removes the global lock, but cross-player mutations (building, trading, loot) still
  need a world-level order. Too large a change for this problem. Rejected.

## Consequences

- Positive: deterministic, tick-ordered mutations; one dispatch path; I/O threads never wait on `_gate`; fixes TS-4 in the
  converted handlers.
- Negative: up to one tick of extra reply latency on these requests; one `TaskCompletionSource` (or pooled
  `IValueTaskSource`) per request, off the per-tick hot path; tests that drive handlers must step a tick.
