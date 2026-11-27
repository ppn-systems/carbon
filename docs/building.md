# Building: blueprint, construction, deconstruction

RimWorld-style construction for Carbon. Code: `engine/Carbon.Maps/Building/`. Data: `resources/config/structures.json`. Wire/persistence decision: [ADR-011](adr/ADR-011-construction-sites-in-chunk-delta.md).

## Audit (before this change)

The question was "can you build and destroy yet?". The answer was only partly yes:

| Area | What existed | What was missing or wrong |
| :--- | :--- | :--- |
| Placement | `BuildingGrid.TryPlaceStructure` placed a finished structure instantly. The server consumed one "structure kit" item (`AuthoritativeWorld.TryPlaceStructure`). | No blueprint or frame stage, no construction work, no material costs (only kits). Placement checked one tile: no footprints, no rotation, no cliff check, no floor support, no door-between-walls rule. Entity overlap was checked only against the placing player's own tile, on the server. |
| Data | Hit points, tool tiers and affordance were hardcoded in `BuildingGrid.CreateBlueprint` and `BuildingFoundation.GetRequiredAffordance`. | No data file for costs, work, size or placement rules. |
| Destroy | `BuildingGrid.ApplyDamage` reduced durability. | A destroyed structure stayed on the grid as rubble forever. The server's salvage drop checked `updatedStruct.Type == StructureType.None`, which never happens, so destroying a structure never dropped anything. |
| Demolish | `TryDemolishStructure` removed a structure instantly and dropped its kit recipe ingredients. | No work time, no configurable refund, and no ownership: any player in range could demolish anyone's walls. |
| Cancel | — | Did not exist. |
| Persistence | Finished structures were stored in `ChunkDelta.Structures` (format v2) and `MapChunkDelta`. | No owner, no rotation, no multi-tile anchor, no in-progress sites. |
| Tests | Foundation, autotile, rooms, doors, collision. | No lifecycle, refund, permission or data-loading tests. |

## Lifecycle

```
PlaceBlueprint ──► Blueprint ──DeliverMaterials (all)──► Frame ──ApplyConstructionWork (per tick)──► Complete
      │                │                                   │                                          │
      │                └──────── CancelBlueprint: 100% of delivered materials refunded ───────────────┘ (site removed)
      │
Complete ──ApplyDeconstructionWork (per tick)──► Removed  (refund deconstructRefundFraction, default 75%)
Complete ──ApplyDamage until 0 HP──────────────► Removed  (drop destroyedDropFraction, default 25%)
```

- **Blueprint**: validated and reserves its footprint. Consumes nothing and does not block movement.
- **Frame**: every cost entry has been delivered. Deliveries can be partial: whatever the builder carries is taken, and the site stays a blueprint until the cost is fully covered.
- **Complete**: the structure is placed on `BuildingGrid` for every footprint tile, with a `StructureRecord` holding the owner, the offset from the anchor, the rotation and any floor underneath. Wall autotiling updates and the roof/room cache is cleared.
- A frame of a solid structure does not complete while an entity stands inside its footprint (`BlockedByEntity`). Work is kept, and the next tick completes it once the tile is clear.
- When a structure is removed it is removed as a whole footprint, a floor it covered is restored, and autotiling and room detection run again.

## Rules

All rules are checked on the server by `BuildingService`. Every call returns a `BuildMutationResult` with a `BuildResultCode`; `ToOpStatus()` maps it onto `MutationResult.Status`.

