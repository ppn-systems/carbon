# ADR-025: Villages Built from Real Structures, Trader NPCs and the Village Blacksmith

- **Date**: 2026-11-27
- **Status**: Proposed
- **Amended by**: [ADR-026](ADR-026-server-secret-gameplay-entropy.md) — gameplay rolls use a server-only secret, not the world seed
- **Amended**: 2026-11-27 — [Sell basket and buy cart](#sell-basket-and-buy-cart-amendment-2026-11-27) (D18–D23): trades are
  staged in the window and committed as one all-or-nothing batch
- **Scope**: `engine/Carbon.Maps` (Building, Landmarks, Settlements, Streaming), `engine/Carbon.Economy` (Trading),
  `contracts/Carbon.Contracts.Items`, `server/Carbon.Application` (Actors, Commerce), `server/Carbon.Infrastructure`
  (persistence), `desktop/` (rendering, trade window), `resources/config`, `resources/art/structures`
- **Design doc**: [`docs/villages-and-traders.md`](../villages-and-traders.md)

## Context

- Villages are laid out by `VillagePlanner` (`engine/Carbon.Maps/Landmarks/LandmarkPlanners.cs`) as decorative
  `LandmarkPiece.House` entries. `PointOfInterestRenderer.DrawHouse` paints them, `LandmarkPiece.GetColliders` fakes five
  wall boxes, and `TileWalkabilityGrid` treats the whole house as one solid block. They have no doors that open, no
  furniture and nothing a player can interact with.
- The RimWorld-style building system already exists (`docs/building.md`, ADR-011). It covers walls, doors, floors and
  furniture on `BuildingGrid`, ownership in `StructureRecord`, persistence and replication through `ChunkDelta` /
  `MapChunkDelta`, and server validation in `BuildingService`.
- Vendors are generic. `EconomyService.RunVendorOperation` accepts any friendly NPC within 175 px and sells every
  `vendorBuyable` item at a fixed price, with unlimited stock. No client UI calls it.
- `SettlementNpcSpawner` spawns NPCs per *chunk* point of interest. A village close to a chunk border is listed by up to
  four chunks, so it gets up to four guards and four blacksmiths.
- `MapHandlers.HandleChunkRequestAsync` copies only the structure arrays of `MapChunkDelta`. Clients therefore never
  receive construction sites or ownership records on a chunk request.

## Decision

### Villages

1. **D1 — Village buildings are real building-grid structures.** The planner emits `VillagePlot`s (template index,
   rotated origin, quarter turns, role, style) in `LandmarkLayout.Plots` instead of `House` pieces. The well, fields,
   the barrel and the paths stay landmark pieces. Harbours keep their decorative houses for now.
2. **D2 — Data-driven templates.** `resources/config/village_buildings.json` defines:
   - a palette per style: `wall`, `door`, `floor` and `porch` structure types. Temperate is wood; desert is sandstone with
     stone floors; snow is stone with wood floors; swamp is wood on a raised plank deck.
   - building templates: an ASCII grid (`W` wall, `D` door, `.` floor, `p` porch, space for bare ground) authored with
     the entrance on the south row, furniture with an anchor and rotation, and an optional NPC spot. Roles are `house`,
     `store`, `smithy` and `stall`.

   The file is world-generation data. It is embedded into `Carbon.Maps`, like `structures.json`, so the server and every
   client plan identical villages. A change to it changes the world: bump `ProceduralWorldGenerator.GeneratorVersion` and
   update the golden test (`VillageGenerationTests.GoldenVillagePlotFingerprint`).
3. **D3 — Deterministic placement.** The planner places the stall, smithy, store and up to five houses on rings around
   the well, with every entrance turned to face the square. Plots keep a one-tile gap between buildings, stay inside the
   point of interest radius and off the well square, stand on dry ground at the site level, and stay off roads. Roads are
   a pure query that the generator binds into `PointOfInterestResolver`; roads read only site centres, so there is no
   cycle. Everything is a pure function of the seed. `GeneratorVersion` goes from 15 to 16.
4. **D4 — Server materialization.** `VillageMaterializer` (`Carbon.Maps.Settlements`) expands the plots
   (`VillageBlueprint.Expand`) and places each tile with `BuildingGrid.PlaceStructure`. It writes a `StructureRecord`
   owned by `StructureOwners.Village`, with anchor offsets, rotation and the floor beneath, so the tiles are persisted
   and replicated like player builds.
   - It runs once per site, from `SettlementNpcSpawner.PopulateAround` and before any chunk delta is served
     (`TryCreateChunkDeltaPacket`). A client therefore never receives a village chunk without its buildings.
   - It is idempotent: occupied or reserved tiles are skipped, so a restart, or a tile a player already built on,
     changes nothing.
   - The wire format does not change. Clients see villages through the existing `MapChunkDelta` path.
5. **D5 — Faction ownership is server-enforced.** Owner ids at or above `StructureOwners.FactionRangeStart`
   (`0xFFFF_FFFF_FFFF_FF00`) belong to world factions. Player owner ids are folded out of that range
   (`StructureOwners.ToPlayerOwnerId`).
   - `BuildPermissionTable.CanModify` refuses faction structures to everyone except that faction.
   - `BuildingService.ApplyDeconstructionWork` and `ApplyPlayerDamage` (tool strikes, through
     `ConstructionService.DamageStructure`) return the new `BuildResultCode.ProtectedSettlement` (→ `OpStatus.Forbidden`).
   - `ValidatePlacement` refuses blueprints on a faction-owned tile, so nothing can be built on village floors.
   - Doors stay usable: anyone can open and close them.
   - Monster damage (`ApplyDamage` without an attacker) is not blocked.
6. **D6 — Village chests are not lootable.** Chests have no container yet, so they are furniture only. When containers
   arrive, faction chests stay closed by default (D5 already blocks modifying them).
7. **D7 — Generator migration drops faction content.** `ChunkDelta.MigrateTo` drops faction-owned structures and records
   but keeps player edits. The new generator's village is then materialized in their place.
8. **D8 — New structure types (append-only):** `SandstoneWall = 16`, `WoodTable = 17`, `WoodShelf = 18`,
   `MarketStall = 19` and `PlankDeck = 20`. Each has a `structures.json` definition, a `BuildingGrid.CreateBlueprint`
   case and `.cvg.json` art (ADR-005). They have no placeable kits yet, so players cannot build them; adding kits is an
   `items.json` change.
9. **D9 — Rendering.**
   - Structure art gains an optional `size` (`[w, h]` tiles). Multi-tile art (bed, workbench, market stall) is drawn
     once at the anchor and rotated with the record (`VectorMeshBaker.StampFootprintShapes`).
   - `RoofRenderer` finds enclosed rooms from their walls, cached by the new `BuildingGrid.Revision`. It draws a roof in
     the style of the walls: shingles, thatch, snow-covered slate, or a flat clay roof with a parapet. The roof of the
     room the local player stands in is cut away, and the camera's interior zoom uses the same rooms.
   - Stoves get chimneys; a forge or a row of shelves gets a shop sign.
   - This applies to player builds too. The decorative house drawing is no longer used for villages.
10. **D10 — One NPC set per point of interest.** `SettlementNpcSpawner` keys spawns by point of interest, not by chunk.
    The village blacksmith stands at the smithy's NPC spot, next to the forge anvil.
11. **D11 — Chunk requests carry sites and records.** `HandleChunkRequestAsync` copies the `Site*` and `Record*` arrays
    that `ChunkDeltaPacketMapper` already fills. This is a bug fix with no layout change. Clients now know village
    ownership, so placement previews show "Village property".

### Traders

12. **D12 — Trader profiles.** `resources/config/traders.json` is server-only (ADR-014 `s_serverOnlyDocuments`) and
    hot-reloads with the economy. A profile (`merchant`, `general`, `blacksmith`) has:
    - a slot count;
    - weighted entries (item code, quantity range, optional style filter);
    - a price variance;
    - a restock period with per-trader jitter.

    The loader rejects unknown items, and rejects any variance that could price a stock item at or below its vendor
    sell price (no arbitrage, `docs/economy.md` §1).
13. **D13 — Deterministic stock per epoch.** A trader is identified by `TraderKey = hash(site, role)`.
    - `epoch = floor((unixSeconds + jitter(TraderKey)) / periodSeconds)`, which uses server wall-clock time, so restocks
      survive restarts. The tick counter restarts at 0 on every boot, so it cannot be used.
    - Stock = `TraderStockRoller.Roll(profile, worldSeed, TraderKey, epoch, style)`, a pure function seeded by
      `DeterministicRandom`.
    - Only the quantity sold in the current epoch is state.
14. **D14 — Persistence.** A new table, `trader_stock(trader_key, epoch, sold, updated_at_utc)`, is reached through
    `ITraderStockRepository` (SQLite, PostgreSQL and in-memory).
    - It is created idempotently with the base schema (`CREATE TABLE IF NOT EXISTS`). There is no numbered migration,
      so its number cannot clash with migrations that other sessions add.
    - `TradeService` loads it at boot and upserts changed rows in the world's persistence flush, retrying on failure.
    - Rows from an older epoch are ignored.
15. **D15 — Wire.** New opcodes in the items band, outside the ranges other sessions reserved (`0x400A–0x400C`):
    - C2S `TraderOpen` `0x4040` (trader network id);
    - C2S `TraderBuy` `0x4041` (trader, stock slot, quantity, epoch);
    - C2S `TraderSell` `0x4042` (trader, bag slot, quantity);
    - S2C `TraderStock` `0x4048`: trader network id, profile, epoch, seconds to restock, and parallel arrays of item id,
      unit price and remaining quantity, plus the sell price of each item the player could sell.

    Buy and sell reply with `MutationResult`. Open replies only on failure. On success the server pushes a fresh
    `TraderStock` and `WalletSync`.
    - `IWorldOutboundSink.SendTraderStock` has a default empty body, so existing sinks need no change.
    - Requests run on `TradeService`'s own `WorldCommandQueue`, drained right after the world's queue on the same tick
      (ADR-017).
    A buy that names a stale epoch is rejected with `Conflict`, so a restock can never change what a click buys.
16. **D16 — Validation.** Every request needs a live trader NPC within `VendorRangeSq` (175 px) of the player. A buy
    checks the slot, the remaining stock, gold (`Wallet.CanAfford`) and bag space (`InventoryDraft`) before committing
    wallet, inventory and the sold count together. A sell pays the economy's vendor sell price, which keeps the
    25 % spread as a gold sink.

### Blacksmith

17. **D17 — Blacksmith.** The village and sanctuary blacksmiths (`MasterBlacksmith`, now
    `ForgeArtisan | Trader`) trade from the `blacksmith` profile (weapons, armor, tools) and keep forge repair
    (`RepairInventoryItem`, which the server already accepts next to a forge artisan).
    - **Repair quotes.** `TraderStock` gains `RepairSlots` and `RepairCosts` (`SerializeOrder` 11 and 12). These are the
      server's `PriceBook.GetRepairCost` quote for each worn bag item, sent only by a blacksmith. The window's Repair mode
      shows the quotes and sends `RepairInventoryItem`, then refreshes. The client never prices a repair.
    - **Enhancement.** It belongs to the enhancement work (`ItemEnhancementPolicy`, its own forge window, ADR-022/024).
      The trade window's Enhance button calls the existing forge entry point, `WorldGameplayScene.OpenForgeWindow`. It
      adds no enhancement UI and no new controller method.

### Sell basket and buy cart (amendment, 2026-11-27)

The owner's feedback: a click in the trade window sold the item at once. Every instant trade path before this amendment:

| Path | Where | Effect |
| :--- | :--- | :--- |
| Click a bag item | `TradeInteraction.HandleWindowInput` → `TraderSession.TrySell(slot, 1)` | sells 1 unit (`TraderSell` `0x4042`) |
| Shift+click a bag item | same, `quantity = slot.Quantity` | sells the whole stack, no confirmation |
| Click a stock row | `TraderSession.TryBuy(row, 1)` | buys 1 unit (`TraderBuy` `0x4041`) |
| Shift+click a stock row | same, `BulkBuy = 5` | buys 5 units |
| Drag | none | the trade window had no drag; `InventoryInputController` is not routed while it is open |
| `VendorBuyItem` / `VendorSellItem` (`0x4007`/`0x4008`) | `IGameplayController.TryVendorBuy/TryVendorSell` | no caller in the client |

18. **D18 — Staged trades.** The trade window stages trades and commits them only on a button press.
    - **Sell basket ("To sell").** A click or a drag moves a bag item into the basket: one unit, or the whole stack with
      Shift; a quantity control (−/+) adjusts each line up to the slot's quantity. Each line shows its unit price and
      subtotal, with a running total. **Sell** commits; **Clear**, closing the window, or walking out of range returns
      every line.
    - **Buy cart.** This is symmetric. A click on a stock row adds a unit (Shift adds 5), capped by the units left. The
      total is shown against the wallet, and **Buy** is disabled while the total exceeds it.
    - A basket line only *reserves* units in the UI: the bag cell shows them as set aside, and the server sees nothing
      until the commit. Equipment slots (`36..41`) are never shown and can never be added; the server refuses them too.
    - Adding an enhanced item (`UpgradeLevel > 0`) marks its line, and the first press of **Sell** asks to confirm. The
      second press sells.
19. **D19 — Wire.** New opcodes in `0x4052–0x4057` (after ADR-028's `0x4050`/`0x4051`):
    - C2S `TraderSellBatch` `0x4052`: trader, parallel lists of bag slot, the item the player saw there and units, and
      the total gold the window showed.
    - C2S `TraderBuyBatch` `0x4053`: trader, epoch, parallel lists of stock slot, item and units, and the total.
    - S2C `TraderBatchResult` `0x4054`: trader, kind (sell/buy), `OpStatus`, a `TradeBatchError` code and the index
      of the failing line. It carries codes only; the client maps each code to a `[trade_basket]` text.
    - `0x4055–0x4057` stay free.
    - `TraderSell` and `TraderBuy` keep working for older clients, but the desktop client no longer sends them. A
      later protocol bump can retire them, like `RepairInventoryItem` (ADR-028).
20. **D20 — All or nothing.** `VendorService.TrySellBatch` and `TraderBook.TryBuyBatch` validate every line before
    anything changes, then commit wallet, bag and sold counts together.
    - **Sell.** Bag slots only, no duplicate slots, each slot still holds the item and units named (`BagChanged`), and
      every item sells (`NotSellable`).
    - **Buy.** Current epoch, no duplicate slots, each stock slot still offers that item, enough units left
      (`SoldOut`), enough gold (`NotEnoughGold`), and everything fits in the bag (`BagFull`, via `InventoryDraft`).
    - **Prices.** The server recomputes every price, and a total other than the confirmed one is refused as
      `PricesChanged`. A buy against an older epoch is refused the same way.
21. **D21 — Re-confirm.** On a `PricesChanged` refusal the server pushes a fresh `TraderStock`.
    - The client re-prices the lines it still holds. If any price, the epoch or a line's stock moved since the player
      last looked, it shows "prices changed", and the button must be pressed once more to accept the new total.
    - The same happens when a restock arrives while the cart is open.
22. **D22 — Tick and queue.** A batch is one `WorldCommand` on `TradeService`'s queue (ADR-017).
    - Its lines ride in the new `WorldCommand.Payload`, allocated on the I/O thread.
    - The tick copies them into a `stackalloc` span, so draining stays allocation-free.
    - The outcome reaches the handler as a packed `TradeBatchReceipt` in `WorldMutationOutcome.Revision`, the same
      way as `RepairReceipt`.
23. **D23 — Client.** The basket and cart live in a plain `TradeBasket` model (`desktop/`): tested without a window,
    owned by `TradeInteraction`, drawn by `TradeModalWidget`. No database change.

## Alternatives Considered

- **Generate village structures on every client as a base layer (no delta).** This sends nothing over the wire, but
  `ChunkDelta.Structures` is the complete structure set of a chunk, so the delta would need "removed base structure"
  tombstones and a persistence format change. Server materialization reuses the existing paths unchanged and costs about
  5–7 KB per village chunk, once.
- **Keep decorative houses and add interiors.** This duplicates collision, doors and rendering, and still cannot hold
  furniture.
- **Hot-reloadable `village_buildings.json` served by config sync.** The plan must be identical on every machine and
  part of the generator version. Runtime reload would desync the client and server layouts.
- **Tick-based restock epochs.** `_currentTick` starts at 0 on every boot, so stock would reset on restart and could be
  farmed.
- **Persist the full rolled stock.** Deriving the stock from the seed and the epoch keeps the table tiny and lets tests
  replay it. Only sold counts are state.
- **Per-chunk NPC spawning kept.** This was rejected because it produced duplicate NPCs (see Context).

## Consequences

- Villages have real walls, opening doors, furniture, floors, roofs, collision and navigation, rendered the same way as
  player builds. Players can enter every building but cannot dismantle, damage or build over it.
- `GeneratorVersion` 16: old village deltas are migrated (D7), and clients built for version 15 refuse to connect.
- Each village adds about 300–450 structure tiles to two to four chunk deltas. This is persisted once and sent once
  per chunk per client.
- The roof renderer also gives player-built rooms roofs. Irregular rooms get a simple per-tile roof.
- The new structure types have no kits yet, and harbours still use decorative houses.
- Trader state adds one small table. UI strings are `GameText` keys (`[trade]` and, for the basket and cart,
  `[trade_basket]` in `resources/localization/*.ini`).
- With the D18 basket, nothing sells or buys without an explicit press of **Sell** or **Buy**. A trade takes one more
  click, and that click is what keeps a mis-click from costing a stack.
- Client code stays within the audit round 2 size caps:
  - trading lives in `TraderSession` (behind `IGameplayController.Trading`), `TradeInteraction` (the scene's talk, click
    and window input), and a stateless `TradeModalWidget`;
  - the scene's screenshot schedule moved to `AutoCapture`, so the trader capture can wait for the stock.
