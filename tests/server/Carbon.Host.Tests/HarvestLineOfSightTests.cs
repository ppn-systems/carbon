// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Maps.Props;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Audit SA-8: the harvest key reaches a prop up to 4 tiles away (<c>structures.json</c> placement reach), so it must not
/// reach one behind a wall.
/// </summary>
public sealed class HarvestLineOfSightTests
{
    [Fact]
    public async Task TryHarvestProp_AWallBetweenThePlayerAndTheProp_StillStrikes()
    {
        // Characterisation: nothing checks the line of sight, so the stones are struck through the wall.
        (int x, int y) = RegrowthWorldFixture.FindProp(p => p.Type == MapPropType.LooseStones, outsideClearing: false);
        using RegrowthWorldFixture f = await RegrowthWorldFixture.CreateAsync(RegrowthWorldFixture.Rules(), x, y, ItemSlot.Empty).ConfigureAwait(true);
        StandTilesEastOf(f.World, x, y, tiles: 3);
        f.World.Chunks.Buildings.ForcePlaceStructure(x + 2, y, StructureType.StoneWall);

        f.World.TryHarvestProp(RegrowthWorldFixture.Conn, x, y).Status.Should().Be(OpStatus.Ok, "the wall does not stop the strike");
    }

    // Moves the fixture's player to the centre of the tile `tiles` east of the prop, on its row.
    internal static void StandTilesEastOf(AuthoritativeWorld world, int x, int y, int tiles)
    {
        world.TryGetSessionByConnection(RegrowthWorldFixture.Conn, out ConnectedPlayerSession? session).Should().BeTrue();
        world.Simulation.TryGetEntityByNetworkId(session!.PlayerNetworkId, out Entity player).Should().BeTrue();
        Vector2 at = new((x + tiles + 0.5f) * RegrowthWorldFixture.TileSize, (y + 0.5f) * RegrowthWorldFixture.TileSize);
        world.Simulation.Entities.SetTransform(player, new Transform2D(at));
    }
}
