# Monsters, Difficulty Scaling & Attack Kinds

Server-authoritative, data-driven creatures. The code lives in `engine/Carbon.Creatures` (definitions, scaling,
spawner, monster brain) and `engine/Carbon.Combat/Attacks` (attack timelines). Data lives in
`resources/config/monsters.json` and `resources/config/spawning.json`.

All durations are in **fixed simulation ticks** (the server runs at 30 Hz by default). All distances are in **world
units** (32 units = 1 tile).

## Pipeline

| Phase | System | Role |
| :--- | :--- | :--- |
| PreUpdate | `AiBehaviorSystem` (`engine/Carbon.AI`) | Idle / Patrol → Chase → Attack → ReturnHome (leash). With `AiBrainComponent.ExternalAttackControl = true` it only makes state transitions while in `Attack`. |
| PreUpdate | `MonsterBrainSystem` | In `Attack`: picks the attack, or positions the monster (kite, close in, hold). Interrupts attacks when the brain leashes home or loses its target. |
| PreUpdate | `AttackSystem` | Runs the attack timeline, drives lunge / leap velocity, resolves hits, spawns projectiles. |
| Physics | `ProjectileSystem` | Swept hit tests for projectiles spawned by `DefaultAttackEffects` (engine-only hosts; the server uses its own replicated projectiles). |
| PostUpdate | `MonsterSpawnerSystem` | Biome-aware, deterministic spawning around `SpawnAnchorComponent` entities. |

`CreatureSystems.Register(...)` registers the first three systems and the spawner in this order. Hit resolution
happens only inside the simulation. Clients only animate.

### Server integration

`AuthoritativeWorld` (`server/Carbon.Application/World/AuthoritativeWorld.Creatures.cs`) runs on `CreatureContent`
(`monsters.json` and `spawning.json`). The Host builds it from `ConfigRepository.Catalog`, the same bytes it serves to
clients (ADR-011). Clients never read these files: they render from replicated archetype ids and
`EntityAttackPhase`.

- **Spawning**: `MonsterSpawnerSystem` replaces chunk-based monster population and the old fixed 45 s respawn
  queue. Every player entity is a `SpawnAnchorComponent`. Entering the world requests an immediate scan.
- **Replication**: each `MonsterSpawnedEvent` is bound to a network id. Monsters replicate their `archetypeId`, and the
  client picks the vector rig from it.
- **Rewards**: kills roll the definition's `lootTableId` in economy's `loot.json`. The `WorldActorCatalog` drop is used
  only when no economy is configured. Kill XP comes from `progression.json` (`experience.kill`): `clamp(0.45 * maxHp + 4 * level, 25, 4000)`,
  doubled for elites, ×1.35 at night, scaled by the level difference to the killer (see
  [progression.md](progression.md) §5).
- **Armor**: a monster's scaled armor (StatSheet `Defense`) mitigates player hits by `100 / (100 + armor)`.
- **Hits and projectiles**: the server's `IAttackEffects` sends hits through the authoritative player damage path
  (dodge, defense or magic resist mitigation by the monster's level, death, `CombatHit`; see
  [progression.md](progression.md) §4). It spawns projectiles as replicated server projectiles
  (`AttackProfile.ProjectileVisual`).
- **Phase broadcast**: every `AttackPhaseChangedEvent` is broadcast to AoI observers as `EntityAttackPhase`
  (opcode `0x1238`, [ADR-013](adr/ADR-013-entity-attack-phase-packet.md)). Monsters no longer use
  `SkillCastBroadcast`.
- **Time of day**: `MonsterSpawnerSystem.TimeOfDayHours` is set every tick from
  `WorldProgression.ComputeTimeOfDayHours`.
- **Layers**: `spawning.json` `monsterLayer` and `targetLayerMask` must match the server collision layers
  (2 and 1).

## Monster definitions (`monsters.json`)

