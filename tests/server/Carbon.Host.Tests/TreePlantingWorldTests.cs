// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-040 D1 through the authoritative world: a player plants the sapling they hold on open ground within build reach;
/// it uses up one sapling, grows into a tree and survives a restart. Out of reach, without a sapling, on water and on an
/// occupied tile, planting is refused and nothing is used up.
/// </summary>
public sealed class TreePlantingWorldTests
{
    private const int TickRate = RegrowthWorldFixture.TickRate;

    private static readonly uint s_saplingId = ItemRules.LoadShippedDefaults().ItemCatalog.ResolveId("sapling");

    [Fact]
    public async Task TryPlantSapling_OpenGroundInReach_PlantsASaplingThatGrowsAndUsesUpOne()
    {
        (int x, int y) = FindOpenGround();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, Saplings(3)).ConfigureAwait(true);

        f.World.TryPlantSapling(RegrowthWorldFixture.Conn, x, y).Should().Be(OpStatus.Ok);

        f.Probe(x, y).Should().Be((false, TreeGrowthStage.Sapling), "the planted sapling is a growing tree on the tile");
        HeldSaplings(f.World).Should().Be(2, "planting uses up one sapling");
        f.Step(5 * TickRate);
        f.Probe(x, y).Stage.Should().Be(TreeGrowthStage.Young, "a planted sapling grows like any other");
        f.Step(21 * TickRate);
        f.Probe(x, y).Should().Be((false, TreeGrowthStage.Mature));
    }

    [Fact]
    public async Task EnqueueAttackInput_HoldingASapling_PlantsItOnTheAimedTile()
    {
        (int x, int y) = FindOpenGround();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, Saplings(1)).ConfigureAwait(true);
        Vector2 aim = new((x + 0.5f) * RegrowthWorldFixture.TileSize, (y + 0.5f) * RegrowthWorldFixture.TileSize);

        f.World.EnqueueAttackInput(RegrowthWorldFixture.Conn, 0UL, aim, abilitySlot: 0).Should().Be(OpStatus.Ok);
        f.Step(2);

        f.Probe(x, y).Should().Be((false, TreeGrowthStage.Sapling), "the primary action with a sapling plants it (the place flow)");
        HeldSaplings(f.World).Should().Be(0);
    }

    [Fact]
    public async Task TryPlantSapling_BeyondBuildReach_IsRefusedAndUsesNothingUp()
    {
        (int x, int y) = FindOpenGround();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, Saplings(3)).ConfigureAwait(true);

        f.World.TryPlantSapling(RegrowthWorldFixture.Conn, x + 30, y).Should().Be(OpStatus.OutOfRange, "planting has build reach");

        HeldSaplings(f.World).Should().Be(3, "a refused planting uses nothing up");
        f.Probe(x + 30, y).Stage.Should().Be(TreeGrowthStage.Mature, "nothing was planted out of reach");
    }

    [Fact]
    public async Task TryPlantSapling_OnWaterOrABoulder_IsRefusedWithItsReason()
    {
        (int wx, int wy) = FindTile((tile, _) => tile.Biome is BiomeType.ShallowWater, near: FindOpenGround());
        using (RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), wx, wy, Saplings(1)).ConfigureAwait(true))
        {
            f.World.TryPlantSapling(RegrowthWorldFixture.Conn, wx, wy).Should().Be(OpStatus.Invalid, "nothing is planted in water");
        }

        (int bx, int by) = FindTile((_, prop) => prop is { Type: MapPropType.Boulder }, near: FindOpenGround());
        using (RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), bx, by, Saplings(1)).ConfigureAwait(true))
        {
            f.World.TryPlantSapling(RegrowthWorldFixture.Conn, bx, by).Should().Be(OpStatus.Conflict, "a boulder already stands on the tile");
            HeldSaplings(f.World).Should().Be(1);
        }
    }

    [Fact]
    public async Task TryPlantSapling_OnABlueprintOrWithoutASapling_IsRefused()
    {
        (int x, int y) = FindOpenGround();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, Saplings(2)).ConfigureAwait(true);

        f.World.TrySelectHotbarSlot(RegrowthWorldFixture.Conn, 1).Should().Be(OpStatus.Ok);
        f.World.TryPlantSapling(RegrowthWorldFixture.Conn, x, y).Should().Be(OpStatus.NotFound, "the player holds no sapling");

        f.World.TrySelectHotbarSlot(RegrowthWorldFixture.Conn, 0).Should().Be(OpStatus.Ok);
        f.World.TryPlaceStructure(RegrowthWorldFixture.Conn, x, y, StructureType.WoodFloor).Status.Should().Be(OpStatus.Ok);
        f.World.TryPlantSapling(RegrowthWorldFixture.Conn, x, y).Should().Be(OpStatus.Conflict, "a construction site is on the tile");
        HeldSaplings(f.World).Should().Be(2);
    }

    [Fact]
    public async Task InitializeFromStoreAsync_PlantedSapling_IsStillThereAfterARestart()
    {
        (int x, int y) = FindOpenGround();
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, Saplings(1)).ConfigureAwait(true);
        f.World.TryPlantSapling(RegrowthWorldFixture.Conn, x, y).Should().Be(OpStatus.Ok);
        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);

        AuthoritativeWorld restarted = f.Restart();
        await restarted.InitializeFromStoreAsync().ConfigureAwait(true);

        RegrowthWorldFixture.IsPlanted(restarted, x, y).Should().BeTrue("the planted tree is saved with its chunk (format 6)");
        RegrowthWorldFixture.Probe(restarted, x, y).Should().Be((false, TreeGrowthStage.Sapling), "with its growth stage");
    }

    private static ItemSlot Saplings(int count) => new(s_saplingId, count);

    private static int HeldSaplings(AuthoritativeWorld world)
    {
        List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
        world.GetInventorySnapshot(RegrowthWorldFixture.Conn, slots).Should().BeTrue();
        int count = 0;
        foreach ((ushort _, ItemSlot slot) in slots)
        {
            if (slot.ItemId == s_saplingId)
            {
                count += slot.Quantity;
            }
        }

        return count;
    }

    private static (int X, int Y) FindOpenGround() =>
        FindTile((tile, prop) => prop is null && !tile.IsSolid && tile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
            BiomePropTables.TryGetPlantedTree(tile.Biome, out _), near: null);

    private static (int X, int Y) FindTile(Func<MapTile, MapProp?, bool> match, (int X, int Y)? near)
    {
        ChunkWorld2D mirror = new(new ProceduralWorldGenerator(RegrowthWorldFixture.Seed), RegrowthWorldFixture.TileSize, 0.0008f);
        (int cx, int cy) = near ?? mirror.SpawnTile;
        for (int r = near is null ? 12 : 0; r <= 120; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    int x = cx + dx;
                    int y = cy + dy;
                    Chunk2D chunk = mirror.GetOrCreateChunk(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y));
                    int lx = Chunk2D.ToLocalCoord(x);
                    int ly = Chunk2D.ToLocalCoord(y);
                    MapProp? prop = mirror.TryGetPropAt(x, y, out MapProp p) ? p : null;
                    if (!chunk.IsRoad(lx, ly) && !chunk.IsBridge(lx, ly) && !mirror.Buildings.TryGetStructure(x, y, out _) &&
                        match(chunk.GetTile(lx, ly), prop))
                    {
                        return (x, y);
                    }
                }
            }
        }

        throw new InvalidOperationException("the test seed has no such tile near spawn");
    }
}
