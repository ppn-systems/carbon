# ADR-027: Organic Village Layout Along Streets and Generated Building Footprints

- **Date**: 2026-11-27
- **Status**: Proposed
- **Amends**: [ADR-025](ADR-025-villages-from-real-structures-and-trader-npcs.md) D2 (building templates) and D3 (placement)
- **Scope**: `engine/Carbon.Maps` (Landmarks, Settlements, Roads), `resources/config/village_buildings.json`,
  `desktop/Carbon.Desktop.Rendering` (village paths)
- **Design doc**: [`docs/villages-and-traders.md`](../villages-and-traders.md) §2

## Context

- ADR-025 D2 authored six fixed buildings in `village_buildings.json`. Every house of every village was one of three
  rectangles with one room, so most villages showed the same box several times.
- ADR-025 D3 placed buildings on rings 6.5–12.5 tiles around the well and turned every entrance to the square. On
  seeds 42, 1, 2 and 1337 the mean alignment between a door's outward direction and the direction to the well was
  0.83–0.84 (1.0 means every door looks at the well). The result reads as a ring, not as a village.
- The village is world-generation data: the server materializes it (ADR-025 D4), and every client builds the same
  `LandmarkLayout` to draw the square and its paths. It must stay a pure function of the site, so it keeps using the
  layout hash of the site, not `GameplayEntropy` (ADR-026 covers gameplay rolls, not world layout).

## Decision

1. **D1 — Generated footprints.** `VillageBuildingGenerator.Generate(role, seed)` builds a `VillageBuildingTemplate`:
   - a rectangle (houses 5–8 × 5–7 tiles, shops 6–8 × 6–7) or an L-shape (a back corner cut away);
   - one to three rooms, split by straight interior walls, each with an interior door. A split never walls off a door
     and always leaves at least two tiles of floor on each side; a shop keeps a large front room for its counter;
   - one entrance in the south wall (the authored orientation of ADR-025 is kept);
   - furniture by role: a home gets beds, a stove, a table and storage; a smithy its forge anvil (the smith stands
     beside it), a stove, a workbench and storage; a store shelves, a counter of tables (the trader stands behind it)
     and chests. Back rooms become bedrooms, storerooms or workrooms. Every piece goes on a free floor tile away from
     doors, and a piece is only kept if every free floor tile and the NPC spot can still be reached from the entrance;
   - the stall stays an open counter with a crate and the merchant.
2. **D2 — Plots carry their footprint.** `VillagePlot.Template` replaces `TemplateIndex`. `VillageBlueprint` reads the
   template from the plot. `village_buildings.json` keeps only the style palettes (format version 2).
3. **D3 — Streets.** `VillagePlanner` (now `engine/Carbon.Maps/Landmarks/VillagePlanner.cs`) lays a main street through
   the square, lined up with the roads that reach the village (found from the road raster on a circle inside the site),
   with a bend in each arm, and one or two side lanes. A street stops where the ground stops being dry and level.
   `LandmarkLayout.Paths` holds the streets and the door paths; `PointOfInterestRenderer` draws them.
4. **D4 — Placement.** The smithy, the store, the stall and four to seven houses are placed in that order:
   - first beside a street, with the door turned to the street and a varied setback;
   - when no frontage is left, anywhere on the site with the door facing the nearest street at most 8 tiles away;
   - each placement adds a short path from the front tile to the street;
   - a plot stays inside `radius − 0.5`, off the square, off streets, paths and roads, one or two tiles from every other
     plot, on dry ground at the site level (with a one-tile ring), and its path crosses open ground only;
   - no two buildings of a village share a footprint: a repeat is rolled again;
   - a footprint with no valid spot is retried compact, and after three houses that do not fit the site counts as full;
   - if fewer than two houses fit, the planner lays the streets out another way (up to four layouts) and keeps the one
     with the most buildings.
5. **D5 — Road raster.** `RoadNetwork.Rasterize` marks the road and bridge tiles of a rectangle in one pass, with the
   same rule as `IsRoadOrBridgeAtTile`. `PointOfInterestResolver.Roads` replaces the per-tile `RoadQuery`; a per-tile
   query costs about 0.1 ms, and the planner needs the whole site.
6. **D6 — Generator version 18.** `ProceduralWorldGenerator.GeneratorVersion` goes from 17 to 18 and the golden
   fingerprint is updated. ADR-025 D7 migration drops the old village structures; the new village is built in their
   place. The wire format does not change.

## Alternatives Considered

- **More authored templates.** Even a dozen templates repeat across a world, and "every house is different" would need
  a pick-without-replacement rule per village. Generation gives variety everywhere for less data.
- **Templates in JSON with generated variations (mirroring, stretching).** Mirroring alone gives two variants of each
  shape; stretching breaks furniture anchors. Generating rooms and furnishing them by rule is simpler.
- **A grid of plots (blocks between orthogonal streets).** It reads as a town plan, not a RimWorld-style settlement,
  and fits badly on sites cut by rivers.
- **Keep the ring but jitter the facing.** It would pass an alignment metric without changing what players see.

## Consequences

- Houses differ in size, shape, room count, door position and furniture; buildings line the streets and face them.
- Measured on seeds 42, 1, 2 and 1337 (17×17 sites): mean door-to-well alignment 0.52–0.56 (ring: 0.83–0.84);
  94–97 % of villages get a stall, a smithy, a store and at least two houses (the test floor stays 90 %).
- Planning a village takes about 15 ms on one core (the ring planner took 2–4 ms). It runs once per site per process
  and is cached; the road raster keeps it there.
- `LandmarkLayout` gains `Paths`. Old clients (version 17) refuse to connect, as for any generator change.