| Field | Meaning |
| :--- | :--- |
| `id` | Unique snake_case key. The catalog index follows file order. |
| `visualArchetype` | Client sprite / animation key. The server never sends sprite data. |
| `lootTableId` | Loot table key. By convention it equals `id` (economy owns `loot.json`). |
| `isElite`, `eliteVariant` | Elite flag and the elite variant spawned on an elite roll (e.g. `wild_slime` → `slime_king`). |
| `stats` | Level-1 daytime `hp`, `damage`, `armor` (Defense mitigation `100 / (100 + armor)`), `speed`. |
| `aggroRange`, `leashRange` | Detection radius. Tether radius around the spawn point. |
| `engageRange` | Distance where chasing stops and fighting starts (`AiBrainComponent.AttackRange`). |
| `retreatRange` | Kiting threshold: if the target is closer, the monster backs off (0 = brawler). |
| `preferredBiomes` | `BiomeType` names. Spawn tables may only list a monster in biomes it prefers (enforced by tests). |
| `habitat` | Water creatures only (ADR-032 D14): `{ "shallowest": tier, "deepest": tier }`, from `Shore` to `Abyss`. The creature spawns only from `waterSpawnTables` of those tiers. Its collider (`TerrainMask.WaterBound`) treats dry land and every other tier as a wall, it paths only through its tiers, and its own water does not slow it. Land monsters omit it. |
| `attacks` | Attack profiles in **priority order** (see below). |

`archetypeId` is the protocol id used for the rig (`WorldActorCatalog`, 101–109). Each elite variant has its own
archetype and rig, so clients can show elite status without any extra wire data.

