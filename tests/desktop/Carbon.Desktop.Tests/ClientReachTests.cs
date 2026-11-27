// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Desktop.Networking.Session;
using Carbon.Maps.Building;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The client predicts reach exactly as the server checks it: a tool swing reaches the aimed tile when the tile's
/// centre is within <c>ToolReach.Px</c> (52 px) of the player, and a forge anvil counts within
/// <c>StationReach.StructureTiles</c> (5) tiles, and a placement reaches when the aimed tile's centre is within
/// <c>placementReachTiles</c> of the player.
/// </summary>
public sealed class ClientReachTests
{
    private const float TileSize = 32f;

    [Theory]
    [InlineData(28f, 16f, 90f, 20f, true)] // tile (2,0)'s centre (80,16) is 52 px away: the server strikes it
    [InlineData(27.5f, 16f, 90f, 20f, false)] // 52.5 px
    [InlineData(16f, 16f, 70f, 16f, false)] // aim point 54 px away but tile (2,0)'s centre 64 px: the server refuses it
    [InlineData(28f, 16f, 95f, 30f, true)] // aim point 68 px away, tile centre 52 px: the server strikes it
    public void IsToolStrikeInReach_AimedTile_MatchesTheServersTileCentreReach(float px, float py, float ax, float ay, bool expected) =>
        MultiplayerSession.IsToolStrikeInReach(new Vector2(px, py), new Vector2(ax, ay), TileSize).Should().Be(expected);

    [Theory]
    [InlineData(16f, 16f, 140f, 20f, true)] // tile (4,0)'s centre (144,16) is 128 px = 4 tiles away: the edge of the reach
    [InlineData(15.5f, 16f, 140f, 20f, false)] // 128.5 px
    [InlineData(16f, 16f, 145f, 31f, true)] // aim point 129.9 px away but the tile centre 128 px: the server places it
    [InlineData(10f, 16f, 132f, 16f, false)] // aim point 122 px away but tile (4,0)'s centre 134 px: the server refuses it
    public void IsPlacementInReach_AimedTile_MatchesTheServersTileCentreReach(float px, float py, float ax, float ay, bool expected) =>
        MultiplayerSession.IsPlacementInReach(new Vector2(px, py), new Vector2(ax, ay), TileSize, reachTiles: 4f).Should().Be(expected);

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void IsAnvilInReach_AnvilTilesAway_CountsWithinTheServersFiveTiles(int tilesAway, bool expected)
    {
        BuildingGrid grid = new();
        grid.ForcePlaceStructure(tilesAway, 0, StructureType.ForgeAnvil);

        MultiplayerSession.IsAnvilInReach(grid, new Vector2(0.5f * TileSize, 0.5f * TileSize), TileSize).Should().Be(expected);
    }

    [Fact]
    public void IsAnvilInReach_Workbench_DoesNotCount()
    {
        BuildingGrid grid = new();
        grid.ForcePlaceStructure(1, 0, StructureType.Workbench);

        MultiplayerSession.IsAnvilInReach(grid, new Vector2(0.5f * TileSize, 0.5f * TileSize), TileSize).Should().BeFalse();
    }
}
