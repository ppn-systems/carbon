# Villages, traders and the village blacksmith

The owner's request: stop hand-drawing village buildings and build real villages with the RimWorld-style building system; add NPCs that buy and sell, with random stock that the server restocks on its own schedule; and give every village a blacksmith. Decision record: [ADR-025](adr/ADR-025-villages-from-real-structures-and-trader-npcs.md).

The work ships as three pull requests:

1. **Real-structure villages** (this document's §2): generated buildings, streets, materialization, protection, art and roofs.
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
  "version": 2,
  "styles": {                                            // one palette per VillageStyle
    "temperate": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor", "porch": null },
    "desert":    { "wall": "SandstoneWall", "door": "WoodDoor", "floor": "StoneFloor", "porch": null },
    "snow":      { "wall": "StoneWall", "door": "WoodDoor", "floor": "WoodFloor", "porch": null },
    "swamp":     { "wall": "WoodWall", "door": "WoodDoor", "floor": "PlankDeck", "porch": "PlankDeck" }  // stilted deck
  }
}
```

- **Validation:** `VillageBuildingCatalog.Parse` rejects unknown styles or structure types, a missing style, and a palette
  entry of the wrong category (for example a floor type used as a wall).
- **World-generation data:** the file is embedded into `Carbon.Maps`, like `structures.json`, so the server and every
  client plan the same villages. Editing it changes the generated world: bump `ProceduralWorldGenerator.GeneratorVersion`
  and update `VillageGenerationTests.GoldenVillagePlotFingerprint` in the same change.

### 2.2 Buildings and layout (deterministic, ADR-027 revision 2)

**Buildings.** `VillageBuildingGenerator.Generate(role, seed)` (`engine/Carbon.Maps/Settlements`) makes every building
from player-buildable pieces only (each has a kit, §2.1):

- a rectangle or an L-shape, with its entrance in the south wall. Houses are 7–11 × 6–9 tiles, smithies 8–10 × 6–7,
  stores 7–9 × 6–8 and storehouses 8–11 × 6–7;
- in front, an apron: one porch row for houses and storehouses (decked in the swamp palette), a two-row paved frontage
  for the store, and a three-row paved work yard with roof pillars at its outer corners for the smithy. The column in
  front of the door is always kept free;
- one to three rooms (a storehouse has one), split by straight interior walls with doors. A split never walls off a
  door and leaves at least two tiles of floor on each side and three on one;
- furniture by role, against walls where it can be, each piece on a free tile away from doors, and only where every
  free tile, the yard and the NPC spot stay reachable from the entrance:

| Role | Inside | Apron |
| :--- | :--- | :--- |
| House | stove, table, shelf or chest; beds when it has one room; back rooms are a bedroom (beds, chest), a storeroom or a workroom | porch |
| Smithy | stove, shelf, two chests (stone walls, masonry floor) | work yard: forge anvil with the smith beside it, workbench, pillars |
| Store | shelves, a counter of two tables with the trader behind it, chest | paved frontage |
| Storehouse | chests and shelves along the walls, free-standing shelves (masonry floor), no NPC | porch |
| Stall | open market stall, crate, merchant behind the counter | — |

Materials follow the village style's palette, except that the smithy is built in stone where the palette walls are
wood, and the smithy, the storehouse and paved aprons use a masonry floor (plank deck in the swamp)
(`VillageBlueprint.CellType`).

**Streets.** `VillagePlanner` (`engine/Carbon.Maps/Landmarks/VillagePlanner.cs`) keeps the well on a square, then lays
a main street through it in a heading taken from the layout hash (two arms, sometimes a third), each with a bend, plus
three or four side lanes of 7–13 tiles that branch from a point on the arm. Villages are not on the road network
(`RoadNetwork.IsConnected`), so no road reaches or crosses them. Streets stop at water and cliffs. Once the buildings
stand, a lane that no door path joins is dropped and every street ends a tile past its last junction (ADR-027 D13).

**Paving.** The square, the streets and the door paths are paved with the village's yard masonry (stone floor, or plank
deck in the swamp), a player-buildable floor (ADR-027 D14). `LandmarkLayout.PavedTiles` lists them: dry level ground
outside plots and gardens along each centre line, plus each front step, joined edge to edge where they would only touch
at a corner. The materializer places them as village-owned structures.

**Placement.** The smithy, the store, the stall, the storehouse and six to ten houses, in that order:

- beside a street first, the door turned to the street, at a varied setback; when no frontage is left, anywhere on the
  site with the door facing the nearest street (at most 9 tiles away);
- a short path joins every front door to its street (`LandmarkLayout.Paths`, drawn by `PointOfInterestRenderer`). It
  crosses open ground and its own apron only, never another building or a garden;
- a plot stays inside the 26-tile site radius, off the square, streets, paths, roads and gardens, two or three tiles from
  other plots, on dry level ground with a one-tile ring;
- no two buildings of a village share a footprint;
- beside a home, a kitchen garden (`LandmarkLayout.Fields`) is added at its side or back, never in front of a door;
- a footprint with no room is retried compact; when a layout gets fewer than three houses, the streets are laid out
  another way (up to four layouts) and the best layout is kept.

**Measured.** Across seeds 42, 1, 2 and 1337 (17×17 sites each), every village gets a stall, a smithy, a store, a
storehouse and at least three houses (`Plan_Villages_GetStallSmithyStoreAndHouses`), with about 4.5–4.7 houses and
2–2.9 gardens per village. Door-to-well alignment stays below the ring's 0.83 (`Plan_Layout_IsNotARingFacingTheWell`
enforces below 0.7). Planning takes about 15 ms per village, once per process.

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
  - Each has a placeable kit (`sandstone_wall`, `wood_table_kit`, `wood_shelf_kit`, `market_stall_kit`, `plank_deck`), crafted at the workbench, like the chest, bed, workbench and stove kits; `VillageStructureKitTests` checks that every structure a village can emit has one.
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
  - Rooms that share a wall form one building. The whole roof of the building the local player stands in is cut away,
    and the roofs of nearby buildings fade with distance: 0.3 opacity right outside, opaque from 3.5 tiles
    (ADR-027 D15). The camera's interior zoom uses the same rooms.
  - Player-built rooms get roofs too.
- **No more decorative houses.** The village house painting is gone: villages emit no `House` pieces, and harbours keep theirs. The square and paths are real paving (§2.2), so the client paints no dirt paths or square for villages.

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
- **Server.** `TradeService` (`server/Carbon.Application/World/Commerce`) has its own command queue, drained on the world tick, and pushes stock through the outbound tick buffer. The `ItemHandlers.Trade` partial routes the C2S opcodes.
- **Wire.**
  - C2S: `TraderOpen` `0x4040`; `TraderSellBatch` `0x4052` (a whole basket: bag slot, item and units per line, and the total the window showed); `TraderBuyBatch` `0x4053` (a whole cart, priced in an epoch).
  - S2C: `TraderStock` `0x4048` carries items, prices, remaining quantities, sell prices and seconds until restock. `TraderBatchResult` `0x4054` answers a batch with a `TradeBatchError` code and the failing line, after the changed slots, `WalletSync` and a fresh `TraderStock`.
  - The single-item `TraderBuy` `0x4041` and `TraderSell` `0x4042` (answered with `MutationResult`) still work for older clients; the desktop client no longer sends them (ADR-025 D19).
- **Rules.** The player must be within 175 px of a live trader. A buy needs a valid slot, enough remaining stock, gold and bag space, and commits all of them atomically. A stale epoch is refused with `Conflict`. A sell pays the economy sell price (25 % spread, a gold sink). A batch is all or nothing: every line is checked and every price recomputed before anything changes, and a total other than the confirmed one is refused as `PricesChanged` (ADR-025 D20).
- **Desktop.** `TradeModalWidget` (nine-slice, ADR-008) shows:
  - the stock rows: icon, name, units left with a gauge, and the price, in red when unaffordable;
  - the bag grid, with what the trader pays per item; items the trader does not buy are dimmed;
  - the wallet and a live restock countdown;
  - the **Cart** and the **To sell** basket (ADR-025 D18): one line per item with −/+, the subtotal, and the running total.

  Controls. Nothing is bought or sold until **Buy** or **Sell** is pressed:
  - `E` near a trader, or clicking one, opens the window.
  - Clicking a stock row adds 1 to the cart; Shift-click adds 5.
  - Clicking a bag item moves 1 to the basket; Shift-click or dragging it onto To sell moves the whole stack. Equipment cannot be added.
  - −/+ change a line (Shift: by 10); **Clear** returns a side.
  - When prices change (a restock, or the server refused the total) or the basket holds enhanced gear, the button reads **Confirm**: the first press accepts, the next one sends.
  - Walking away, opening another window or `Esc` closes the window, and everything staged returns (it never left the bag).

  All strings are in `resources/localization/{en,vi}.ini` under `[trade]` and `[trade_basket]` (ADR-023).

  The code is split to stay within the client size caps (audit round 2):
  - `TraderSession` (networking) holds the pushed stock, sends the committed batches and queues their answers. It is reached through `IGameplayController.Trading`.
  - `TradeBasket` (abstractions) holds the staged lines, re-prices them and decides when a press re-confirms.
  - `TradeInteraction` (scene) decides when to talk, click, open and close; `TradeWindowController` maps clicks and drags to the basket.
  - `TradeModalWidget` (HUD) is stateless; it draws the window and hit-tests it.
- **Screenshots.** `tools/screenshots/capture.sh <out> '^trade_'` spawns the player in front of the market stall and opens the trader (`CARBON_OPEN_TRADER=1`). `trade_basket` also stages a sample basket and cart (`CARBON_TRADE_PREVIEW=basket`, visual only).

## 4. Blacksmith

The village blacksmith (at the smithy's forge) and the sanctuary blacksmith (beside the world spawn) are both traders. Their `MasterBlacksmith` spec has `NpcCapabilities.ForgeArtisan | NpcCapabilities.Trader`.

- **Stock.** He trades from the `blacksmith` profile: weapons, armor, tools and smithing materials, with the same seeded, restocking, limited stock as the other traders (§3).
  - The village smith's key is `TraderKeyOf(village, Smithy)` and his style is the village's.
  - The sanctuary smith's key comes from the spawn tile, and his style is temperate.
- **Repair.** Replaced by [ADR-028](adr/ADR-028-material-repair-per-item.md): the blacksmith repairs one chosen item for materials and a gold fee (`RepairItemRequest`, `0x4050`). `TraderStock.RepairSlots` and `RepairCosts` are always empty.
- **Enhancement.** The enhancement work owns this (`ItemEnhancementPolicy` and the forge window, ADR-022/024). The window's **Enhance…** button only calls the scene's existing forge entry point, `WorldGameplayScene.OpenForgeWindow`, which closes the trade window and opens the forge. Nothing in the enhancement window is duplicated. `U` still opens the forge directly.
- **Input.** `E` (when he is the nearest thing in reach, see the interact prompt) or a click on the blacksmith opens his trade window; the click no longer also opens the forge. His nameplate reads `[U: FORGE]`.

## 5. Tests

| Test | Checks |
| :--- | :--- |
| `VillageGenerationTests` (Maps) | Catalog parsing and rejection; same seed gives the same plots and the same structures; golden plot fingerprint; completeness across 4 seeds; plots inside the footprint and at least two tiles apart; no road or bridge tile in a village; every emitted structure is a catalog structure with a cost and its real footprint; gardens beside homes, off buildings, streets and paths; every interior floor is an enclosed room, reachable from its door, with reachable NPC spots; idempotent and fully village-owned materialization; chunk-delta round trip; migration drops village tiles but keeps player builds; players cannot deconstruct, damage or build. |
| `LandmarkCollisionTests` (Maps) | Real village house: walls push out, a closed door blocks and an open one lets you in, the interior is roofed, and the ground in front is free. |
| `SettlementNpcSpawnerTests` (Host) | A village is built once with one guard; the blacksmith stands next to the forge; deconstruct and damage are refused. |
| `VillageStructureKitTests` (Maps) | Every structure type any village building can emit has a placeable kit item with a build cost. |
| `VillageStreetTests` (Maps) | Every street leads to a building and stops a tile past its last junction; paving covers the square, every front step and every dry path tile outside plots and gardens, and forms one network from the well to every door; materialized paving is a village-owned buildable floor of the village's style. |
| `RoofRendererTests` (Desktop) | Inside one room, the roof of the other room of the same building is cut away too; the neighbour stays roofed; from the doorstep the roof is faded; walking away it fades in and is opaque far away. |
| `VectorArtAndArchitectureTests` (Desktop) | Every structure type has art (doors with open shapes). |
| `TraderTests` (Economy) | Shipped profiles load; invalid documents are rejected (unknown item, variance, quantities, too few entries, jitter, style); stock is deterministic per epoch and changes at restock; slots are distinct and style-correct, within quantity and variance ranges; prices stay within variance and above the buy-back price at levels 1/30/80; the epoch clock and per-trader jitter; limited quantity sells out and the next epoch restocks; failures change nothing (funds, full bag, slot, quantity, stale epoch, unknown trader); sales drain and import. |
| `TraderWorldTests` (Host) | Through the real world: open a merchant and buy (wallet and stock change); more than is left, a stale epoch, a bad slot and an unknown trader are refused; selling pays less than the buy price; a far player is out of range; sales persist across a server restart on SQLite; the next epoch is fully restocked. The smithy's blacksmith sells only `blacksmith` profile items, sends no gold-only repair quotes, and repairs a worn sword for its `repair.json` materials and fee (ADR-028). The sanctuary blacksmith trades too. The merchant quotes no repairs. |

Screenshots:
- `tools/screenshots/capture.sh <out> '^village_'` captures one village per style, a macro view and a night view (`CARBON_DEBUG_SPAWN_POI=<PointOfInterestType>`).
- `'^trade_'` captures the merchant, a desert trader and the blacksmith. `'^repair_'` captures the repair window (ADR-028) at the blacksmith, its animation, and at a player station.
  - `CARBON_DEBUG_SPAWN_POI=RiverVillage:Smithy` spawns in front of a named building; `RiverVillage:House:inside` one step through its door (`village_roof_near`, `village_roof_in`, `village_forge_in`).
  - `CARBON_DEBUG_WORN_GEAR=1` wears the starting gear to 40 %.
  - `CARBON_OPEN_REPAIR=blacksmith|station` opens the repair window; `CARBON_REPAIR_PREVIEW=work|success[:s]` plays its animation.
