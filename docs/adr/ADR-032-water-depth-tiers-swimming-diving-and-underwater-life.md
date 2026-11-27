# ADR-032: Water Depth Tiers, Swimming, Diving and Underwater Life

- **Date**: 2026-11-27
- **Status**: Accepted
- **Scope**: `engine/Carbon.Maps` (Hydrology, generation stages, landmarks, roads), `engine/Carbon.Simulation`,
  `engine/Carbon.Survival`, `engine/Carbon.Creatures`, `contracts/Carbon.Contracts.Items` (opcodes 0x4080–0x4087),
  `server/Carbon.Application` (World), `desktop/` (rendering, HUD), `resources/` (config, art, localization)
- **Delivered as**: four pull requests, each mergeable on its own: (1) generation, (2) server movement and oxygen,
  (3) client underwater view, (4) water creatures and the abyss boss.

## Context

The owner's request: "The black sea is deep water: make it enterable, so you actually go into the water, descending
step by step like terrain levels. Underwater: darker, a smaller view radius, remaining oxygen, monsters and a boss.
The rivers look rather small, yet they have black water too? Optimise and rework the map generation."

State before this ADR (generator version 16; 17 and 18 added starter forage and street villages, not water):

- **Water was two biomes cut from noise.** `WhittakerBiomeResolver.Resolve` made every tile below elevation 0.22
  `DeepOcean` and every tile below `SeaLevel` (0.32) `ShallowWater` (`engine/Carbon.Maps/Biomes/WhittakerBiomeResolver.cs:21-29`).
  The sea floor was therefore the continental noise itself. On 90% of measured shore transects the depth rose again
  by more than 0.01 within 300 tiles (seeds 42, 1337 and 7).
- **Rivers were black because of their flow count, not their width.** `RiverNetwork.BuildRegion` marked a segment
  "deep" when `flow >= 4` or at an estuary, and stamped a `DeepMask` core wherever the width-scaled distance was
  ≤ 0.85 (`engine/Carbon.Maps/Hydrology/RiverNetwork.cs`, v16 lines 474 and 718). `HydrologyStage` turned that core
  into `DeepOcean` tiles with a fixed elevation of 0.25. So a channel 7 tiles wide fed by four springs got a solid,
  navy-black stripe down its middle. River width came from an 18-entry table that saturated at a 5.7-tile
  half-width, so trunk rivers never grew past about 11 tiles.
- **Readers decoded the fixed elevations.** Landmarks told the sea from deep rivers by `Elevation < 0.22`, roads used
  `Elevation < 0.23` and `>= 0.27`, and bridges used `>= 0.24` (`PointOfInterestResolver.TryFindSea`,
  `RoadNetwork` coarse grid, `RoadStage`).
- **Deep water is solid.** `BiomeProperties` marks `DeepOcean` `IsSolid` with a speed of 0
  (`engine/Carbon.Maps/Biomes/BiomeProperties.cs:18`). `TileWalkabilityGrid` makes it impassable (`:147`) and
  `WorldCollision.Resolve` blocks it (`:115`).
- **Wading exists.** `WaterWading` slows a walker in `ShallowWater` from 0.75× at the waterline to 0.40× at full
  depth. That depth is a distance ramp to the nearest dry and `DeepOcean` tiles (`ChunkWorld2D.GetWaterDepth`,
  `engine/Carbon.Maps/Streaming/ChunkWorld2D.cs:561`). It is applied through `IWorldPhysics2D.GetMoveSpeedMultiplier`
  in `WorldSimulation` (`engine/Carbon.Simulation/World/WorldSimulation.cs:667`). That path is shared by server
  players, client prediction and creature AI.
- **Movement speed** is `(walk 250 | sprint 450) × gear MoveSpeed × needs MoveSpeedScale` on the server
  (`server/Carbon.Application/World/AuthoritativeWorld.TickAndAoi.cs:65`), then multiplied by the terrain value.
- **Needs** (ADR-015, ADR-018) are hunger, rest and mood in `NeedsComponent`. `NeedsActivity.Swimming` exists but
  is never set. Needs reach the owner only, inside `PlayerStatsSync` (0x1235).
