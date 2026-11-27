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

State before this ADR (generator version 16; 17 only added starter forage):

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

### Generation (PR 1, generator version 18)

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
- **D8 — Versioning.** `GeneratorVersion` becomes 18 (17 was taken by the starter forage that landed meanwhile). Stored chunk deltas migrate through the existing
  `ChunkDelta.MigrateTo` path: tile edits and player structures are kept, natural prop states and faction villages
  are dropped and regenerated.
- **D9 — Cost.**
  - No per-tile allocation; a cached chunk allocates 0 B (regression test).
  - River stretches are stamped in one pass per cell-to-cell curve, with each row clipped to the curve's reach.
    Before, there were six overlapping boxes per stretch.
  - Stretches that cannot reach the region are skipped before their spline is evaluated.
  - The hydrology region keeps its ~30 ms despite the wider rivers and the two extra fields.

### Swimming, diving and oxygen (PR 2, server)

- **D10 — Movement state from the tier.** The tier under a player gives Walking, Wading, Swimming or Diving.
  Each state has a speed multiplier from server config, applied in place of the wading ramp. Deep and Abyss stop
  being solid for players, but stay impassable for land creatures and in land path cost.
- **D11 — Oxygen is server-authoritative.**
  - It drains while Diving and refills at the surface or on land. At 0, drowning damage pulses through the normal
    damage and death path.
  - It hooks into the ADR-029 status-effect system if that has landed; otherwise it sits behind a seam that one
    call site can replace.
  - Oxygen is **not persisted**. A relog takes longer than surfacing, so persisting it would buy no fairness, and
    it would spend the reserved migration id 0015 for nothing.
- **D12 — Wire.** Oxygen goes to its owner only, in a new S2C packet in the free band 0x4080–0x4087, sent on
  change. No existing packet member is renumbered.

### Client (PR 3)

- **D13** An underwater post-filter scales with the tier: darker, a blue-green tint, a fog or vignette that
  narrows the view, and faint caustics or bubbles. An oxygen bar sits beside the vitals (placed through the HUD
  layout code) and shows only while diving or not full. Swim and dive poses come from vector art (`.cvg.json`).
  All strings live in a new `[water]` section of `en.ini` and `vi.ini`.

### Creatures (PR 4)

- **D14**
  - Three to four water creatures per tier band (shore crab; shallow piranha-slime; medium jelly; deep eel),
    spawned only in their tier at the density rules of `spawning.json`. Their AI paths through water tiers only.
  - One abyss boss with a rare, fixed lair anchor derived from world data. It has telegraphed attack patterns,
    level-scaled loot through the economy tables, and a respawn timer. It never despawns while a player fights in
    its chunk.
  - All combat rolls use `GameplayEntropy` (ADR-026).

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
- Every world regenerates (v18): stored natural prop states and village layouts reset.
