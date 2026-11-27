// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Players;
using Carbon.Application.World.Props;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Data;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Props;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    /// <summary>
    /// Ticks until a destroyed prop of <paramref name="type"/> on a world tile comes back (ADR-040): starter forage in the
    /// spawn clearing after its short delay, flora after its <c>props.json</c> delay, stone and ore never (0).
    /// Trees felled before the growth stages existed (legacy rows) come back after the stump stage's time.
    /// </summary>
    private ulong PropRegrowDelayTicksLocked(MapPropType type, int tileX, int tileY) =>
        MapProp.IsTree(type)
            ? _propRegrowth.Trees.StageTicks(TreeGrowthStage.Stump, this.TickRate)
            : _propRegrowth.DelayTicks(type, SpawnClearingStage.IsInClearing(_chunkWorld.SpawnTile, tileX, tileY), this.TickRate);

    /// <summary>Whether a living player stands on the world tile, so a prop due there waits instead of growing into them.</summary>
    private bool IsPropTileBlockedLocked(int tileX, int tileY)
    {
        foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
        {
            PlayerState state = kvp.Value;
            if (!_simulation.Entities.IsAlive(state.Entity) || !_simulation.Entities.HasTransform(state.Entity))
            {
                continue;
            }

            Vector2 position = _simulation.Entities.GetTransform(state.Entity).Position;
            if ((int)MathF.Floor(position.X / this.TileSize) == tileX && (int)MathF.Floor(position.Y / this.TileSize) == tileY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a living player is within <c>trees.deferWithinTiles</c> of the tile, so a tree there grows its next stage
    /// later, out of their sight (ADR-040 D6).
    /// </summary>
    private bool IsPropTileWatchedLocked(int tileX, int tileY)
    {
        int radius = _propRegrowth.Trees.DeferWithinTiles;
        if (radius <= 0)
        {
            return false;
        }

        float reach = (radius + 0.5f) * this.TileSize;
        Vector2 center = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
        {
            PlayerState state = kvp.Value;
            if (_simulation.Entities.IsAlive(state.Entity) && _simulation.Entities.HasTransform(state.Entity) &&
                Vector2.DistanceSquared(_simulation.Entities.GetTransform(state.Entity).Position, center) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Debug screenshots (<c>CARBON_DEBUG_TREE_STAGES</c>): the trees nearest the spawn, out to 14 tiles, start at
    /// stump, sapling, young and mature in turn, and never grow on, so every stage is in view.
    /// </summary>
    private int StageTreesAroundLocked(int spawnX, int spawnY)
    {
        List<(int Distance, int X, int Y, MapPropType Type)> trees = [];
        for (int dy = -14; dy <= 14; dy++)
        {
            for (int dx = -14; dx <= 14; dx++)
            {
                if (_chunkWorld.TryGetPropAt(spawnX + dx, spawnY + dy, out MapProp prop) && MapProp.IsTree(prop.Type) && !prop.Destructibility.IsDestroyed)
                {
                    trees.Add(((dx * dx) + (dy * dy), spawnX + dx, spawnY + dy, prop.Type));
                }
            }
        }

        trees.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        ReadOnlySpan<TreeGrowthStage> cycle = [TreeGrowthStage.Stump, TreeGrowthStage.Sapling, TreeGrowthStage.Young, TreeGrowthStage.Mature];
        int staged = 0;
        for (int i = 0; i < trees.Count; i++)
        {
            TreeGrowthStage stage = cycle[i % cycle.Length];
            if (stage != TreeGrowthStage.Mature &&
                _chunkWorld.TrySetTreeStageAt(trees[i].X, trees[i].Y, stage, _propRegrowth.Trees.StateOf(trees[i].Type, stage), 0UL, out _))
            {
                staged++;
            }
        }

        return staged;
    }

    /// <summary>
    /// Plants the sapling in the player's active hotbar slot on a tile within build reach (ADR-040 D1). Synchronous form
    /// for tests; players plant with the primary action while holding a sapling.
    /// </summary>
    /// <param name="connectionId">The planting player.</param>
    /// <param name="tileX">World tile X.</param>
    /// <param name="tileY">World tile Y.</param>
    /// <returns>
    /// <see cref="OpStatus.Ok"/>; <see cref="OpStatus.OutOfRange"/> beyond build reach; <see cref="OpStatus.NotFound"/>
    /// when no sapling is held; <see cref="OpStatus.Invalid"/> on unsuitable ground; <see cref="OpStatus.Conflict"/> on an
    /// occupied tile.
    /// </returns>
    public OpStatus TryPlantSapling(ulong connectionId, int tileX, int tileY)
    {
        lock (_gate)
        {
            return _players.ByConnection.TryGetValue(connectionId, out PlayerState? state)
                ? this.PlantSaplingLocked(state, tileX, tileY)
                : OpStatus.Forbidden;
        }
    }

    private OpStatus PlantSaplingLocked(PlayerState state, int tileX, int tileY)
    {
        if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
        {
            return OpStatus.OutOfRange;
        }

        ushort slotIndex = state.Session.SelectedHotbarSlot;
        ItemSlot held = slotIndex < state.Inventory.Capacity ? state.Inventory.GetSlot(slotIndex) : ItemSlot.Empty;
        if (held.IsEmpty || !_itemRules.ItemCatalog.TryGetByCode(PropHarvestService.SaplingCode, out ItemCatalogEntry? sapling) || held.ItemId != sapling.Id)
        {
            return OpStatus.NotFound;
        }

        PlantResult result = _chunkWorld.TryPlantTree(tileX, tileY, _propRegrowth, _currentTick, this.TickRate, out MapProp planted);
        if (result != PlantResult.Planted)
        {
            return result == PlantResult.Occupied ? OpStatus.Conflict : OpStatus.Invalid;
        }

        ItemSlot left = held.Quantity > 1 ? held with { Quantity = held.Quantity - 1 } : ItemSlot.Empty;
        state.Inventory.SetSlot(slotIndex, in left);
        _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, slotIndex, left));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY)));
        (int offset, int count) = this.AppendTileRecipientsLocked(tileX, tileY);
        if (count > 0)
        {
            _outbound.Pending.PropChanges.Add(new OutboundPropChange(
                offset, count, tileX, tileY, planted.Destructibility.CurrentDurability, IsDestroyed: false, TreeGrowthStage.Sapling));
        }

        return OpStatus.Ok;
    }

    /// <summary>
    /// Debug screenshots (<c>CARBON_DEBUG_PLANTED_GROVE</c>): saplings planted on every other tile of a 7 × 3 patch south
    /// of the spawn, every second one grown to a young tree, through the same rules as a player planting them.
    /// </summary>
    private int PlantGroveLocked(int spawnX, int spawnY)
    {
        int planted = 0;
        for (int dy = 3; dy <= 7; dy += 2)
        {
            for (int dx = -6; dx <= 6; dx += 2)
            {
                int x = spawnX + dx;
                int y = spawnY + dy;
                if (_chunkWorld.TryPlantTree(x, y, _propRegrowth, _currentTick, this.TickRate, out MapProp sapling) != PlantResult.Planted)
                {
                    continue;
                }

                if ((planted++ & 1) == 1)
                {
                    _ = _chunkWorld.TrySetTreeStageAt(x, y, TreeGrowthStage.Young, _propRegrowth.Trees.StateOf(sapling.Type, TreeGrowthStage.Young), 0UL, out _);
                }
            }
        }

        return planted;
    }
}
