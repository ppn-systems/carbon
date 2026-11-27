# Monster AI: evaluation and tactical upgrade

This document evaluates the monster AI as it was before this change, lists its weaknesses, and describes the target
design and what this change implements. Attack timelines, scaling and spawning are covered in
[monsters.md](monsters.md). Attack phases on the wire are covered in
[ADR-013](adr/ADR-013-entity-attack-phase-packet.md).

## Pipeline

| Phase | System | Role |
| :--- | :--- | :--- |
| PreUpdate | `AiBehaviorSystem` (`engine/Carbon.AI`) | State machine: target selection, chase, leash, flee, pathing. |
| PreUpdate | `MonsterBrainSystem` (`engine/Carbon.Creatures`) | In `Attack`: picks an attack profile and positions the monster by its role. |
| PreUpdate | `AttackSystem` (`engine/Carbon.Combat`) | Attack timelines (`AttackPhaseChangedEvent`, ADR-013). |
| Server | `CombatService.ApplyMonsterHits` | Player hits add threat, force a chase, and alert the pack. |

## State machine

```
            ┌──────── proximity aggro / threat ────────┐
 Idle/Patrol ──────────────────────────────────────────► Chase ◄──────── flee ends ────────┐
      ▲                                                  │  ▲                               │
      │ home reached:                       in range +   │  │ target out of range / no LOS  │
      │ full HP, reset                      clearance    ▼  │ / threat leader switches      │
 ReturnHome ◄── leash / give-up / target lost ────────  Attack ── HP <= fleeHealthFraction ─► Flee
      ▲                                                                                     │
      └───────────────────────────── leash / target lost ──────────────────────────────────┘
```

- `Idle/Patrol` look for a target: the threat table first, then the closest visible hostile in `aggroRange`
  (times `nightAggroMultiplier` at night). Getting a target calls the pack.
- `Chase` steers straight at the target when the path is clear. When it is not, it follows an A* path on the tile
  grid. Near the target, melee monsters steer to a surround slot. `Chase` also counts `ChaseTicks` for the give-up
  timer.
- `Attack` belongs to `MonsterBrainSystem` (`ExternalAttackControl`). The AI layer only handles the transitions:
  leash, flee, threat switch, and target out of range or out of sight.
- `Flee` runs away from the target for `fleeTicks`, sliding along walls, then fights again. A monster flees at most
  once per engagement. `HasFled` is reset when it gets home.
- `ReturnHome` walks home (with A* if needed) and regenerates 25% of max HP per second. On arrival it restores full
  HP and clears threat.

## Evaluation of the previous behaviour

| Area | Before | Weakness |
| :--- | :--- | :--- |
| Ranged kiting | Back off in a straight line below `retreatRange` and stop if a 24 px probe hits a wall. Shoot whenever a profile is in range. | A shot was allowed right at `retreatRange`, and the archer is rooted for about 1 s per shot, so a player walking at 40% of its speed caught it after a few shots. No strafing: archers stood still between shots. They froze against walls. |
| Casters | Same as ranged. Spell windups already telegraph through `AttackPhaseChangedEvent` (target position and radius). | No preferred distance: casters drifted between the retreat and engage edges. |
| Melee approach | Converge on the target center, with a fixed ±20% perpendicular bias by entity id parity and crowd separation in `Chase` only. | Packs queued on the approach side and stacked on one or two tiles (separation stops once in `Attack`, where monsters are busy for about 90% of the time). |
| Pathing | A* when line of sight or clearance is blocked, repathing every 10 ticks per monster. | No global budget: N blocked monsters meant N searches in the same tick (spawn bursts all start with cooldown 0). The node cap was the pathfinder default (2048). |
| Leash | Distance from home only, regeneration while returning. | A target that is unreachable but inside the leash (on an island, in a walled box) kept the monster pressed against a wall forever. |
| Low HP | — | Monsters fought to the death. |
| Group aggro | Server only: a player hit alerted every monster within 160 px, of any species. | Golems answered slimes. Proximity pulls never alerted anyone. The radius was hard-coded. |
| Threat | The threat leader replaced the target on every tick. | The target flip-flopped between near-equal attackers (two players trading the top spot each hit). No taunt hook. |
| Day / night | Night raised stats, density and elite chance at spawn. | Behaviour was identical by day and night. |

## Target design

