# ADR-040: Tree Regrowth Stages, Saplings and Permanent Stone

- **Date**: 2026-11-27
- **Status**: Accepted
- **Amends**: [ADR-014](ADR-014-server-authoritative-config.md) (new `props.json` document),
  [ADR-001](ADR-001-map-delta-sync.md) (chunk delta storage format 4, prop growth stages on the wire)
- **Scope**: `engine/Carbon.Maps`, `server/`, `contracts/Carbon.Contracts.Maps`, `desktop/`, `graphics/`,
  `resources/config`, `resources/art`, `resources/localization`, `tests/`

## Context

The owner's decision after the design discussion: "Trees: stump → slow regrowth with growth stages, plus saplings
players can plant. Stone and ore: never respawn."

What master (`6125a15`) does:

- `server/Carbon.Application/World/Props/PropHarvestService.cs:136` schedules every destroyed prop to come back after
  `tickRate * 30` ticks. Trees, boulders, ore veins and bushes all regrow in place after 30 s. The value is not in config.
- `engine/Carbon.Maps/Streaming/ChunkDelta.cs` keeps the respawn ticks (`_propRespawnTicks`) only in memory.
  `ChunkDelta.Serialize` (storage format 3) writes the prop state without its timer, and `Deserialize` restores it as
  permanent. After a server restart every prop that was waiting to come back is gone forever. The respawn tick is also
  an absolute world tick, and the world tick restarts at 0 with the server, so storing it as is would not work either.
- `ChunkDelta.RemoveExpiredPropStates` allocates a list (`expired ??= []`) and `ChunkWorld2D.ProcessPropRespawns`
  allocates another on every call, on the tick path (AGENTS.md rule 4). A respawn also regenerates the whole chunk
  from the seed (`ChunkStreamer.RebuildIfResident`).
- The spawn clearing (`engine/Carbon.Maps/Generators/Stages/SpawnClearingStage.cs`, #129) places loose stones, fallen
  branches and dry bushes so an empty-handed player can gather a first tool. That only works if they come back.

## Decision

1. **D1 — Trees regrow through stages.**
   - Felling a mature tree leaves a **stump**. The stump grows back through **stump → sapling → young tree → mature
     tree**. Each stage has its own duration, its own vector-art visual and its own harvest yield. The defaults add up
     to about 45 minutes of server time.
   - **Only a mature tree leaves a stump** (amended, D1c). Felling a **young tree** drops `trees.young.yield` logs and
     clears the tile. Cutting a **sapling** uproots it back into its item. Neither leaves a stump or a timer.
   - Felling a mature tree also drops one **sapling** item with chance `trees.mature.saplingChance` (0.2), rolled on
     `GameplayEntropy` (ADR-026, `GameplayDomain.SaplingDrop`, keyed by tick and tile; D1c).
   - Stumps and saplings have no collider and no canopy; a young tree has both, drawn at 60 % size. A stump draws the
     tree's existing `remnantShapes`, a sapling the shared `props/tree_sapling.cvg.json`. All of it is vector art.
   - Growth works while the chunk is not loaded: the chunk delta records the tree type with the stage.
   - A sapling item can be **planted** through the place flow. The item is `placeable`: the primary action plants it on
     the aimed tile, so it gets the client's placement preview and build reach. The server checks:
     - reach (`structures.json` `placementReachTiles`);
     - a sapling in the active hotbar slot (one is used up);
     - natural ground: no water, rock, road or bridge, in a biome that grows trees
       (`BiomePropTables.TryGetPlantedTree`);
     - no structure, construction site or other prop. A dug-up stump's tile can be planted again.

     The sapling grows into the biome's first natural tree (on a dug-up stump's tile, the tree it was) and grows like
     any other tree. The result is `OpStatus.Ok`, `OutOfRange`, `NotFound` (no sapling held), `Invalid` (unsuitable
     ground) or `Conflict` (occupied tile).
   - Other flora (bushes, cactus, mushrooms, dune grass, dead trees, fallen branches) comes back whole after its own
     delay, with no stages.
