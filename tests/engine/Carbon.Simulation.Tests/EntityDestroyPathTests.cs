// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Simulation.ECS;
using Carbon.Simulation.Events;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

/// <summary>
/// ECS-8: an entity of a world must leave it through <see cref="WorldSimulation.DespawnEntity"/>, which also clears its
/// spatial-grid entry and its network id and publishes <see cref="EntityDespawnedEvent"/>.
/// </summary>
public sealed class EntityDestroyPathTests
{
    [Fact]
    public void EntitiesDestroyEntity_OnAWorldEntity_LeavesItsGridEntryAndNetworkIdBehind()
    {
        WorldSimulation world = new();
        Entity entity = world.SpawnNetworkEntity(77UL, new Vector2(50f, 50f));

        world.Entities.DestroyEntity(entity).Should().BeTrue();

        // Characterisation: the public EntityManager path skips the world's bookkeeping.
        world.SpatialGrid.EntityCount.Should().Be(1, "the destroyed entity's grid entry stays");
        world.ReadEvents<EntityDespawnedEvent>().Length.Should().Be(0, "no despawn is published");
        Entity next = world.SpawnEntity(new Vector2(500f, 500f));
        Action rebind = () => world.BindNetworkIdentity(next, 77UL);
        rebind.Should().Throw<InvalidOperationException>("network id 77 is still bound to the dead entity");
    }
}