- **Creatures** spawn from per-biome tables in `resources/config/spawning.json` that contain no water biome
  (a test enforces that). `ChunkWorldSpawnSurface` rejects water tiles. AI paths with `GridPathfinder` over
  `TileWalkabilityGrid`. There is no boss system: elites are `eliteVariant` links in `monsters.json`.
- **Cost (v16, seed 1337, Release, warm JIT, this container).**
  - A cached chunk took about 860 µs and allocated 32 B. The allocation was a closure in `SpawnClearingStage`,
    created on every call.
  - A fresh 512-tile hydrology region took about 30 ms: priority flood ~6 ms, segment stamping 3–20 ms, lake
    shores 2–8 ms.
  - Elevation and climate noise are about 70% of chunk time.

Terrain is generated on the client from the public world seed (ADR-026). Generation must therefore stay a pure
function of `WorldGenSettings` and must never touch `GameplayEntropy`.

## Decision

### Generation (PR 1, generator version 19)

- **D1 — One continuous depth field.**
  - Water depth is measured in elevation units below the surface. A water tile stores it as
    `Elevation = SeaLevel − depth` (`WaterDepth.ToElevation`), so chunk deltas, the wire and every
    `MapTile.Elevation` reader keep working without a format change.
  - `WaterDepth.Of(tile)` reads the depth back.
  - Tiers are fixed cuts of the field (`WaterDepthTier`):

    | Tier | Depth from | Play | Biome |
    | :--- | :--- | :--- | :--- |
    | Shore | 0.004 | wade, light slow | ShallowWater |
    | Shallow | 0.015 | wade, slow | ShallowWater |
    | Medium | 0.045 | swim on the surface | ShallowWater |
    | Deep | 0.10 | dive | DeepOcean |
    | Abyss | 0.20 | dive into the dark | DeepOcean |

  - Deep starts at 0.10, which is the old 0.22 elevation line, so the sea's biome split is unchanged.
- **D2 — The sea deepens with distance from the coast.**
  - Each hydrology region also builds a chamfer distance-to-land field over its 16-tile routing cells. The window
    is 16 cells past the region on every side, and values are capped there. Because of the cap, neighbouring
    regions agree on shared cells and the sea floor has no seams.
  - Sea depth is `SeaDepthAtCoastDistance(d)`, a strictly increasing shelf: Shore within 4 tiles, Shallow to 14,
    Medium to 40, Deep to 120, Abyss beyond (at most 220).
  - The coastline itself still comes from the per-tile elevation.
  - Result: abyss covers about 1.5% of the world and deep water about 7.4% (seeds 42, 1337, 7).
- **D3 — Rivers widen downstream, and are as deep as they are wide.**
  - Half-width is `(2.2 + 1.7·√flow) × estuary flare`, capped at 18 tiles. `flow` is the number of springs
    upstream, so width grows at every confluence.
  - The bed is parabolic across the channel. Its maximum is `MaxDepthForChannelWidth(width − 1)`, which is
    0.0055 per tile of width, capped at 0.16, so inland water never reaches the abyss.
  - Consequences: a stream narrower than about 8 tiles is never more than waist-deep, and only a river at least
    18 tiles wide gets a diving-deep (navy) middle.
  - Natural fords clamp the depth to a wade every ~8 coarse zones.
  - Lakes deepen with their basin depth, from the waterline to a deep heart. Oasis pools never pass Shallow, and
    shoal out where their desert climate ends.
- **D4 — Banks blend.**
  - Outside the water, distance is counted in bank widths of at most 2.6 tiles, so wide rivers keep natural,
    not proportionally wider, valleys. The riparian lowering is unchanged.
  - The field is stamped out to 12.5 width-scaled tiles, the farthest any reader looks.
- **D5 — Every river drains.** A river ends in the sea, in another river, or in a pool at least 1.5× its own
  width (minimum radius 6). That includes a course that finds nowhere lower before the routing window's edge.
- **D6 — Readers use tiers, not magic elevations.**
  - Landmarks find the sea as water that is at least Shallow and outside the river field.
  - Roads bridge anything inside the river field and any water up to Shallow.
  - Chunk bridges clip river water and sea shallower than Deep.
  - The client darkens water smoothly with depth (`TerrainSurface.WaterDepthShade`).
