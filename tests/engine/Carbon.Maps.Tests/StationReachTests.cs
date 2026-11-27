// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Building;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class StationReachTests
{
    private const float TileSize = 32f;

    private static readonly Vector2 s_originTileCentre = new(0.5f * TileSize, 0.5f * TileSize);

    [Theory]
    [InlineData(5, 0, true)]
    [InlineData(-5, 5, true)]
    [InlineData(6, 0, false)]
    [InlineData(0, -6, false)]
    public void IsNearStructure_StationTilesAway_CountsWithinTheSharedReach(int dx, int dy, bool expected)
    {
        BuildingGrid grid = new();
        grid.ForcePlaceStructure(dx, dy, StructureType.ForgeAnvil);

        StationReach.IsNearStructure(grid, s_originTileCentre, TileSize, StructureType.ForgeAnvil, StructureType.ForgeAnvil, StationReach.StructureTiles)
            .Should().Be(expected);
    }

    [Fact]
    public void IsNearStructure_EitherType_Counts()
    {
        BuildingGrid grid = new();
        grid.ForcePlaceStructure(2, 0, StructureType.Campfire);

        StationReach.IsNearStructure(grid, s_originTileCentre, TileSize, StructureType.Stove, StructureType.Campfire, StationReach.StructureTiles)
            .Should().BeTrue();
        StationReach.IsNearStructure(grid, s_originTileCentre, TileSize, StructureType.Workbench, StructureType.Workbench, StationReach.StructureTiles)
            .Should().BeFalse("a campfire is not a workbench");
    }

    [Fact]
    public void IsNearStructure_DestroyedStation_DoesNotCount()
    {
        BuildingGrid grid = new();
        grid.ForcePlaceStructure(1, 0, StructureType.ForgeAnvil);
        grid.ApplyDamage(1, 0, float.MaxValue, out StructureTile wreck).Should().BeTrue();
        wreck.IsDestroyed.Should().BeTrue();

        StationReach.IsNearStructure(grid, s_originTileCentre, TileSize, StructureType.ForgeAnvil, StructureType.ForgeAnvil, StationReach.StructureTiles)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNearStructure_NegativePosition_MeasuresFromTheFlooredTile()
    {
        BuildingGrid grid = new();
        grid.ForcePlaceStructure(-6, 0, StructureType.Workbench);

        // x = -0.5 tile lies in tile -1, so tile -6 is 5 tiles away (truncation would make it 6).
        StationReach.IsNearStructure(grid, new Vector2(-0.5f * TileSize, 0f), TileSize, StructureType.Workbench, StructureType.Workbench, StationReach.StructureTiles)
            .Should().BeTrue();
    }

    [Fact]
    public void Reach_IsFiveTilesAnd175Px()
    {
        StationReach.StructureTiles.Should().Be(5, "the server's crafting reach the client must predict (audit DUP-3)");
        StationReach.NpcPx.Should().Be(175f);
    }
}
