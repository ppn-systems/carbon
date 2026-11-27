# ADR-027: Organic Village Layout Along Streets and Generated Building Footprints

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-025](ADR-025-villages-from-real-structures-and-trader-npcs.md) D2 (building templates), D3 (placement)
  and, from revision 3, D9 (roof cutaway)
- **Revision 2** (owner feedback: no roads between villages, bigger and wider villages, each building type distinct, built
  only from player-buildable pieces, "beautiful, done properly"): design rules below, D7–D12; D3–D6 amended
- **Revision 3** (the follow-ups from the revision 2 review: roofs hid every interior, streets were plain dirt, empty
  lanes ran on to a desert beach): D13–D16
- **Scope**: `engine/Carbon.Maps` (Landmarks, Settlements, Roads), `resources/config/village_buildings.json`,
  `desktop/Carbon.Desktop.Rendering` (village paths, roofs)
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

1. **D1 — Generated footprints.** `VillageBuildingGenerator.Generate(role, seed, structures)` builds a `VillageBuildingTemplate`:
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

## Design Rules (revision 2)

Derived from how RimWorld colonies and faction bases read top-down, and from small real villages. The goal is a village
a player could have built with the same tools, laid out with intent.

- **Pieces.** Every tile is a structure a player can place: walls, doors, floors, pillars and furniture from
  `structures.json`, each with a placeable kit, at its catalog footprint and rotation, with doors set between walls.
  No village-only props stand in for buildings; the well and the square stay as the village centre.