- **D7 — Villages need room.** A village-sized site (radius ≥ 16) is chosen only if at least 6 of 8 points on
  its plot ring (0.7 × radius) are dry at its level. Wider rivers had exposed the 6-tile flatness probe: it
  accepted peninsulas that fit two buildings.
- **D8 — Versioning.** `GeneratorVersion` becomes 19 (17 and 18 landed meanwhile with the starter forage and the street villages). Stored chunk deltas migrate through the existing
  `ChunkDelta.MigrateTo` path: tile edits and player structures are kept, natural prop states and faction villages
  are dropped and regenerated.
- **D9 — Cost.**
  - No per-tile allocation; a cached chunk allocates 0 B (regression test).
  - River stretches are stamped in one pass per cell-to-cell curve, with each row clipped to the curve's reach.
    Before, there were six overlapping boxes per stretch.
  - Stretches that cannot reach the region are skipped before their spline is evaluated.
  - The hydrology region keeps its ~30 ms despite the wider rivers and the two extra fields.

### Swimming, diving and oxygen (PR 2, server)

- **D10 — Movement state from the tier.**
  - The tier of the tile under a player gives Walking (land, bridge, pier), Wading (Shore, Shallow), Swimming
    (Medium) or Diving (Deep, Abyss): `ChunkWorld2D.GetSwimStateAt`.
  - Speed comes from `resources/config/water.json` (a new ADR-014 document, synced to clients and hot-reloadable).
    It holds one multiplier per tier, taken at the depth where the tier starts, and blends linearly between them
    over a depth sampled bilinearly from the four nearest tile centres, so speed never steps at a tile border.
    This replaces the wading distance ramp in `ChunkWorld2D.GetMoveSpeedMultiplier`, the path shared by server
    players, client prediction and creatures.
  - Deep water stays `IsSolid`. A collider whose mask lacks `TerrainMask.DeepWater` (`SwimmingColliders.AsSwimmer`)
    passes through it in `WorldCollision.Resolve` and `TileWalkabilityGrid.CanTraverse(…, canSwim)`. Cliffs, props,
    structures and landmarks still block swimmers.
  - Players get that mask on the server and on the predicting client. Every other collider keeps the default mask,
    so deep water stays a wall to land creatures and in their path costs.
  - A swimming or diving player counts as the `Swimming` needs activity (ADR-015), which needs.json already rates.
  - On the ADR-033 D8 seam, each tick of swimming or diving strokes drains `stamina.json` `swimDrainPerSecond` (5)
    through `StaminaMeter.TryDrain`, so the regen delay and exhaustion apply to swimmers as they do to sprinters.
- **D11 — Oxygen is server-authoritative.**
  - The pool is `oxygen.maxSeconds` (30). It drains while diving (Deep 1/s, Abyss 1.5/s) and refills at 10/s
    anywhere else. It is stepped after the simulation tick (`OxygenService.Step`).
  - At 0, the server applies the ADR-029 `drowning` effect once: Refresh, 8 damage a second, lethal, and
    `healthRegenMultiplier` 0, so regeneration cannot outlast it. The effect is removed once on surfacing, so the
    owner's status list changes twice per drowning and not every tick.
  - A lethal drowning pulse takes the normal death path with the new `DeathCause.Drowning` (3, appended), which
    the death screen names (`death.cause.drowning`). Respawning refills the breath.
  - The effect has the `status_drowning` icon and the `status_effects.drowning.*` texts in the client's status strip.
  - Oxygen is **not persisted**: a player always enters the world with a full breath. A relog takes longer than
    surfacing, so persisting it would buy no fairness, and it would spend the reserved migration id 0015 for nothing.
- **D12 — Wire.**
  - New S2C `PlayerOxygenSync` (0x4080 in the free band 0x4080–0x4087), sent to the owning connection only. It
    carries the swim state, the depth tier, the oxygen left and the full pool, both in milliseconds.
  - It is sent at entry, and when the swim state or tier changes or the oxygen crosses a whole percent: about 1.5
    a second while diving in the abyss, and never on land at full breath.
  - No existing packet member is renumbered.

### Client (PR 3)

