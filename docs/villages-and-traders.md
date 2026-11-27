# Villages, traders and the village blacksmith

The owner's request: stop hand-drawing village buildings and build real villages with the RimWorld-style building system; add NPCs that buy and sell, with random stock that the server restocks on its own schedule; and give every village a blacksmith. Decision record: [ADR-025](adr/ADR-025-villages-from-real-structures-and-trader-npcs.md).

The work ships as three pull requests:

1. **Real-structure villages** (this document's §2): templates, planner, materialization, protection, art and roofs.
2. **Traders** (§3): stock tables, restock epochs, persistence, packets and the desktop trade window.
3. **Blacksmith** (§4): a trading profile for weapons, armor and tools, repair, and a hook into the enhancement window.

## 1. Evaluation (before)

| Area | What existed | Problem |
| :--- | :--- | :--- |
| Village layout | `VillagePlanner` placed a well, 7 `LandmarkPiece.House` entries on a ring, fields and a barrel. | Houses were paintings: `PointOfInterestRenderer.DrawHouse` drew them, `LandmarkPiece.GetColliders` faked five wall boxes, and navigation treated each house as one solid block. They had no real doors, furniture or interiors, and nothing in them could be interacted with. |
| Building system | Walls, doors, floors, pillars, stations and furniture on `BuildingGrid`, with ownership records, chunk-delta persistence and replication (`docs/building.md`, ADR-011). | Unused by generated content. Any structure without a record counts as public, so it can be deconstructed. |
| Multi-tile art | Beds (1×2) and workbenches (2×1) occupy several grid tiles. | The terrain baker stamped the full 1-tile art on every footprint tile, so a bed showed as two beds. |
| Roofs | `BuildingGrid.IsRoofedAt` knows which tiles are roofed rooms. | Nothing drew a roof. Enclosed rooms only got a faint tint. |
| Village NPCs | `SettlementNpcSpawner` placed a guard and a blacksmith next to the centre. | Spawning was per chunk. A village is listed by every chunk it reaches (up to 4), so it got up to 4 guards and 4 blacksmiths. |
| Vendors | `EconomyService.RunVendorOperation` handled buy, sell and repair near any friendly NPC. | There was no stock, no scarcity and no restock: every `vendorBuyable` item was always available at a fixed price. No client UI called it. |
| Client landmarks | `WorldSceneRenderer` built its own `ProceduralWorldGenerator` from the seed alone. | It used the default frequency instead of the server's, resolved different points of interest, and drew no village pieces (well, barrels, paths) in multiplayer. |
| Chunk requests | `MapHandlers.HandleChunkRequestAsync` replied with a `MapChunkDelta`. | It copied the structure arrays only. Sites and ownership records never reached clients on a chunk request. |

## 2. Villages from real structures

### 2.1 Data: `resources/config/village_buildings.json`

```jsonc
{
  "styles": {                                            // one palette per VillageStyle
    "temperate": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor", "porch": null },
    "desert":    { "wall": "SandstoneWall", "door": "WoodDoor", "floor": "StoneFloor", "porch": null },
    "snow":      { "wall": "StoneWall", "door": "WoodDoor", "floor": "WoodFloor", "porch": null },
    "swamp":     { "wall": "WoodWall", "door": "WoodDoor", "floor": "PlankDeck", "porch": "PlankDeck" }  // stilted deck
  },
  "buildings": [
    {
      "id": "smithy", "role": "smithy", "weight": 1,      // role: house | store | smithy | stall
      "grid": [                                           // authored with the entrance on the bottom (south) row
        "WWWWWWW",                                        // W wall  D door  . floor  p porch  ' ' bare ground
        "W.....W",
        "W.....W",
        "W.....W",
        "W.....W",
        "WWDWWWW",
        "ppppppp"
      ],
      "furniture": [ { "type": "ForgeAnvil", "at": [3, 2] }, { "type": "Workbench", "at": [4, 1] } ],
      "npc": [3, 3]                                       // where the building's NPC stands
    }
  ]
}
```

- **Templates:** `cottage`, `family_house`, `workshop_house`, `general_store`, `smithy` and `market_stall`.
  - A template may be limited to some styles (`"styles": [...]`).
  - Furniture can be rotated, and its anchor is the top-left cell of its footprint.
- **Validation:** `VillageBuildingCatalog.Parse` rejects:
  - unknown cells, roles, styles or structure types;
  - furniture on a wall or door;
  - an NPC spot inside a wall;
  - a palette entry of the wrong category (for example a floor type used as a wall).
- **World-generation data:** the file is embedded into `Carbon.Maps`, like `structures.json`, so the server and every client plan the same villages. Editing it changes the generated world: bump `ProceduralWorldGenerator.GeneratorVersion` and update `VillageGenerationTests.GoldenVillagePlotFingerprint` in the same change.

### 2.2 Planner (deterministic)

`VillagePlanner` (`engine/Carbon.Maps/Landmarks/LandmarkPlanners.cs`) keeps the well, the crop fields (temperate) and the barrel. It then places buildings in this order: market stall, smithy, general store, then up to five houses.

- **Choosing a template.** For each building, `VillageBuildingCatalog.Pick` chooses a template for the role and style, weighted and driven by the layout hash.
- **Candidate positions.** Up to 40 candidates lie on rings around the well: 4.5–5.5 tiles out for the stall, 6.5–12.5 tiles for the rest, starting at a golden-angle offset. Each candidate first turns its entrance to face the square. A second pass accepts any facing, so cramped sites still fit their buildings.
- **Accepting a plot.** A candidate becomes a plot when all of these hold:
  - every corner lies within `radius − 0.5`;
  - it stays off the 5×5 well square;
  - it keeps at least one tile from every other plot, so there are alleys between buildings;
  - every tile, plus a one-tile ring around it, is dry flat ground at the site level;
  - no tile is on a road or bridge;
  - the tile in front of the entrance is free.
- **Roads.** The road query is pure. The generator binds `RoadNetwork.IsRoadOrBridgeAtTile` into `PointOfInterestResolver.RoadQuery`. Roads read only site centres, never layouts, so there is no cycle.
- **Measured.** Across seeds 42, 1, 2 and 1337 (17×17 sites each), 92–100 % of villages get a stall, a smithy, a store and at least 2 houses; the rest are squeezed by rivers or cliffs. `VillageGenerationTests.Plan_Villages_GetStallSmithyStoreAndHouses` enforces at least 90 %.

`LandmarkLayout.Plots` holds the result. It is a pure function of the seed and the generator version.

### 2.3 Materialization (server)

`VillageMaterializer` (`engine/Carbon.Maps/Settlements`) expands each plot (`VillageBlueprint.Expand`) into structure tiles:

- palette walls, doors, floors and porches;
- the furniture footprints, each tile remembering the floor it covers.

It places walls, doors and floors first, so wall autotiling sees whole outlines, then the furniture. Each tile goes through `BuildingGrid.PlaceStructure` with a `StructureRecord`: owner `StructureOwners.Village`, anchor offset, rotation and the floor beneath. From there the existing paths take over: chunk delta, SQLite/PostgreSQL persistence (`_dirtyChunks`), `MapChunkDelta` replication and client import.

`SettlementNpcSpawner.EnsureVillageBuilt` runs it once per site, at two points:

- when chunks come into play (`PopulateAround`);
- before any chunk delta is served (`TryCreateChunkDeltaPacket`), so no client ever receives a village chunk without its buildings.

It is idempotent: an occupied or reserved tile is skipped. A restart, or a tile a player already built on, therefore changes nothing. On a generator version bump, `ChunkDelta.MigrateTo` drops faction-owned tiles and keeps player builds, and the new layout is built in their place.

### 2.4 Protection

These rules are server-enforced through `BuildingService`, and the client preview runs the same check:

| Action on a village tile | Result |
| :--- | :--- |
| Deconstruct (`MapStructureDemolish`) | `ProtectedSettlement` → `Forbidden` |
| Tool strike (`ConstructionService.DamageStructure` → `ApplyPlayerDamage`) | `ProtectedSettlement`, no damage |
| Place a blueprint (on a village floor or any village tile) | `ProtectedSettlement`; the ghost reads "Village property" |
| Open or close a door | Allowed for everyone |
| Monster damage (`ApplyDamage` without an attacker) | Not blocked |

- **Reserved owner ids.** Owner ids from `StructureOwners.FactionRangeStart` up are reserved. `BuildPermissionTable.CanModify` refuses them to everyone but the faction. Player owner ids are hashed from character ids and folded out of that range (`ToPlayerOwnerId`).
- **Chests.** Village chests are not lootable. They are furniture with no container; when containers arrive, the faction rule keeps village chests closed by default.

### 2.5 Art and rendering

- **New structure types** (append-only), each with `.cvg.json` art in the existing layered style (ADR-005):
  - `SandstoneWall` (16), `WoodTable` (17), `WoodShelf` (18), `MarketStall` (19, 2×1, striped canopy over produce crates) and `PlankDeck` (20, swamp deck over dark water).
  - They have no placeable kits yet.
- **Multi-tile art.** Structure art takes an optional `size: [w, h]`. Multi-tile art is drawn once at the anchor and rotated with the record by `VectorMeshBaker.StampFootprintShapes`. `wood_bed` (1×2), `workbench` (2×1) and `market_stall` (2×1) use it.
- **Roofs.** `RoofRenderer` finds enclosed rooms from their walls (cached by `BuildingGrid.Revision`) and draws a roof in the style of the walls:

  | Walls | Roof |
  | :--- | :--- |
  | Wood | Red shingles |
  | Wood on plank deck (swamp) | Thatch |
  | Stone (snow) | Slate with snow |
  | Sandstone (desert) | Flat clay roof with a parapet and spouts |
  | Metal | Grey metal |

  - The roof stops on the top of the south wall row, so façades and front doors stay visible. Side and back entrances get a small awning.
  - Stoves get a chimney with smoke. A room with a forge gets an anvil sign; one with three or more shelves gets a goods sign.
  - The room the local player stands in is cut away, and the camera's interior zoom uses the same rooms.
  - Player-built rooms get roofs too.
- **No more decorative houses.** The village house painting is gone: villages emit no `House` pieces, and harbours keep theirs. Paths now run from the square to each building's entrance. The square shrank to the well area so it never paints over the stall.

### 2.6 NPCs

Spawning is keyed by point of interest, so each village gets one set:

- a guard by the well;
- the blacksmith at the smithy's NPC spot, beside the forge anvil, with a short patrol radius so he stays at the forge.

The traders of §3 use the `store` and `stall` NPC spots.

## 3. Traders

| Trader | Where | Stock |
| :--- | :--- | :--- |
| Merchant | Market stall on the square | Regional goods by village style: produce, materials, local crafts |
| General trader | General store | Tools, consumables, building materials, rope and cloth |
| Blacksmith (§4) | Smithy | Weapons, armor and tools |

- **Profiles.** `resources/config/traders.json` is server-only and hot-reloads with the economy. Each profile has a slot count, weighted entries (item code, quantity range, optional styles), a price variance and a restock period with jitter. The loader rejects unknown items, and rejects any variance that could sell an item at or below the vendor's buy-back price.
- **Epochs.** A restock epoch is `floor((unixSeconds + jitter(trader)) / period)`, using server wall-clock time, so stock survives restarts and villages restock at different moments.
- **Stock.** The stock of an epoch is a pure function `(trader stock key, trader key, epoch, profile, style)`, rolled by `TraderStockRoller` with `DeterministicRandom`. The trader stock key derives from the persisted gameplay secret, not the public world seed (ADR-026), so clients cannot preview future stock and a restart does not reshuffle it.
  - `TraderStockRoller` picks distinct weighted entries, filtered by village style.
  - Only the quantity sold in the current epoch is state (`TraderBook`), kept in the `trader_stock` table. The table is created idempotently with the base schema in SQLite and PostgreSQL.
  - The trader key mixes the village centre with the building role (`SettlementNpcSpawner.TraderKeyOf`), so it is stable for a given world.
- **Prices.** A unit price is the list price (`PriceBook.GetListPrice`: base × buy markup, consumables by the buyer's level band), shifted by the slot's rolled offset. It is never at or below the vendor buy-back price.
- **Server.** `TradeService` (`server/Carbon.Application/World/Commerce`) has its own command queue, drained on the world tick, and pushes stock through the outbound tick buffer. The `ItemHandlers.Trade` partial routes the three C2S opcodes.
- **Wire.**
  - C2S: `TraderOpen` `0x4040`, `TraderBuy` `0x4041` (carries the epoch it was priced in), `TraderSell` `0x4042`.
  - S2C: `TraderStock` `0x4048` carries items, prices, remaining quantities, sell prices and seconds until restock.
  - Results come back as `MutationResult`, followed by `TraderStock` and `WalletSync`.
- **Rules.** The player must be within 175 px of a live trader. A buy needs a valid slot, enough remaining stock, gold and bag space, and commits all of them atomically. A stale epoch is refused with `Conflict`. A sell pays the economy sell price (25 % spread, a gold sink).
- **Desktop.** `TradeModalWidget` (nine-slice, ADR-008) shows:
  - the stock rows: icon, name, units left with a gauge, and the price, in red when unaffordable;
  - the bag grid, with what the trader pays per item; items the trader does not buy are dimmed;
  - the wallet and a live restock countdown.

  Controls:
  - `E` near a trader, or clicking one, opens the window.
  - Clicking a row buys 1; Shift-click buys 5.
  - Clicking a bag item sells 1; Shift-click sells the stack.
  - Walking away, opening another window or `Esc` closes it.

  All strings are in `resources/localization/{en,vi}.ini` under `[trade]` (ADR-023).

  The code is split to stay within the client size caps (audit round 2):
  - `TraderSession` (networking) holds the pushed stock and sends the requests. It is reached through `IGameplayController.Trading`.
  - `TradeInteraction` (scene) decides when to talk, click, open and close.
  - `TradeModalWidget` (HUD) is stateless; it draws the window and hit-tests it.
- **Screenshots.** `tools/screenshots/capture.sh <out> '^trade_'` spawns the player in front of the market stall and opens the trader (`CARBON_OPEN_TRADER=1`).

## 4. Blacksmith

The village blacksmith (at the smithy's forge) and the sanctuary blacksmith (beside the world spawn) are both traders. Their `MasterBlacksmith` spec has `NpcCapabilities.ForgeArtisan | NpcCapabilities.Trader`.

- **Stock.** He trades from the `blacksmith` profile: weapons, armor, tools and smithing materials, with the same seeded, restocking, limited stock as the other traders (§3).
  - The village smith's key is `TraderKeyOf(village, Smithy)` and his style is the village's.
  - The sanctuary smith's key comes from the spawn tile, and his style is temperate.
- **Repair.** The `TraderStock` push carries `RepairSlots` and `RepairCosts`, the server's quotes (`PriceBook.GetRepairCost`) for each worn item in the bag.
  - The window's **Repair** button switches the bag to repair mode. Worn gear shows the server's repair quote; everything else is dimmed. The client never prices a repair.
  - A click sends the existing `RepairInventoryItem`. The server already accepts it next to a forge-artisan NPC (`InventoryService.IsNearForgeStation`).
  - On success the window re-opens the trader to refresh the quotes. A `Conflict` (not enough gold) shows a toast.
- **Enhancement.** The enhancement work owns this (`ItemEnhancementPolicy` and the forge window, ADR-022/024). The window's **Enhance…** button only calls the scene's existing forge entry point, `WorldGameplayScene.OpenForgeWindow`, which closes the trade window and opens the forge. Nothing in the enhancement window is duplicated. `U` still opens the forge directly.
- **Input.** `E` or a click on the blacksmith opens his trade window; the click no longer also opens the forge. His nameplate reads `[E: TRADE  U: FORGE]`.

## 5. Tests

| Test | Checks |
| :--- | :--- |
| `VillageGenerationTests` (Maps) | Catalog parsing and rejection; same seed gives the same plots and the same structures; golden plot fingerprint; completeness across 4 seeds; plots inside the footprint and apart; every interior floor is an enclosed room, reachable from its door, with reachable NPC spots; idempotent and fully village-owned materialization; chunk-delta round trip; migration drops village tiles but keeps player builds; players cannot deconstruct, damage or build. |
| `LandmarkCollisionTests` (Maps) | Real village house: walls push out, a closed door blocks and an open one lets you in, the interior is roofed, and the ground in front is free. |
| `SettlementNpcSpawnerTests` (Host) | A village is built once with one guard; the blacksmith stands next to the forge; deconstruct and damage are refused. |
| `VectorArtAndArchitectureTests` (Desktop) | Every structure type has art (doors with open shapes). |
| `TraderTests` (Economy) | Shipped profiles load; invalid documents are rejected (unknown item, variance, quantities, too few entries, jitter, style); stock is deterministic per epoch and changes at restock; slots are distinct and style-correct, within quantity and variance ranges; prices stay within variance and above the buy-back price at levels 1/30/80; the epoch clock and per-trader jitter; limited quantity sells out and the next epoch restocks; failures change nothing (funds, full bag, slot, quantity, stale epoch, unknown trader); sales drain and import. |
| `TraderWorldTests` (Host) | Through the real world: open a merchant and buy (wallet and stock change); more than is left, a stale epoch, a bad slot and an unknown trader are refused; selling pays less than the buy price; a far player is out of range; sales persist across a server restart on SQLite; the next epoch is fully restocked. The smithy's blacksmith sells only `blacksmith` profile items, quotes a worn sword at exactly `PriceBook.GetRepairCost`, repairs it for that gold, and quotes nothing afterwards. The sanctuary blacksmith trades too. The merchant quotes no repairs. |

Screenshots:
- `tools/screenshots/capture.sh <out> '^village_'` captures one village per style, a macro view and a night view (`CARBON_DEBUG_SPAWN_POI=<PointOfInterestType>`).
- `'^trade_'` captures the merchant, a desert trader and the blacksmith, in sell mode and in repair mode.
  - `CARBON_DEBUG_SPAWN_POI=RiverVillage:Smithy` spawns in front of a named building.
  - `CARBON_DEBUG_WORN_GEAR=1` wears the starting gear to 40 %.
  - `CARBON_OPEN_TRADER=repair` opens the window in repair mode.