- **Rooms with a purpose.** A bedroom is at least 3 tiles wide (about 3×4 inside, RimWorld's "decent" bedroom); a
  kitchen or pantry may be 2 wide. Each room has its own door. Furniture stands against walls, leaving the middle and
  every doorway clear; nothing may cut a room off.
- **Material contrast.** Walls, floors and outdoor paving differ by role: the smithy is stone (fireproof walls and a
  stone floor round the forge), the storehouse has a stone floor for heavy goods, homes and the store use the style's
  wood or stone; yards are stone paving; dirt paths are lighter and narrower than streets.
- **Each type reads differently.**
  - Smithy: a stone hall with an open-sided work yard in front, two roof pillars at its outer corners, the anvil and
    workbench out in the yard and the smith beside the anvil.
  - Store: a paved frontage the width of the shop; the counter just inside, the trader behind it, shelves on the walls.
  - Storehouse: one big hall of shelves and chests with aisles.
  - Homes: bedrooms, a kitchen with the stove and table, storage; a kitchen garden beside many of them.
- **Spacing.** Two to three tiles of open ground between buildings (yards and alleys), a one-tile ring of dry level
  ground round each, and nothing in front of a door.
- **A clear centre that is not radial.** The well square anchors the main street, but buildings follow the streets
  and their own frontage, not a ring; side lanes branch well clear of the square, so there is no crossroads at the well.
- **No roads.** Villages are not on the road network; their streets are their own lanes inside the village.

### Review of revision 1 (screenshots of every biome, gameplay and macro zoom)

- Cramped: 16-tile villages squeezed 3–5 buildings between the well and a lake or river.
- Monotonous: every building was a wooden box of the same material; smithy, store and homes differed only inside.
- Roads ran straight through villages and dictated the main street.
- Streets were as wide as the inter-village roads; door paths were long diagonal scratches.
- Paths met at the well in a crossroads; the square was small for the village.
- Roofs hide interiors (ADR-025 D9), so only footprint, materials, yards and gardens can make buildings read apart from
  above; revision 2 relies on those.

## Decision (revision 2)

7. **D7 — No roads to villages.** `RoadNetwork.IsConnected` excludes the four village types; the planner no longer
   reads roads to orient the main street (D3), which takes the layout hash's heading, with a third arm on some sites.
8. **D8 — Bigger, drier sites.** Village radius 16 → 26 tiles; villages sit 8–18 tiles from water (was 3–11), so the
   wider footprint is mostly dry. Six to ten homes are attempted, buildings keep 2–3 tiles apart, three or four lanes.
9. **D9 — Building types.** A new append-only role, `VillageBuildingRole.Storehouse = 4`. Templates gain two cells:
   `y` (open-air paving, the smithy's yard and the store's frontage) and `P` (a roof pillar). Materials by role as in
   the design rules (`VillageBlueprint.CellType`).
10. **D10 — Gardens.** A kitchen garden (`LandmarkField`) beside a home's side or back, never in front of a door, on
    dry ground off streets; later buildings and door paths avoid it.
11. **D11 — Player-buildable only.** Every structure type a village emits has a placeable kit
    (`VillageStructureKitTests`), and `Plan_EveryVillageStructure_IsAPlayerBuildableCatalogStructure` checks cost,
    footprint and door placement against the catalog.
12. **D12 — Generator version 20** (on top of the water rework's 19); golden fingerprints re-pinned.

## Decision (revision 3)

13. **D13 — Streets lead somewhere.** After the buildings are placed, every street or lane that no door path and no
    other kept street joins is dropped, and every street ends one tile past its last junction, repeated until stable
    (`VillagePlanner.PruneStreets`). Lanes now branch from a point on the bent arm itself, not from the arm's straight
    heading, so they join it exactly. This removes the empty lanes that fanned out to the beach in desert villages.
14. **D14 — Paved with a buildable floor.** The square (radius 3.3 tiles), every street and every door path are paved
    with the village's yard masonry, `VillageBlueprint.PavingType`: stone floor, or plank deck in the swamp. Both are
    player-buildable floors with a kit. The planner lists the tiles (`LandmarkLayout.PavedTiles`): dry level ground
    outside plots and gardens, along each centre line (main street within 1 tile, lanes and door paths within 0.72, so a
    diagonal paves a 4-connected staircase), plus each building's front step. Where paving meets paving or an apron only
    at a corner, the tile beside the corner is paved too. The materializer places them as village-owned structures, like
    the buildings (ADR-025 D4–D7), so they replicate, persist and are protected the same way. The client no longer
    paints dirt paths or a dirt square for villages.
15. **D15 — RimWorld-style roofs (amends ADR-025 D9).** Rooms whose outlines touch (a shared wall or door) form one
    building. The whole roof of the building the local player stands in is cut away, not only the room they stand in.
    Roofs of other buildings fade with the player's distance from their walls, from 0.3 opacity right outside to opaque
    at 3.5 tiles (`RoofRenderer.FadeFor`). Drawing and `RoofRenderer.RoofAlphaAt` use the same function. Client-only.
16. **D16 — Generator version 21.** The golden village fingerprint now pins the paved tiles too.

## Alternatives Considered

- **More authored templates.** Even a dozen templates repeat across a world, and "every house is different" would need
  a pick-without-replacement rule per village. Generation gives variety everywhere for less data.
- **Templates in JSON with generated variations (mirroring, stretching).** Mirroring alone gives two variants of each
  shape; stretching breaks furniture anchors. Generating rooms and furnishing them by rule is simpler.
- **A grid of plots (blocks between orthogonal streets).** It reads as a town plan, not a RimWorld-style settlement,
  and fits badly on sites cut by rivers.
- **Keep the ring but jitter the facing.** It would pass an alignment metric without changing what players see.
- **Draw streets as client-side paving art (revision 3).** It would look paved but could not be built, mined or
  protected like everything else in a village. The design rules ask for player-buildable pieces only.
- **A roof toggle key instead of a fade (revision 3).** RimWorld shows roofs as an overlay toggle. A fade needs no
  input and keeps buildings readable at a distance; a toggle can come later.

## Consequences

- Houses differ in size, shape, room count, door position and furniture; buildings line the streets and face them.
- Measured on seeds 42, 1, 2 and 1337 (17×17 sites): mean door-to-well alignment 0.50–0.56 (ring: 0.83–0.84);
  94–97 % of villages get a stall, a smithy, a store and at least two houses (the test floor stays 90 %).
- Planning a village takes about 15 ms on one core (the ring planner took 2–4 ms). It runs once per site per process
  and is cached; the road raster keeps it there.
- `LandmarkLayout` gains `Paths`. Old clients (version 17) refuse to connect, as for any generator change.
- Revision 2, measured on the same seeds: every village is complete (a stall, a smithy, a store, a storehouse and at
  least three houses; 42–57 villages per seed), about 4.5–4.7 houses and 2–2.9 gardens per village. Villages are no
  longer on the road network, and existing worlds rebuild their villages through the ADR-025 D7 migration
  (generator version 20).
- Revision 3, measured on the same seeds: about 170–180 paved tiles and 9 street segments per village. Generating a
  village site takes the same time as before within noise (43–94 ms per site, including terrain, on both). Each paved
  tile is one more structure record in its chunk delta. Existing worlds re-pave through the ADR-025 D7 migration
  (generator version 21).
