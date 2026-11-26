# ADR-013: EntityAttackPhase Packet for Server-Authoritative Monster Attack Timelines

- **Date**: 2026-11-26
- **Status**: Accepted
- **Scope**: `contracts/`, `engine/`, `server/`, `desktop/`, `tests/`

## Context

Monster attacks used to be resolved instantly. `AuthoritativeWorld.ProcessAiAttacksLocked`
(`server/Carbon.Application/World/AuthoritativeWorld.TickAndAoi.cs`) applied damage on `AiAttackReadyEvent` and sent
one `SkillCastBroadcast`. Its `AbilitySlot` byte was overloaded with magic offsets
(`AbilityInputEncoding.MonsterProjectileCastOffset = 220`, `MonsterMeleeCastOffset = 230`) so the client could guess
an animation.

The data-driven creature work (`engine/Carbon.Combat/Attacks`, `engine/Carbon.Creatures`, `docs/monsters.md`) runs
every attack through a `Windup → Travel → Active → Recovery` timeline. It also exposes four kinds that behave
differently: melee, projectile, spell, and leap. Clients need to know when each phase starts, how long it lasts, and
where the telegraph is (locked target point, direction, radius, cone angle). Only then can they play distinct
animations and draw honest ground telegraphs. Damage is resolved only in the simulation's `Active` phase.

Encoding phases into the ability id space would mix two unrelated concepts. It would also consume the remaining byte
range and still could not carry radius, angle, or duration.

## Decision

1. **D1: dedicated S2C packet.** Add `EntityAttackPhase` (`contracts/Carbon.Contracts.Entities/S2C`) at opcode
   `EntityOpCode.EntityAttackPhase = 0x1238`, inside the Entities S2C band (`0x1230–0x12FF`). Fields, in
   `[SerializeOrder]` order:
   - `NetworkId`
   - `ServerTick`
   - `AttackKind` (`0` melee, `1` projectile, `2` spell, `3` leap)
   - `Phase` (`0` none, `1` windup, `2` travel, `3` active, `4` recovery)
   - `Shape` (`0` circle, `1` cone)
   - `Interrupted`
   - `TargetX`, `TargetY`
   - `DirectionX`, `DirectionY`
   - `AreaRadius`
   - `HalfAngleRadians`
   - `PhaseDurationTicks`
   - `Progress`

   The byte values mirror `Carbon.Combat.Attacks.AttackKind`, `AttackPhase`, and `AttackAreaShape`.
2. **D2: one packet per phase start.** The server publishes a packet for every `AttackPhaseChangedEvent`, including
   `Phase = None` at completion or interruption. It sends each packet only to the attacker's AoI observers. No packet
   is sent per tick: clients advance the phase locally from `PhaseDurationTicks`, and use `ServerTick` and `Progress`
   to resync late or delayed packets.
3. **D3: server authority.** The packet is purely presentational. Damage still reaches clients only through
   `CombatHit`. Projectile attacks still spawn a replicated projectile entity.
4. **D4: monster attacks stop using `SkillCastBroadcast`.** Monsters no longer send `SkillCastBroadcast` with the
   monster cast offsets. The `AbilityInputEncoding` monster helpers stay, so older clients keep decoding. They are no
   longer emitted for monsters.

## Alternatives Considered

- **Overload `SkillCastBroadcast.AbilitySlot` with kind and phase.** Rejected. It conflates abilities with attack
  phases, exhausts the byte space, and cannot carry geometry or duration.
- **Replicate `AttackStateComponent` inside `EntityDeltaBatch`.** Rejected. It adds per-tick bandwidth for every
  entity, even though the state only changes a few times per attack. It would also change an existing packet's layout.
- **Send per-tick progress.** Rejected for bandwidth. Local advancement from the duration is exact under the fixed
  tick.

## Consequences

- The change is additive: no existing packet or field is removed or renumbered, so no protocol version bump is needed.
  Old clients ignore the unknown opcode and lose only the new animations.
- Renderers get a stable contract, documented in `docs/monsters.md`. They play the clip for (archetype, kind, phase)
  over the phase duration and draw the telegraph from `Target*`, `AreaRadius`, `HalfAngleRadians`, and `Shape`.
- Bandwidth is about 60 bytes per observer per phase. A typical attack has 3 to 5 phases.
- Contract tests cover the opcode band and uniqueness and the round-trip of every field
  (`tests/contracts/Carbon.Contracts.Tests/ProtocolContractGuardTests.cs`).