Behaviour is data. Each monster in `resources/config/monsters.json` has an optional `behavior` block, and every key
has a conservative default. Generic mechanics live in `engine/Carbon.AI` (`AiTacticsComponent`, `AiPack`,
`AiSteering`, `ThreatTable`). Creature roles live in `MonsterBrainSystem`. Actors without `AiTacticsComponent`, such
as settlement NPCs and legacy monsters, behave exactly as before.

| Key | Default | Meaning |
| :--- | :--- | :--- |
| `role` | `Melee` | `Melee`, `Ranged` or `Caster`: how the monster positions itself in `Attack`. |
| `preferredRange` | midpoint of `retreatRange` and `engageRange` | Distance ranged monsters and casters hold. Must be in `(retreatRange, engageRange]`. |
| `rangeTolerance` | 24 | Half width of the band around `preferredRange`. Kiters shoot only inside the band unless cornered. |
| `strafeSpeedFactor` | 0 | Fraction of move speed used to circle-strafe while holding the band. |
| `strafeSwitchTicks` | 45 | Ticks between strafe direction flips (deterministic from tick and entity id). |
| `surroundRadius` | 0 | Melee ring radius. Chasers claim distinct slots on it. Must be below `engageRange`. |
| `pack` | none | Pack name. Monsters with the same pack assist each other. |
| `assistRadius` | 0 | Radius in which pack allies answer a call (requires `pack`). |
| `fleeHealthFraction` | 0 | Flee once at or below this HP fraction (0 = never). |
| `fleeTicks` | 0 | Length of a flee episode. |
| `giveUpTicks` | 0 | Chase ticks without reaching attack range before evading home (0 = never). |
| `threatSwitchMargin` | 1.1 | A new threat leader must exceed the current target's threat by this factor. |
| `nightAggroMultiplier` | 1 | Aggro and assist radius multiplier at night. |
| `nightLeashMultiplier` | 1 | Leash multiplier at night. Night leash must stay at or beyond night aggro. |

### Shipped personalities

| Monster | Role | Pack | Notable |
| :--- | :--- | :--- | :--- |
| `wild_slime`, `slime_king` | Melee (surround 18 / 26) | `slime` | Slimes flee at 20%; the king never flees. |
| `rock_golem` | Melee (surround 20) | `golem` | Fearless, stubborn about targets (`threatSwitchMargin` 1.25). |
| `frost_wolf` | Melee (surround 18) | `wolf` | Assist radius 260, flees at 25%, night aggro x1.4 and leash x1.25. |
| `skeleton_archer` | Ranged (holds 170, strafes at 55%) | `undead` | Fearless, night aggro x1.3. |
| `ash_wraith` | Caster (holds 150, slow strafe) | — | Solitary. |
| `orc_shaman`, `orc_warlord` | Caster (holds 140) / Melee (surround 24) | `orc` | The shaman flees at 25% and its scream pulls the warlord. |
| `crimson_demon` | Melee (surround 22) | — | Solitary hunter, night aggro x1.35. |

### Mechanics

- **Kiting (ranged and caster).** Below `retreatRange` the monster backs off at 80% speed. Below the band it holds
  fire and keeps backing off, so every shot starts at `preferredRange - rangeTolerance` or farther. In the band and
  between shots it circle-strafes. All moves probe the straight direction, then 45° and 90° turns
  (`AiSteering.TryFindOpenDirection`), so the monster slides along walls. Close-range fallbacks fire only when every
  direction is blocked.
- **Caster telegraphs.** Spells already broadcast `Windup` with the target position and area radius (ADR-013).
  Casters are rooted for the whole windup, and the blast lands where the telegraph showed.
- **Surround and flank (melee).** `AiSteering.ComputeSurroundPoint` starts from the monster's own bearing to the
  target and pushes it away from the bearings of other monsters on the same target that are less than 90° away.
  Exact ties are split by entity id. The monster steers to that ring point from `max(2 x engageRange,
  6 x surroundRadius)` inward. In `Attack`, a monster crowded by a pack mate (closer than 2.2 radii) moves to its
  slot before it swings.
- **Pathing budget.** `AiBehaviorSystem.MaxPathSearchesPerTick` (default 8) caps A* searches per tick across all
  actors, and `MaxPathNodesPerSearch` (768) caps each search. An actor over budget keeps its previous path, or
  steers directly, and retries next tick. Each search sets a 10-tick (chase) or 15-tick (return) cooldown, so N
  requests are served within `ceil(N / budget)` ticks. `PathSearchesLastTick` and `PathSearchesDeferredLastTick`
  expose the load for metrics.