| Rule | Code |
| :--- | :--- |
| Unknown type or rotation not allowed | `UnknownStructure`, `RotationNotAllowed` |
| Water (affordance `None`) | `BlockedByWater` |
| Cliff / solid rock, or footprint over different height levels | `BlockedByCliff` |
| Terrain affordance below the definition's `affordance` | `InsufficientAffordance` |
| Overlaps another blueprint or frame | `BlockedByConstructionSite` |
| Overlaps a finished structure. Exception: a non-floor structure may stand on a finished floor | `BlockedByExistingStructure` |
| Solid natural prop (tree, rock) | `BlockedByNaturalObstacle` |
| Live entity on the footprint of a solid structure | `BlockedByEntity` |
| `requiresFloor` without a finished floor under every tile | `RequiresFloor` |
| `requiresWallsOnSides` (doors) without walls, doors, pillars or their blueprints on two opposite sides | `RequiresWallsOnSides` |
| Builder farther than `builderReachTiles` (deliver, work, deconstruct) or `placementReachTiles` (place, cancel) away, measured as Chebyshev distance to the footprint | `OutOfReach` |
| Actor is not the owner and was not granted access (`IBuildPermissions`) | `PermissionDenied` |
| Tile belongs to a village (faction-owned record): deconstruct, tool strike or placement | `ProtectedSettlement` |
| Builder's tool tier is below `toolTier` (construction work) | `ToolTierTooLow` |
| No still-missing material in the inventory | `InsufficientMaterials` |
| Work on a blueprint | `MaterialsNotDelivered` |

Structures with no record (placed before this change) are treated as unowned 1×1 structures that anyone may deconstruct.

Village buildings are real structures owned by a world faction (`StructureOwners.Village`, ADR-025, [villages-and-traders.md](villages-and-traders.md)). Deconstructing, damaging or building over them returns `ProtectedSettlement`; their doors open for everyone.

## `structures.json`

```jsonc
{
  "deconstructRefundFraction": 0.75,   // refunded on deconstruct
  "destroyedDropFraction": 0.25,       // dropped when destroyed by damage
  "deconstructWorkFraction": 0.5,      // deconstruct work = work * fraction
  "builderReachTiles": 3,
  "placementReachTiles": 4,          // build reach: server gate, engine placement check and client preview
  "builderWorkPerSecond": 60,          // work one builder adds per second
  "structures": [
    {
      "id": "workbench", "type": "Workbench",           // type = StructureType enum name
      "category": "Station", "material": "Wood",        // StructureCategory / StructureMaterial names
      "cost": [ { "item": "wood_log", "qty": 6 } ],     // ItemArchetype.Code from the item catalog
      "work": 150, "hitPoints": 120, "toolTier": 1,
      "affordance": "Medium",                           // TerrainAffordance name
      "size": [2, 1], "rotatable": true,                // East/West swap width and height
      "requiresFloor": false, "requiresWallsOnSides": false,
      "moveSpeed": 1.15                                 // floors only
    }
  ]
}
```

Material codes are `ItemArchetype` / `items.json` codes. Basic structures cost raw materials (`wood_log`, `stone_chunk`, `iron_ore`); advanced ones (metal walls and doors, stove, chest, bed) cost processed materials (`wood_plank`, `stone_brick`, `iron_ingot`). `StructureCatalogTests` fails if a code is missing from `resources/config/items.json`. On the server, codes resolve through the loaded `GameEconomy.Items` catalog, with `StarterGameCatalog` as the fallback when no economy data is loaded.

The server reads the file from its config directory (`ServerGameData.LoadStructures`) and sends it to clients with the other synced documents (ADR-014). The desktop client builds only with that synced copy (`SyncedStructureCatalog`): its build reach, footprints and site work amounts come from the server, and before the first sync nothing can be placed (audit COUP-5). The document is restart-required on the server, so a hot reload never changes it under a running world. Village planning sizes furniture with the same catalog: the server passes its loaded one to `ProceduralWorldGenerator` and the client its synced copy, so villages match. The file is also compiled into `Carbon.Maps` as an embedded resource; only tools and tests that build a world without a catalog fall back to it, and an architecture test keeps `StructureCatalog.LoadEmbedded()` out of `desktop/`.

`StructureCatalog.Parse` rejects unknown enum names, duplicate types or ids, non-positive work, hit points or quantities, and footprints larger than 15 tiles per side. Hit points, tool tier, category and material must match `BuildingGrid.CreateBlueprint`, because clients rebuild structures from that table when decoding `MapChunkDelta`. `StructureCatalogTests` enforces this.

## Public API (engine)

