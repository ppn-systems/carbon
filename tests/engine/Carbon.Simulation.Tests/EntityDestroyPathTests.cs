// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Reflection;
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
    public void EntityManager_DestroyEntity_IsNotPublic()
    {
        typeof(EntityManager).GetMethod(nameof(EntityManager.DestroyEntity), BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull("only WorldSimulation.DespawnEntity may destroy a world entity");
    }

    [Fact]
    public void DespawnEntity_OnANetworkedEntity_ClearsItsGridEntryAndNetworkIdAndPublishesTheDespawn()
    {
        WorldSimulation world = new();
        Entity entity = world.SpawnNetworkEntity(77UL, new Vector2(50f, 50f));

        world.DespawnEntity(entity);

        world.SpatialGrid.EntityCount.Should().Be(0);
        world.ReadEvents<EntityDespawnedEvent>().ToArray().Should().ContainSingle().Which.Should().Be(new EntityDespawnedEvent(entity, 77UL));
        world.TryGetEntityByNetworkId(77UL, out _).Should().BeFalse();
        Entity next = world.SpawnEntity(new Vector2(500f, 500f));
        world.BindNetworkIdentity(next, 77UL);
        world.TryGetEntityByNetworkId(77UL, out Entity bound).Should().BeTrue();
        bound.Should().Be(next);
    }
}