1b. **D1b — Digging up stumps** (owner addition). A player can dig up a stump to clear the land for building.
   - Any tool works, and none is required. A pickaxe digs at its full mineral damage, an axe at `trees.dig.axeEfficiency`
     (default 0.5) of its wood damage, and bare hands or anything else at `trees.dig.bareHandDamage`. That is slow, but
     a fresh player is never blocked. A stump needs no tool tier.
   - A dig strike is an ordinary swing: it costs stamina per ADR-033 and wears the tool. A blade dulls, as on wood.
   - A dug-up stump is destroyed with no timer. It never regrows, it draws nothing, and the tile can be built on. An
     undug stump or a sapling blocks building (`BuildResultCode.BlockedByNaturalObstacle`).
   - Yield: `trees.stump.yield` logs (default 1) plus one sapling with chance `trees.stump.saplingChance` (0.1; D1c), on
     the same entropy domain.

2. **D2 — Stone, boulders and ore never come back.** A destroyed mineral prop is permanent and stays persisted in its
   chunk delta. The world is infinite: exploration replaces regrowth. `props.json` cannot give a mineral a regrowth
   delay; the loader rejects it.
3. **D3 — Spawn forage stays fast.** Starter forage (loose stones, fallen branches, dry bushes) inside the spawn
   clearing comes back after `spawnForage.respawnSeconds` (default 45 s), so a new player never soft-locks. The same
   types outside the clearing follow D1 and D2: dry bushes regrow after their flora delay, loose stones never.
4. **D4 — All numbers live in `resources/config/props.json`.** It is a server-authoritative, hot-reloadable document
   (ADR-014). Clients that render growth stages read it through the config sync, never from hardcoded values. A reload
   applies to props destroyed after it; props already waiting keep their scheduled tick. PR 1 ships:

   ```json
   {
     "schemaVersion": 1,
     "spawnForage": { "respawnSeconds": 45 },
     "blockedRetrySeconds": 10,
     "regrowSeconds": { "oakTree": 2700, "bush": 600, "...": 0 }
   }
   ```

   A prop type that is not listed never comes back. PR 2 moves the trees from `regrowSeconds` into a `trees` block
   (the loader rejects a tree in `regrowSeconds`):

   ```json
   "trees": {
     "deferWithinTiles": 6,
     "stump":   { "seconds": 900, "durability": 30, "yield": 1, "saplingChance": 0.1 },
     "sapling": { "seconds": 900, "durability": 10, "yield": 0 },
     "young":   { "seconds": 900, "durabilityShare": 0.5, "yield": 1 },
     "mature":  { "yield": 3, "saplingChance": 0.2 },
     "dig":     { "bareHandDamage": 4, "axeEfficiency": 0.5 }
   }
   ```
5. **D5 — Regrowth survives a restart.** Chunk delta storage format 4 appends to each prop state the ticks left until
   it comes back (`uint32`, 0 = permanent), counted from the world tick at save time. On load the timer restarts from
   the current tick. Server downtime does not count as growth time, and a chunk saved before its last change loses at
   most the time since its last save.
   - Rows in formats 1–3 have no timer. Their destroyed props are rescheduled once at startup
     (`ChunkWorld2D.ScheduleLegacyRespawns`) with the delay of their type, which needs the chunk's baseline to know the
     type. The rescheduled chunks are saved again in format 4. Mined stone and ore stay gone.
   - No SQL migration: `chunk_deltas.payload` is an opaque blob, and the format byte travels inside it.
   - Format 5 (PR 2) appends the growth stage and the tree type to each prop state. Format 6 (PR 3) appends the planted
     trees (tile index and tree type). They are added to the chunk before the prop states are applied. Each format
     keeps its own legacy reader. Like natural prop states, planted trees are dropped when a delta migrates to another
     `GeneratorVersion`, because the new generator may put a prop on their tile.
   - A tree felled before format 5 (destroyed, mature stage) is rescheduled with the stump stage's time and comes back
     whole.
6. **D6 — Nothing grows into an occupied tile.** When a prop is due, it waits `blockedRetrySeconds` and tries again
   instead if a structure, a construction site or a living player is on its tile. A stage change (PR 2) is also
   deferred while a player is within `trees.deferWithinTiles` tiles (default 6). The tree changes out of the player's
   sight instead of popping in front of them. The check runs only for props that are due, so it costs nothing on most
   ticks.
7. **D7 — Hot path.** The respawn scan reuses one scratch list owned by `ChunkWorld2D` and allocates nothing. A due
   prop is restored in place in its resident chunk (`Chunk2D.SetProp`) instead of regenerating the chunk. A chunk that
   is not resident regenerates the prop from the seed when it next loads. `ServerTickAllocationTests` keeps its budget.
