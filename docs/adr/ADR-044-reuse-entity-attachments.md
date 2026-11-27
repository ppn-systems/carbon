# ADR-044: Reuse Entity Attachments After Despawn

- **Date**: 2026-11-27
- **Status**: Accepted (by the orchestrator, with the conditions in D2–D6)
- **Resolves**: audit round 3 ALLOC-11; round 2 PERF-12; for the three types below, round 1 ECS-3
- **Amends**: the `carbon-engine-ecs` skill, which forbids heap state in components without saying where per-entity
  managed state goes
- **Scope**: `engine/Carbon.Simulation/ECS` (`IReusableAttachment`, `EntityAttachmentStore<T>`), `StatSheet`,
  `ThreatTable`, `PathWaypointBuffer`, `MonsterFactory`, `tests/`

## Context

Per-entity managed state lives in `EntityAttachmentStore<T>`, outside the unmanaged component pools: stat sheets, threat
tables, path buffers and status buffers. A despawn nulls the entity's slot in every store
(`EntityManager` → `IAttachmentStore.RemoveBySlot`), and the instance is garbage.

Every kill despawns a monster, and its cell respawns one after `respawnDelayTicks`. The new monster gets new attachments.
The tick guards never saw this, because nothing dies in their windows: audit round 3 ALLOC-9 traced the guards' second
window to the first respawn wave. Measured through the real world (`RespawnAllocationTests`: kill every live monster,
despawn it as `CombatService` does, tick until the cells respawn):

| per respawn, steady state | bytes |
|---|---|
| `StatSheet` (`MonsterFactory.cs:63`): object, two `float[StatCount]`, `List<StatModifier>(16)` | 408 |
| `ThreatTable` (`AuthoritativeWorld.Creatures.cs:77`): object, `ThreatEntry[8]` | 168 |
| **total, both probe windows (72 and 107 respawns)** | **576.0** |
| `PathWaypointBuffer`, lazily on a monster's first path (`AiBehaviorSystem.cs:527,602`): object, `(int, int)[128]` | about 1,080 |
| **a pathing golem's life** (`MonsterTacticsTests`) | **1,656** |

`StatusEffectBuffer` is attached once, at a player's world entry, so it is not part of a respawn. Players carry no
`StatSheet`: only `MonsterFactory` and `ArmorDamageMitigation` touch one.

## Decision

1. **D1 — Opt-in reuse in the store.** `IReusableAttachment` (`ResetForReuse()`) marks a type whose instances the store
   reuses. When a reusable attachment is released by a despawn, a `Remove` or a replacing `Set`, the store calls
   `ResetForReuse` and keeps the instance. `GetOrAdd` hands out a kept instance before calling its factory.
   `StatSheet`, `ThreatTable` and `PathWaypointBuffer` opt in. `MonsterFactory` takes its sheet through `GetOrAdd`
   instead of `new` plus `Set`. Other attachment types behave as before.
2. **D2 — Reset equals fresh.** `ResetForReuse` brings an instance to a fresh one's state: lists keep their capacity but
   are cleared, and arrays are zeroed (a `ThreatTable` whose entries grew keeps the longer, zeroed array).
   `AttachmentResetCoverageTests` proves it by reflection over every reusable type in the Carbon assemblies:
   - it writes every field (arrays filled, lists given entries, scalars byte-filled), resets, and compares with a fresh
     instance;
   - the explicit list of deviations is empty;
   - a field it cannot write fails the test until a sample is added, so a new field is covered.
   This also covers a sheet passing between entities: no modifier or base value survives the reset.
3. **D3 — No holder keeps an attachment.** A reference kept past despawn would see the next entity's state, so nothing may
   keep one past the call that fetched it. Holders keep the `Entity` handle, which fails the store's generation check once
   the entity is despawned.
   - **Audit:** at the time of this ADR, every use of the three types is a local fetched with `TryGet` or `GetOrAdd` in
     the same call: `AiBehaviorSystem`, `AiPack`, `CombatService`, `ArmorDamageMitigation`, `TickAndAoi` and the three
     spawn sites. No field, property or closure keeps one.
   - **Guard:** `AttachmentReuseGuardTests` fails on any field or property whose type names a reusable attachment
     (generic arguments included) in `engine`, `server`, `desktop`, `wasm` or `graphics`. It also fails on any
     non-static lambda or local function that refers to a local or parameter of such a type declared outside it.
   - **Test:** `Respawn_IntoTheSameSlot_TheOldHandleCannotReachTheNewLife` despawns a monster and respawns one into the
     same slot. The old handle reaches neither attachment, and the reused threat table starts empty.
   - An instance is attached to one entity at a time. Every spawn site creates or takes one per entity.
4. **D4 — Bounded.** A new instance is created only when none is kept. So the kept instances plus the attached ones
   never exceed the peak number attached at once. `RespawnAllocationTests` asserts that bound against the peak live
   monster count, and that the repeat window of `AllocationProbe` allocates 0 B (no growth from cycle to cycle).
5. **D5 — Guards at 0 B.** `RespawnAllocationTests` measures a whole kill-and-respawn cycle of the 8-player world after
   two warm-up cycles: 0 B in both windows. `Respawn_PathingGolemIntoTheSameSlot_ReusesItsAttachmentsAndAllocatesNothing`
   covers `PathWaypointBuffer`: a golem paths around a wall each life, and a life allocates 0 B.
6. **D6 — Docs.** The `carbon-engine-ecs` skill names the store as the place for per-entity managed state, and states the
   reuse and no-holder rules.

## Alternatives Considered

- **Fixed-capacity unmanaged components** (`[InlineArray]` in `ComponentPool<T>`, round 1 ECS-3). This is the cleaner
  long-term shape, but it changes every user of the three types. It also puts 128 waypoints (1 KB) per entity into the
  dense pool, including entities that never path. Not rejected for good, only not needed to reach 0 B.
- **A separate object pool per type at each spawn site.** It duplicates the release logic in every despawn path
  (combat, leash, boss lair, settlement NPCs). The store already sees every release.
- **Reset on reuse instead of on release.** A released instance would then hold the old entity's state until reused.
  Resetting on release, as ADR-043 does for packets, leaves no stale state anywhere.

## Consequences

- A steady kill-and-respawn costs 0 B on the tick thread, down from 576 B per respawn (1,656 B for a pathing monster).
- Keeping a reusable attachment past its call is now a bug. The guard catches fields, properties and closures; a value
  passed to a method that stores it under another type is not caught. Review new holders against D3.
- The store keeps up to the peak number of instances of each reusable type alive after their entities are gone.
- Only the three types opt in. Another type's respawn cost remains until it opts in, with its reset covered by D2's test.
- Kill rewards (`_onMonsterKilled`: loot rolls, XP) are not in these guards. They run per kill, not per respawn, and are
  a separate measurement.