- **Leash, give-up and regeneration.** Leash is still measured from home. `giveUpTicks` adds an evade timer for
  unreachable targets. Being hit does not reset it, so a player cannot farm a monster from behind a wall. Return
  home regenerates HP.
- **Flee and call for help.** At the flee threshold the monster enters `Flee` and calls its pack with a heavy threat
  boost (half its max HP) against its attacker. Server hits do not cancel a flee.
- **Pack and social aggro.** `AiPack.CallForHelp` runs on a proximity pull, on flee, and on server hits for monsters
  with a pack. Only same-pack allies within `assistRadius` (times the night multiplier) answer. Allies that are
  fleeing or leashing are skipped. Alerted allies do not re-broadcast, so a pull never chains across a region.
  Monsters without a pack keep the legacy "any monster within 160 px" assist.
- **Threat.** `ThreatTable.SelectTarget` applies the switch margin. `ThreatTable.Taunt(taunter, untilTick)` lifts the
  taunter to the top threat and forces it as the target until `untilTick`. This is the hook for a future taunt skill.
- **Day and night.** The host sets `AiBehaviorSystem.IsNight` from the spawner's authoritative clock every tick
  (`AuthoritativeWorld.TickAndAoi.cs`). At night the aggro, assist and leash radii grow per monster.

### Constraints kept

- **Determinism.** No `System.Random`, no wall clock, and no trigonometry in steering: rotations use constants, and
  ties break by entity id. A mixed scenario run twice gives bit-identical positions
  (`Update_SameScenarioTwice_ProducesIdenticalPositions`).
- **Zero allocation.** Components are unmanaged. Scratch spans are `stackalloc`. Threat tables and path buffers are
  allocated once per entity, then reused. `Update_SteadyStateTacticsTicks_AllocateNothing` measures
  `GC.GetAllocatedBytesForCurrentThread` over 300 mixed ticks with kiting, surround, pack calls, flee and pathing.
- **CPU.** About 4.6 ms per tick for 500 monsters and 50 players in a walled arena (container measurement,
  `Update_FiveHundredMonstersWithWalls_StaysWithinTickAndPathBudget`).
- **Wire.** `AiState.Flee` is simulation-only. `AiState` is not replicated, so there is no protocol change.

## Tests

Scenarios are in `tests/engine/Carbon.Creatures.Tests/MonsterTacticsTests.cs` and use fixed 30 Hz tick stepping:

| Scenario | Test |
| :--- | :--- |
| Archer keeps range against an advancing player | `Update_PlayerAdvancingOnArcher_ArcherBacksOffAndKeepsShooting` |
| Archer strafes inside its band | `Update_ArcherAtPreferredRange_StrafesWhileHoldingBand` |
| Melee surround without overlap | `Update_FourGolemsApproachFromOneSide_SurroundTargetWithoutStacking` |
| Pathing around a wall | `Update_PlayerBehindWall_MeleePathsAroundWallAndHits` |
| Time-sliced A* budget | `Update_ManyMonstersNeedPathsSameTick_SearchesAreTimeSlicedByBudget` |
| Leash return and regeneration | `Update_PlayerOutrunsLeash_MonsterReturnsHomeAndRegenerates` |
| Give-up on an unreachable target | `Update_TargetUnreachable_MonsterGivesUpAfterGiveUpTicks` |
| Flee at low HP | `Update_WolfDropsToLowHealth_FleesThenReengages` |
| Pack assist (same pack only) | `Update_SlimeAggroes_SamePackAssistsAndOtherPackIgnores` |
| Threat switch margin and taunt | `Update_SecondPlayerOutThreatsFirst_SwitchesOnlyPastMarginAndHonorsTaunt` |
| Night aggression | `Update_AtNight_WolvesAggroFromFurther` |
| Caster telegraph | `Update_CasterSpell_TelegraphsTargetAreaBeforeImpact` |
| Determinism, zero allocation, 500-monster benchmark | see above |

`ThreatTable` unit tests are in `tests/engine/Carbon.AI.Tests`. Behaviour validation tests are in
`MonsterCatalogTests`.

## Not done (candidates)

- Flow fields for large packs chasing one target. The time-sliced per-actor A* is enough at current densities.
- A real taunt skill (only the `ThreatTable.Taunt` hook exists) and threat decay over time.
- Night multipliers on server-side hit assists (`CombatService` passes 1).
