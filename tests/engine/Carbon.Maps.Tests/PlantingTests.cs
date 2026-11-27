// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-040 D1: a sapling planted on natural ground grows into the biome's tree like any other tree; water, rock and
/// occupied tiles are refused; a dug-up stump's tile can be planted again; planted trees are saved and sent to clients.
/// </summary>
public sealed class PlantingTests
{
    private const ulong Seed = 42UL;
    private const int TickRate = 10;

    private static readonly PropRegrowthRules s_rules = PropRegrowthRules.LoadFromJson(
        """{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 5, "regrowSeconds": {}, """ + PropRegrowthTests.TreesJson + "}");

    [Fact]
    public void TryPlantTree_OpenGround_PlantsASaplingThatGrowsIntoTheBiomeTree()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, BiomeType biome) = FindOpenGround(world);
        Assert.True(BiomePropTables.TryGetPlantedTree(biome, out PropRule tree));

        Assert.Equal(PlantResult.Planted, world.TryPlantTree(x, y, s_rules, currentTick: 0UL, TickRate, out MapProp sapling));

        Assert.Equal(tree.Type, sapling.Type);
        Assert.Equal(TreeGrowthStage.Sapling, sapling.GrowthStage);
        Assert.True(world.TryGetPropAt(x, y, out MapProp onTile));
        Assert.Equal(sapling, onTile);
        Assert.True(world.HasGrowingTreeAt(x, y), "a sapling blocks building");

        ulong young = s_rules.Trees.StageTicks(TreeGrowthStage.Sapling, TickRate);
        ulong mature = young + s_rules.Trees.StageTicks(TreeGrowthStage.Young, TickRate);
        Assert.Equal(1, world.ProcessPropRespawns(young, s_rules, TickRate, null, null, null));
        Assert.Equal(1, world.ProcessPropRespawns(mature, s_rules, TickRate, null, null, null));
        Assert.True(world.TryGetPropAt(x, y, out MapProp grown));
        Assert.Equal(TreeGrowthStage.Mature, grown.GrowthStage);
        Assert.Equal(MapProp.BaselineDestructibility(tree.Type), grown.Destructibility);
        Assert.True(grown.HasCollision, "a planted tree grows into a full tree");
    }

    [Fact]
    public void TryPlantTree_Water_IsUnsuitableGround()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindTile(world, (tile, _) => tile.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean);

        Assert.Equal(PlantResult.UnsuitableGround, world.TryPlantTree(x, y, s_rules, 0UL, TickRate, out _));
        Assert.False(world.TryGetPropAt(x, y, out _));
    }

    [Fact]
    public void TryPlantTree_TileWithAStructureOrAnotherProp_IsOccupied()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, BiomeType biome) = FindOpenGround(world);
        Assert.Equal(PlacementValidationResult.Success, world.Buildings.TryPlaceStructure(x, y, StructureType.WoodFloor, biome, false, out _));
        (int bx, int by) = FindTile(world, (_, prop) => prop is { Type: MapPropType.Boulder });

        Assert.Equal(PlantResult.Occupied, world.TryPlantTree(x, y, s_rules, 0UL, TickRate, out _));
        Assert.Equal(PlantResult.Occupied, world.TryPlantTree(bx, by, s_rules, 0UL, TickRate, out _));
    }

    [Fact]
    public void TryPlantTree_DugUpStumpTile_PlantsTheTreeThatStoodThere()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindTile(world, (_, prop) => prop is { } p && MapProp.IsTree(p.Type));
        Assert.True(world.TryGetPropAt(x, y, out MapProp tree));
        Assert.True(world.TrySetTreeStageAt(x, y, TreeGrowthStage.Stump, s_rules.Trees.StateOf(tree.Type, TreeGrowthStage.Stump), 100UL, out _));
        Assert.Equal(PlantResult.Occupied, world.TryPlantTree(x, y, s_rules, 0UL, TickRate, out _));
        Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, 0UL, out MapProp dug));
        Assert.True(world.TrySetTreeStageAt(x, y, TreeGrowthStage.Stump, dug.Destructibility, 0UL, out _));

        Assert.Equal(PlantResult.Planted, world.TryPlantTree(x, y, s_rules, 0UL, TickRate, out MapProp sapling));

        Assert.Equal(tree.Type, sapling.Type);
        Assert.Equal(TreeGrowthStage.Sapling, sapling.GrowthStage);
        Assert.False(sapling.Destructibility.IsDestroyed);
    }

    [Fact]
    public void SerializeDeserialize_PlantedSapling_IsBackAfterARestartAndKeepsGrowing()
    {
        ChunkWorld2D source = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, _) = FindOpenGround(source);
        Assert.Equal(PlantResult.Planted, source.TryPlantTree(x, y, s_rules, 0UL, TickRate, out MapProp sapling));
        Assert.True(source.TryGetDelta(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out ChunkDelta? delta));

        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        world.ImportDelta(ChunkDelta.Deserialize(delta!.Serialize(currentTick: 0UL), currentTick: 0UL));

        Assert.True(world.TryGetPropAt(x, y, out MapProp reloaded));
        Assert.Equal(sapling, reloaded);
        Assert.Equal(1, world.ProcessPropRespawns(s_rules.Trees.StageTicks(TreeGrowthStage.Sapling, TickRate), s_rules, TickRate, null, null, null));
        Assert.True(world.TryGetPropAt(x, y, out MapProp young));
        Assert.Equal(TreeGrowthStage.Young, young.GrowthStage);
    }

    [Fact]
    public void ChunkDeltaPacketMapper_PlantedSapling_ReachesTheClient()
    {
        ChunkWorld2D server = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, _) = FindOpenGround(server);
        Assert.Equal(PlantResult.Planted, server.TryPlantTree(x, y, s_rules, 0UL, TickRate, out MapProp sapling));
        Assert.True(server.TryGetDelta(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out ChunkDelta? delta));
        using MapChunkDelta packet = ChunkDeltaPacketMapper.ToPacket(delta!);
        using MapChunkDelta received = MapChunkDelta.Deserialize(packet.Serialize());

        ChunkWorld2D client = new(new ProceduralWorldGenerator(Seed));
        client.ImportDelta(ChunkDeltaPacketMapper.FromPacket(received));

        Assert.True(client.TryGetPropAt(x, y, out MapProp seen));
        Assert.Equal(sapling.Type, seen.Type);
        Assert.Equal(TreeGrowthStage.Sapling, seen.GrowthStage);
        Assert.Equal(sapling.WorldPosition, seen.WorldPosition);
    }

    private static (int X, int Y, BiomeType Biome) FindOpenGround(ChunkWorld2D world)
    {
        (int x, int y) = FindTile(world, (tile, prop) =>
            prop is null && !tile.IsSolid && tile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
            BiomePropTables.TryGetPlantedTree(tile.Biome, out _));
        return (x, y, world.GetTileAtWorld(x, y).Biome);
    }

    private static (int X, int Y) FindTile(ChunkWorld2D world, Func<MapTile, MapProp?, bool> match)
    {
        for (int cy = -3; cy <= 3; cy++)
        {
            for (int cx = -3; cx <= 3; cx++)
            {
                Chunk2D chunk = world.GetOrCreateChunk(cx, cy);
                for (int ly = 0; ly < Chunk2D.Size; ly++)
                {
                    for (int lx = 0; lx < Chunk2D.Size; lx++)
                    {
                        if (chunk.IsRoad(lx, ly) || chunk.IsBridge(lx, ly))
                        {
                            continue;
                        }

                        int index = chunk.FindPropIndexAt(lx, ly);
                        MapProp? prop = index >= 0 ? chunk.Props[index] : null;
                        int x = (cx * Chunk2D.Size) + lx;
                        int y = (cy * Chunk2D.Size) + ly;
                        if (!world.Buildings.TryGetStructure(x, y, out _) && match(chunk.GetTile(lx, ly), prop))
                        {
                            return (x, y);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("no matching tile near the origin");
    }
}