8. **D8 — Wire.** PR 1 changes nothing on the wire: a prop that comes back is replicated through the existing
   prop-changed path at full durability. PR 2 appends `MapChunkDelta.PropGrowthStages` (`[SerializeOrder(30)]`,
   parallel to `PropIndices`) and `MapPropChanged.GrowthStage` (`[SerializeOrder(5)]`). It also bumps
   `CarbonProtocol.Version` to 7. No opcode is added. Each stage change sends one `MapPropChanged` with the new stage's
   full durability. The client treats a change to a stump as the tree breaking (fracture feedback) and any other stage
   change as quiet growth. PR 3 appends `MapChunkDelta.PlantedIndices` / `PlantedTypes` (`[SerializeOrder(31)]`, `(32)`)
   in the same protocol version 7, since both ship before a release. A `MapPropChanged` for a tile where the client has
   no prop (a tree just planted) makes the client fetch that chunk's delta again.
9. **D9 — Delivery.** Three PRs. (1) persistence fix, hot path, config-driven timing, permanent stone and fast spawn
   forage. (2) tree stages and stumps. (3) sapling drops and planting. `ProceduralWorldGenerator.GeneratorVersion` does
   not change: the world generator's output is the same.

1c. **D1c — Sapling drop rate and clearing (amendment, owner reports after PR 3).**
   - **Drop rate.** The first values (0–2 saplings per felled mature tree, so 1 on average, plus a 50 % chance per dug
     stump) made saplings common loot. A player clearing a grove ended up with more saplings than trees cut. The
     shipped values are one sapling at **20 %** per felled mature tree and **10 %** per dug-up stump. Why these
     numbers:
     - Replanting a felled tree needs no sapling: its stump grows back by itself (D1). A sapling only plants a
       **new** tree (expands or moves a grove), so it can be rare without making woodcutting unsustainable.
     - At 20 %, a player who fells five mature trees gets one sapling on average (expected 0.2 each, a 67 % chance of
       at least one in five). A small grove of five trees grows by about one tree per harvest cycle: steady, but
       earned.
     - Clearing land for good (felling, then digging up the stump) yields 0.2 + 0.1 = 0.3 saplings per tree on
       average. Clearing therefore costs trees: about three cleared trees replant one elsewhere. Moving a forest has a
       price, and land does not convert back into trees for free.
     - Below about 10 % per mature tree, a new player would fell twenty trees before their first sapling, and planting
       would feel absent. Above about 25 %, saplings pile up in the bag again.
     - Both values live in `props.json` and are pinned by `TreeGrowthTests.LoadEmbedded_SaplingDrops_AreRareButSustainable`.
   - **Clearing rule: only a mature tree leaves a stump.** Before, felling a sapling or a young tree left a stump that
     regrew, so a misplaced sapling could never be removed and a tile could not be cleared except by digging a stump.
     Now cutting a **sapling** uproots it back into the sapling item, so it can be moved. Felling a **young** tree drops
     its logs. Both clear the tile for good: destroyed at their stage, with no timer, drawn as nothing, free for
     building or planting. A stump is what is left of a full-grown trunk; a young or a sapling stem has no stump worth
     leaving.

## Alternatives Considered

- **Persist the absolute respawn tick.** Rejected: the world tick restarts at 0, so every timer would jump far into the
  future after a restart.
- **Persist a wall-clock respawn time.** Rejected: the simulation is tick-driven and tested with virtual ticks. Growth
  that continues while the server is down would also make a whole grove change at once on startup.
- **A SQL migration with a separate timer column.** Rejected: the delta is already a versioned blob, and a second
  source of truth for one prop state could disagree with it.
- **Keep regenerating the chunk on respawn.** Rejected: it regenerates the whole chunk from the seed for one prop, and
  it allocates.
- **Let stone respawn slowly.** Rejected by the owner: exploration replaces regrowth for minerals.

## Consequences

- Felled trees, cleared bushes and spawn forage come back after a restart. Mined stone and ore are gone for good.
- Chunk delta rows in format 4 grow by 4 bytes per prop state. Old rows load and are rewritten once.
- Changing a delay needs only a `props.json` edit, applied by hot reload.
- A player camping on a stump keeps it a stump. That is intended.
- Growth stages (PR 2) add a protocol bump. Older clients must update.