- **D13 — Underwater view.**
  - **What the client knows.** `WaterView` (swim state, tier, depth, oxygen, full pool) is read from
    `IWorldReadModel.LocalWater`. The swim state and depth come from the client-generated terrain under the
    *predicted* position, so the view changes the moment the player crosses a tier border. The oxygen is the last
    `PlayerOxygenSync` (D12); the client never simulates it.
  - **The filter** (`UnderwaterLook`, a pure function of the view, drawn first in the main HUD so it covers the
    world but not the HUD):
    - Walking and wading: nothing.
    - Swimming (head above the surface): a light blue-green tint and surface caustics, no fog.
    - Diving: from the top of Deep water to the abyss floor (depth 0.10 → 0.30) the tint darkens towards a near
      black blue, the clear circle of view shrinks from 0.52 to 0.22 of the screen's half diagonal inside a fog of
      rings, the caustics fade out, and bubbles rise from the player.
  - **The oxygen bar** sits centred just above the vitals (`HudLayout.GetOxygenBarRect`), clear of the hotbar, the
    status strip and the minimap column. It shows while diving, or until the breath has refilled, and turns red and
    pulses under a quarter. Its text is `water.oxygen_left` / `water.out_of_air`.
  - **Poses.** `base_hero.cvg.json` gains looping `swim` (0.9 s) and `dive` (1.1 s) clips. A swimmer sinks to the
    shoulders, and a diver is drawn under a water veil with rising bubbles, for the local player and for others.
  - All strings live in a new `[water]` section of `en.ini` and `vi.ini`.

### Creatures (PR 4a: water creatures; PR 4b: the abyss boss)

- **D14 — Water creatures (PR 4a).**
  - Four creatures, from the shore to the abyss. Each has a `habitat` (a range of tiers) in `monsters.json`:

    | Creature | Habitat | Levels | Role |
    | :--- | :--- | :--- | :--- |
    | `shore_crab` | Shore–Medium | 1–100 | claw brawler |
    | `piranha_slime` | Shallow–Deep | 10–100 | lunging pack biter |
    | `drift_jelly` | Medium–Deep | 10–100 | stinging caster |
    | `abyss_eel` | Deep–Abyss | 25–100 | striking brawler |

  - **Spawning.** A spawn point in open water picks from `spawning.json` `waterSpawnTables[tier]` instead of a biome
    table. Water and land share each density cell's cap and timers, so a coast is no busier than inland. The loader
    rejects a land table that lists a water creature, and a water table that lists a creature not living in its
    tier. Levels follow distance as on land, so the water near the world spawn holds only crabs.
  - **Movement.** A water creature's collider clears the new `TerrainMask.LandWalker` bit and keeps only its tiers'
    bits (`TerrainMask.WaterBound`).
    - `WorldCollision` and `TileWalkabilityGrid.CanTraverse` treat dry land and every other tier as a wall, and
      it swims under bridges and piers.
    - Its own water does not slow it (`IWorldPhysics2D.GetMoveSpeedMultiplier(position, mask)`).
    - Walkers and swimmers are unchanged.
  - **AI.** `AiBehaviorSystem` paths each creature on `ITileWalkabilityGrid.ForTerrainMask(mask)`, a cached view in
    which only its tiers are open. Line-of-travel checks walk that grid instead of the terrain raycast. A creature
    chases a player up to the edge of its water and no further.
  - **Loot.** Four `water`-themed tables (`crab`, `piranha_slime`, `jelly`, `eel`) drop existing items, now also
    tagged `water`. They stay inside every gold, diamond, net and sink-share band of `economy.json`.
  - **Combat.** Attacks are ordinary attack profiles, telegraphed by `EntityAttackPhase`. Each creature has ADR-036
    effect procs in `status_effects.json`:
    - crab: bleeding 20 %;
    - piranha slime: bleeding 35 %;
    - jelly: poisoned 30 % and slowed 20 %;
    - eel: stunned on a hit of at least 12 % of max HP, and bleeding 20 %.

    Every roll stays on the `GameplayEntropy` path (ADR-026); nothing new is rolled.
  - **Art.** Vector rigs `shore_crab`, `piranha_slime`, `drift_jelly` and `abyss_eel` (`resources/art/monsters`).
    Names are in the existing `[monster]` locale section.
  - No new packets or opcodes.