| Type | Purpose |
| :--- | :--- |
| `BuildingService` | `ValidatePlacement`, `PlaceBlueprint`, `DeliverMaterials`, `ApplyConstructionWork`, `CancelBlueprint`, `ApplyDeconstructionWork`, `ApplyDamage`. `BuildingService.ForWorld(ChunkWorld2D, catalog, permissions, isEntityAt)` wires it to a streamed world. |
| `BuildActor` | Server-resolved actor: id, tile, tool tier. Never taken from the client. |
| `IMaterialSource` / `IMaterialSink` | Inventory port (`CountOf`, `Take`, `Add`) so `Carbon.Maps` does not depend on `Carbon.Items`. |
| `IConstructionEnvironment` | Terrain / prop / entity port. `ChunkWorldConstructionEnvironment` is the `ChunkWorld2D` adapter and uses `PeekTileAtWorld`, which never generates chunks. |
| `IBuildPermissions`, `BuildPermissionTable` | Owner-or-granted policy. |
| `StructureCatalog`, `StructureDefinition`, `BuildingRules` | Data loaded from `structures.json`. |
| `ConstructionLedger`, `ConstructionSite`, `StructureRecord` | Site and ownership store, exposed as `ChunkWorld2D.Construction` and mirrored into `ChunkDelta`. |

`BuildingService` is not thread-safe. Call it on the world tick thread or under the world lock, as `AuthoritativeWorld` already does for `BuildingGrid`. A work tick does not allocate: the delta copies site state into its existing snapshot.

## Server (`AuthoritativeWorld`)

`server/Carbon.Application/World/Construction/ConstructionService.cs` routes every building mutation through `BuildingService`:

| Request | Behaviour |
| :--- | :--- |
| `MapStructurePlace` (type, tile, `Rotation`) and the primary action with a placeable kit selected | Places a blueprint owned by the character, delivers what the inventory holds, and makes it the player's build job. Kits select a blueprint and are not consumed. |
| Fixed tick | Each player's job tops up deliveries and adds `builderWorkPerSecond / tickRate` work. It pauses while the player is out of reach, short of materials, lacks a tool tier, or someone stands in the frame. When the structure completes or is removed, `MapStructureChanged` is pushed for every footprint tile. |
| `MapStructureDemolish` | Cancels a blueprint (100% refund) or starts deconstruction. Only the owner, or players the owner granted, may deconstruct (`Forbidden` otherwise). |
| Tool strike on a structure | `BuildingService.ApplyDamage`, gated by the tool's harvest tier. A destroyed structure drops 25% of its materials as ground loot. |

- The owner id is derived from the character `Guid`, so ownership survives reconnects and restarts.
- The builder's tool tier is the highest tier among tools in the inventory.
- Refunds that do not fit in the inventory drop at the player's feet.
- Placement and completion check only players for entity overlap; monsters are not checked yet.

## Client build UX (desktop)

The client previews and displays; the server decides every outcome.

- **Placement ghost.** When a placeable kit is selected and the aimed tile is within reach, `ConstructionOverlayRenderer` draws the structure's `.cvg.json` art over the footprint. The ghost is green when `IWorldReadModel.PreviewPlacement` returns `Ok`, and red with a cross and the reason label (`BuildResultText`) otherwise. `PreviewPlacement` runs the same `BuildingService.ValidatePlacement` as the server, against the client's world copy, the local player and remote entities.
- **Sites.** A blueprint is drawn as faded art with a blue dashed outline. A frame fades in as work progresses and gets scaffold hatching. Both show a green progress bar, and nearby ones also show a label (`Building 40% - [Z] cancel`).
- **Deconstruction.** An orange pulsing outline and an orange progress bar.
- **Data.** The server pushes `MapConstructionChanged` (S2C `0x1133`) when a stage changes and in 10% progress steps. The client keeps a `ConstructionMarkerBook`, which chunk-delta imports also seed, and mirrors pushed sites into its `ConstructionLedger` with `ImportSite`/`ForgetSite`, so previews see fresh blueprints.
- **Keys.** Primary action (LMB) with a kit places a blueprint. `Z` or RMB on a blueprint cancels it, and on an owned structure starts deconstruction.

## Not done yet

- A UI to choose rotation (the ghost always previews North) and to grant build permissions (`BuildPermissionTable.Grant` is not exposed to clients yet; grants are in memory only).
- Monsters do not attack structures yet. `BuildingService.ApplyDamage` is ready for it.
- Deconstruction progress is kept in memory only and resets on restart; construction progress is persisted.
- Frames do not block movement yet.
