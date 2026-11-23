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
}