- **D15 — Abyss boss (PR 4b).**
  - **Lairs from world data.** `AbyssLairs` (Maps) splits the world into 1024-tile lair cells. A hash of the public seed and
    the cell decides whether the cell holds a lair (`spawning.json → bossLairs.lairChance`, 0.35). The first of 24 hashed
    points sitting in Abyss water, with Abyss 4 tiles away in all eight directions, is the anchor; a cell without such
    open abyss has no lair. Lairs are world data like rivers, so the public seed fixes them; nothing is written into the
    terrain, so the generator version is unchanged. Seed 42 holds lairs in 3 of the 20 sea cells north of its coast.
  - **The boss.** `abyss_leviathan` (archetype 114, `isBoss`) lives in Abyss water only. Its level and stats follow its
    lair's distance from the world origin like any monster's. Three telegraphed patterns, each an ordinary attack profile
    broadcast as `EntityAttackPhase`:

    | Pattern | Kind | Telegraph |
    | :--- | :--- | :--- |
    | Tidal Crush | circle spell, 72 units | a 1.2 s wind-up, then a delayed detonation |
    | Abyssal Lunge | leap, 46-unit landing | the locked landing point |
    | Tail Sweep | melee arc, 240°, 58 units | the swept arc |

    It stuns on a hit of at least 15 % of max HP and makes wounds bleed (level II), as ADR-036 procs.
  - **Rising and respawning.** `BossLairSystem` (Creatures, PostUpdate) raises the boss when a player comes within
    `activationRadius` (40 tiles) of its lair. After its death the lair waits `respawnTicks` (30 minutes). It is not a
    spawner monster: it takes no density-cell slot, and the loader rejects a spawn table that lists a boss.
  - **Never leaving a fight.** A boss nobody is near for `idleDespawnTicks` (2 minutes) sinks back and rises again, whole
    and without the respawn wait, when a player returns. It never sinks while a player in its chunk is fighting it: a
    target or a wound within `combatGraceTicks` (20 s) holds it.
  - **Loot.** Its `leviathan` table is a `"boss": true` table: every kill drops gold, and the boss gold, diamond and
    extra-roll multipliers of `economy.json` apply. Boss tables, like elite ones, are left out of the per-hour farming
    bands.
  - **Sight in deep water (fixes D14).** The AI's aggro and attack line of sight used the terrain raycast, which treats
    deep water as solid, so creatures in Deep or Abyss water never noticed anyone. A water creature now looks along the
    nav grids instead: only what blocks both it and a walker (cliffs, props, structures) hides a target.
  - All combat rolls stay on `GameplayEntropy` (ADR-026); the boss adds none of its own.

## Alternatives Considered

- **A new depth byte in `MapTile`.** Rejected: it would change the chunk-delta tile format, and tiles stored by
  older servers would load with depth 0. Water tiles already carried sub-sea-level elevations, so storing depth
  there changes no format.
- **More `BiomeType` values per tier.** Rejected: every biome table (props, art, spawning, minimap, autotiling)
  would need new rows. Two biomes plus a depth field drive both look and behaviour.
- **Sea depth from smoothed terrain noise.** Rejected: low octaves still rise and fall. Only a distance-to-coast
  field is monotone away from the shore.
- **An exact Euclidean distance transform for the coast.** Rejected: a chamfer pass over 16-tile cells costs
  0.2 ms per region and is smooth after bilinear interpolation.
- **Persisting oxygen.** Rejected (D11).

## Consequences

- Positive:
  - Narrow streams can never be black.
  - Rivers visibly widen downstream and end somewhere.
  - The sea reads as a shelf falling to the abyss.
  - Gameplay gets a single depth tier to key movement, oxygen, spawning and the underwater view on.
- Trade-offs:
  - Each cached hydrology region holds a 256 KiB depth byte per tile in place of a 32 KiB deep-water bit mask, so
    about +224 KiB per region and up to about 28 MiB more across the 128 cached regions.
  - The sea shelf has a uniform width everywhere.
  - Small islands rising out of open sea drop straight into deep water.
- Every world regenerates (v19): stored natural prop states and village layouts reset.