| Monster | Archetype | Attacks (priority order) | Role |
| :--- | :--- | :--- | :--- |
| `wild_slime` | 101 | leap, bite | brawler |
| `slime_king` (elite of `wild_slime`) | 109 | big leap, bite | brawler |
| `rock_golem` | 102 | overhead slam | brawler |
| `frost_wolf` | 103 | pounce (leap), bite | brawler |
| `ash_wraith` | 104 | eruption (circle spell), dark bolt | caster (retreat 90) |
| `skeleton_archer` | 105 | bow shot, bow bash | ranged (retreat 110) |
| `orc_shaman` | 106 | hex cone (cone spell), bolt, staff whack | caster (retreat 80) |
| `orc_warlord` (elite of `orc_shaman`, levels 20–50) | 108 | war charge (leap), axe chop | brawler |
| `crimson_demon` | 107 | leap, cleave | brawler |
| `shore_crab` (water, Shore–Medium, levels 1–100) | 110 | claw pinch | brawler |
| `piranha_slime` (water, Shallow–Deep, levels 10–100) | 111 | lunge (leap), bite | brawler, `piranha` pack |
| `drift_jelly` (water, Medium–Deep, levels 10–100) | 112 | sting pulse (circle spell), tentacle lash | caster (retreat 50) |
| `abyss_eel` (water, Deep–Abyss, levels 25–100) | 113 | strike (leap), bite | brawler |
| `abyss_leviathan` (boss, Abyss, its lair's level) | 114 | tidal crush (circle spell), abyssal lunge (leap), tail sweep (240° arc) | boss brawler |

## Attack kinds and the animation phase contract

Every attack uses the same four-phase timeline: `Windup → Travel → Active → Recovery`. Its meaning depends on the
`AttackKind`. A phase with 0 ticks is skipped and never reported. **Damage is resolved only during `Active`.**

| Kind | Windup | Travel | Active | Recovery |
| :--- | :--- | :--- | :--- | :--- |
| `Melee` | Wind-up telegraph (direction locked) | Lunge `lungeDistance` along the locked direction | Hit frames: arc (`areaRadius`, `halfAngleDegrees`) is checked **every active tick**, each target is hit at most once | Recovery |
| `Projectile` | Aim: the aim point **tracks the target** | (none) | Release on the first active tick: spawns a projectile entity (`projectileSpeed`, `projectileRadius`) and publishes `ProjectileLaunchedEvent` | Recovery |
| `Spell` | Cast channel: ground telegraph **locked at cast start** (`Circle` at the target point, or `Cone` from the caster) | Delayed detonation: telegraph stays on the ground | Detonation on the first active tick: everyone inside the shape is hit | Recovery |
| `Leap` | Crouch: landing point locked (clamped to `maxRange`) | Airborne: travels to the landing point | Landing impact: circle (`areaRadius`) around the landing position on the first active tick | Recovery |

### Events

`AttackPhaseChangedEvent` is published on the **first tick of every phase**. It is also published with
`Phase = None` when the attack finishes (`Progress = 1`) or is interrupted (`Interrupted = true`, for example after a
stun or a leash).

| Field | Renderer use |
| :--- | :--- |
| `Attacker`, `Kind`, `Phase`, `ProfileId` | Select the animation clip for `(visualArchetype, Kind, Phase)`. |
| `Origin`, `TargetPosition`, `Direction` | Facing, lunge or leap path, telegraph placement, aim point. |
| `AreaRadius`, `HalfAngleRadians`, `Shape` | Draw the ground telegraph (melee arc, spell circle or cone, leap landing circle). |
| `PhaseDurationTicks` | Play the clip over `PhaseDurationTicks / tickRate` seconds. |
| `Progress` | Normalized progress through the **whole** attack when the phase starts. Use it to resync late joiners. |

`ProjectileLaunchedEvent(Owner, Projectile, Origin, Velocity, ArcHeight, FlightTicks)` carries the release. The
simulation is planar, so the client draws a parabola with apex `ArcHeight` over `FlightTicks`. The projectile entity is
replicated like any other entity.

Renderer rules:

1. Never infer hits from animation. Health changes arrive through `DamageEvent` / health replication.
2. Advance the phase animation locally from `PhaseDurationTicks`. Do not wait for per-tick updates (none are sent).
3. On `Phase = None` with `Interrupted = true`, blend straight back to idle.
4. Show the telegraph from the `Windup` event until the `Active` event for `Spell` and `Leap`, and during `Windup` for
   `Melee`.

`AttackStateComponent.PhaseProgress` / `TotalProgress` expose the same data to snapshot-based consumers.

### Desktop clips

The desktop receives `EntityAttackPhase` in `MultiplayerSession`. It exposes the phase as
`RemoteEntityView.Attack` (`AttackPhaseView`) and advances it locally between packets.
`MonsterAttackAnimator.ResolveClip` picks one clip per kind:

| Kind | Clip | Fallback |
| :--- | :--- | :--- |
| Melee | `attack_slam` for golem and demon, otherwise `attack` | none |
| Projectile | `attack_bow`, otherwise `attack_cast` | `attack` |
| Spell | `attack_spell` | `attack_cast`, then `attack` |
| Leap | `attack_leap` | `attack` |

Each attack clip declares `phaseMarks` (`windupEnd`, `travelEnd`, `activeEnd`) in normalized clip time.
`MonsterAttackAnimator.ToClipTime` maps (phase, phase progress) into the matching segment. This keeps the key poses
(full draw, overhead apex, airborne apex, impact) on the server's phase boundaries for any profile duration.
`AttackTelegraphRenderer` draws the ground telegraphs.

### Night and elite overlays

Night empowerment and elite status are painted on the rig itself. They are never scene lights, and players never
glow.

- **Night** (client time of day is a night hour): `RigOverlay.NightGlow` draws glowing eyes at the rig's
  `markers.eyes` / `markers.eyesSide` anchors on the head bone, plus a faint body aura in `markers.eyeGlowColor`.
  Eyes are hidden when the rig faces north.
- **Elite** (art catalog entry has `"elite": true`, currently `orc_warlord` and `slime_king`):
  `RigOverlay.EliteCrown` draws a gold crown pip at `markers.crown`. Elite rigs are also larger
  (`worldHeight`).

`dotnet run --project desktop/Carbon.Desktop -c Release -- --showcase <dir>` writes:

- `monsters_attack_phases.bmp`: a filmstrip of every monster attack across its phases;
- `monsters_day_night_elite.bmp`: every rig by day and by night, normal and elite.

Use them to review clip and overlay changes.

## AI positioning

While the brain is in `Attack` and no attack is running, `MonsterBrainSystem` positions the monster by its
`behavior.role`:

1. If the target is inside `retreatRange`, the monster backs off at 80% speed and slides along walls. Ranged monsters
   and casters restore their spacing before they use close-range fallbacks. They use those fallbacks only when
   cornered.
2. Ranged monsters and casters hold fire below `preferredRange - rangeTolerance` and keep backing off. Crowded melee
   monsters step to a free surround slot first.
3. Otherwise the first **ready** profile whose `[minRange, maxRange]` contains the target starts.
4. Otherwise the monster closes in (melee monsters move to their surround slot) if a ready profile needs it closer,
   backs off if one needs it farther, strafes inside its band (ranged and caster), or holds.

The AI layer handles leashing, give-up, fleeing, pack assist and threat switching. When the monster leashes home it
heals, and any running attack is interrupted. See [monster-ai.md](monster-ai.md) for the state machine, the `behavior`
keys and the tests.

## Difficulty scaling (`spawning.json`)

`MonsterScaling.Compute(distance, isNight, def)` returns `ScaledMonsterStats` (level, band index, hp, damage, armor,
speed). The distance is measured from the **world origin / first player spawn point**, which the host passes to the
spawner.

### Level curve

`level.bands` are contiguous distance bands. Each band starts where the previous one ends, at the same level. Inside a
band the level eases with a smoothstep:

```
t     = (d - startDistance) / (endDistance - startDistance)
level = startLevel + (endLevel - startLevel) * t² (3 - 2t)
```

Beyond the last band: `level = lastEndLevel + (d - lastEndDistance) * levelsPerUnitBeyond`, capped at `maxLevel`. The
integer level is `floor(level)`. The curve is continuous and never decreases. Each band reads as a plateau joined to
the next by a smooth ramp.

| Band | Distance (units) | Tiles | Levels |
| :--- | :--- | :--- | :--- |
| `heartland` | 0 – 6 400 | 0 – 200 | 1 → 8 |
| `frontier` | 6 400 – 32 000 | 200 – 1 000 | 8 → 25 |
| `outer_wilds` | 32 000 – 80 000 | 1 000 – 2 500 | 25 → 50 |
| `endgame` | 80 000 – 160 000 | 2 500 – 5 000 | 50 → 80 |
| beyond | > 160 000 | > 5 000 | +1 per 2 000 units, max 100 |

Each monster also declares `minLevel` / `maxLevel`, matching its `loot.json` band, and spawns only where the
spawn-point level lies inside that range. Elite variants follow the same rule. As a result slimes (1–15) dominate
near spawn and demons (45–100) only appear in the endgame band. `MonsterScalingTests` checks that every spawnable
range overlaps its loot band.

### Stat curves

Each stat is multiplied by `min(maxMultiplier, (1 + perLevel * (level - 1)) ^ exponent)`:

| Stat | perLevel | exponent | cap |
| :--- | :--- | :--- | :--- |
| hp | 0.14 | 1.15 | ×40 |
| damage | 0.10 | 1.10 | ×25 |
| armor | 0.08 | 1.0 | ×10 |
| speed | 0.005 | 1.0 | ×1.3 |

### Night

Night is decided from `MonsterSpawnerSystem.TimeOfDayHours`, an input the host sets every tick from authoritative
server time (e.g. `WorldProgression.ComputeTimeOfDayHours`). The engine never reads client atmosphere state.
`night.startHour = 19` and `night.endHour = 5` wrap past midnight. At night:

- hp, damage, and armor × `statMultiplier` (1.3),
- per-cell cap and spawn rate × `densityMultiplier` (1.5, so a cap of 2 becomes 3),
- elite chance `eliteChance` (12%) instead of `dayEliteChance` (2%),
- night-only table weights (`nightWeight`) favour skeleton archers, orc shamans, and crimson demons.

Empowerment is applied when a monster spawns: monsters spawned during the day keep their daytime stats.

## Spawner

- **Cells**: the world is split into `cellSize` (768) density cells. Every `scanIntervalTicks` the spawner visits the
  cells within `activationRadiusCells` of each anchor entity (`SpawnAnchorComponent`, typically players). Each cell is
  visited once per scan.
- **Caps and timers**: a cell spawns while its live count is below the cap and `spawnIntervalTicks` have passed since
  its last attempt. When one of its monsters dies, the cell waits `respawnDelayTicks` before spawning again.
- **Density budget**: with a 768 cell and a cap of 2 (3 at night), a fully populated area shows about 3 monsters on a
  1280×720 screen by day and about 5 at night. `spawnIntervalTicks` (300, 10 s at 30 Hz) and `respawnDelayTicks` (1800, 60 s)
  keep cleared areas quiet for a while. `ShippedConfig_SpawnDensity_StaysWithinScreenBudget` fails CI if a config
  change pushes the per-screen count above 3.5 (day) or 5.5 (night).
- **Placement retries**: each spawn opportunity samples up to `placementAttempts` (6) candidate points. A rejected
  point (safe zone, anchor distance, blocked tile, no monster eligible at the level) retries at once, so the cells
  around a player who just entered (`RequestScan`) populate on the first scan instead of after several intervals.
- **Safe zone**: no spawn within `safeZoneRadius` (480, 15 tiles) of the world origin, and none within
  `minSpawnDistanceFromAnchor` (384, 12 tiles) of any anchor. A larger safe zone empties the whole activation area
  of a player standing at the world spawn.
- **Biome tables**: `ISpawnSurface` rejects blocked candidate points (solid tiles, water, solid props) and returns
  the biome, which selects a weighted `spawnTables` entry. `ChunkWorldSpawnSurface` adapts `ChunkWorld2D`. Biomes
  without a table never spawn monsters.
- **Water tables** (ADR-032 D14): a candidate point in open water (no prop, structure or deck) samples its depth
  tier instead (`ISpawnSurface.TrySampleSpawnWater`) and picks from that tier's `waterSpawnTables` entry. Water and land
  share each cell's cap, so a coast is no busier than inland.

  | Tier | Creatures (weight) |
  | :--- | :--- |
  | Shore | `shore_crab` 100 |
  | Shallow | `shore_crab` 40, `piranha_slime` 60 |
  | Medium | `shore_crab` 20, `piranha_slime` 45 (night 35), `drift_jelly` 35 (night 45) |
  | Deep | `piranha_slime` 20, `drift_jelly` 35, `abyss_eel` 45 (night 60) |
  | Abyss | `abyss_eel` 100 |

  Level ranges apply as on land, so the water near the world spawn holds only crabs.
- **Determinism**: every attempt seeds `DeterministicRandom` from `PRF(key, cell, attemptIndex, tick)`, where `key` is
  the server-only epoch subkey (ADR-026), never the public world seed. The same key and the same anchor history always
  produce the same monsters, positions, levels, and elites; a client that knows the world seed cannot predict elites.
- `MonsterSpawnedEvent(Monster, MonsterIndex, Level, IsElite, IsNight, Position)` lets the host resolve
  `visualArchetype` and `lootTableId` from the catalog for replication and drops.

## Abyss boss (ADR-032 D15)

`BossLairSystem` (PostUpdate, registered by `CreatureSystems.Register` when the host passes an `ILairSource` and
`spawning.json` has a `bossLairs` block) raises `bossLairs.monster` at its lair:

- **Lairs**: `ChunkWorldLairSource` wraps `AbyssLairs`. Lairs are at most one per 1024-tile cell, at an open-abyss
  site fixed by the world seed (`lairChance` 0.35). Each cell is looked up once and cached.
- **Rising**: a player within `activationRadius` (1280 units, 40 tiles) of a lair raises its boss. Its level follows
  the lair's distance from the origin.
- **Respawn**: after the boss dies, the lair waits `respawnTicks` (54 000, 30 min at 30 Hz).
- **Sinking back**: with no player within the radius for `idleDespawnTicks` (3600, 2 min), the boss despawns and rises
  again whole when a player returns. It never despawns while a player in its chunk is fighting it: a target or a wound
  within `combatGraceTicks` (600, 20 s) holds it.
- A boss (`isBoss` in `monsters.json`) never appears in a spawn table, and its loot table must be `"boss": true`.

## Validation

`MonsterCatalog.FromJson` and `SpawningConfig.FromJson` reject unknown JSON members and unknown enum names
(`JsonException`). They also reject inconsistent data (`ArgumentException`): missing loot or visual keys, empty attack
lists, invalid range windows, non-contiguous level bands, night multipliers below 1, and unknown monsters in spawn
tables. A land table that lists a water creature, a water table that lists a creature not living in its tier, and a
habitat that is not a Shore..Abyss range are rejected too. Tests in `tests/engine/Carbon.Creatures.Tests` run against
the shipped files.
