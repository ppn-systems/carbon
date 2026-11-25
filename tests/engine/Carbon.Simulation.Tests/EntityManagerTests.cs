// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

public sealed class EntityManagerTests
{
    [Fact]
    public void CreateEntity_ShouldProduceUniqueValidGenerationalHandles()
    {
        EntityManager manager = new(64);

        Entity e1 = manager.CreateEntity();
        Entity e2 = manager.CreateEntity();

        e1.IsValid.Should().BeTrue();
        e2.IsValid.Should().BeTrue();
        e1.Id.Should().NotBe(e2.Id);

        manager.IsAlive(e1).Should().BeTrue();
        manager.IsAlive(e2).Should().BeTrue();
        manager.EntityCount.Should().Be(2);
    }

    [Fact]
    public void ComponentAccess_ShouldStoreAndMutateValues()
    {
        EntityManager manager = new(64);
        Entity entity = manager.CreateEntity();

        manager.SetTransform(entity, new Transform2D(100f, 200f));
        manager.SetVelocity(entity, new Velocity2D(50f, 0f, drag: 2f));
        manager.SetHealth(entity, new HealthComponent(100f, 100f));

        manager.HasTransform(entity).Should().BeTrue();
        manager.HasVelocity(entity).Should().BeTrue();
        manager.HasHealth(entity).Should().BeTrue();

        ref Transform2D transform = ref manager.GetTransform(entity);
        transform.Position.Should().Be(new Vector2(100f, 200f));

        transform.Position = new Vector2(300f, 400f);
        manager.GetTransform(entity).Position.Should().Be(new Vector2(300f, 400f));
    }

    [Fact]
    public void DestroyEntity_ShouldInvalidateHandleAndRecycleSlot()
    {
        EntityManager manager = new(64);
        Entity e1 = manager.CreateEntity();
        int slotId = e1.Id;
        int origGen = e1.Generation;

        bool destroyed = manager.DestroyEntity(e1);
        destroyed.Should().BeTrue();
        manager.IsAlive(e1).Should().BeFalse();
        manager.EntityCount.Should().Be(0);

        Entity e2 = manager.CreateEntity();
        e2.Id.Should().Be(slotId);
        e2.Generation.Should().Be(origGen + 1);

        Action actStale = () => manager.GetTransform(e1);
        actStale.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DestroyEntity_SwapBackRemoval_MaintainsDenseListConsistency()
    {
        EntityManager manager = new(64);
        Entity e1 = manager.CreateEntity();
        Entity e2 = manager.CreateEntity();
        Entity e3 = manager.CreateEntity();

        manager.EntityCount.Should().Be(3);
        manager.ActiveEntities.Should().Equal(e1, e2, e3);

        // Destroy middle entity: e3 will be swapped to index 1
        bool destroyed = manager.DestroyEntity(e2);
        destroyed.Should().BeTrue();

        manager.EntityCount.Should().Be(2);
        manager.ActiveEntities.Count.Should().Be(2);
        manager.IsAlive(e2).Should().BeFalse();
        manager.IsAlive(e1).Should().BeTrue();
        manager.IsAlive(e3).Should().BeTrue();

        // GetEntity and IsSlotAlive tests
        manager.IsSlotAlive(e1.Id).Should().BeTrue();
        manager.IsSlotAlive(e2.Id).Should().BeFalse();
        manager.IsSlotAlive(e3.Id).Should().BeTrue();

        manager.GetEntity(e1.Id).Should().Be(e1);
        manager.GetEntity(e2.Id).Should().Be(Entity.Null);
        manager.GetEntity(e3.Id).Should().Be(e3);
    }

    [Fact]
    public void GenericComponentPool_SetGetSpanAndDestroy_WorksAcrossArbitraryStructTypes()
    {
        EntityManager manager = new(initialCapacity: 2);
        Entity e1 = manager.CreateEntity();
        Entity e2 = manager.CreateEntity();
        Entity e3 = manager.CreateEntity(); // Triggers capacity growth beyond 2

        manager.SetComponent(e1, new CustomMana(50f, 100f));
        manager.SetComponent(e3, new CustomMana(25f, 80f));

        manager.HasComponent<CustomMana>(e1).Should().BeTrue();
        manager.HasComponent<CustomMana>(e2).Should().BeFalse();
        manager.HasComponent<CustomMana>(e3).Should().BeTrue();

        ref CustomMana mana1 = ref manager.GetComponent<CustomMana>(e1);
        mana1 = mana1 with { Current = 75f };
        manager.TryGetComponent(e1, out CustomMana read1).Should().BeTrue();
        read1.Current.Should().Be(75f);

        ComponentPool<CustomMana> pool = manager.GetPool<CustomMana>();
        pool.Count.Should().Be(2);
        pool.AsSpan().Length.Should().Be(2);

        // Removing a component leaves entity alive
        manager.RemoveComponent<CustomMana>(e1).Should().BeTrue();
        manager.HasComponent<CustomMana>(e1).Should().BeFalse();
        pool.Count.Should().Be(1);

        // Destroying an entity cleans up all custom component pools automatically
        manager.DestroyEntity(e3).Should().BeTrue();
        pool.Count.Should().Be(0);
    }

    private readonly record struct CustomMana(float Current, float Max);
}
