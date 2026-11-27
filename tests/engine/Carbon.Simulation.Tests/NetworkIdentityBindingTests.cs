// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

/// <summary>
/// Pins the network-identity binding rules of <see cref="WorldSimulation"/> whose signatures lost the unused owner-session
/// argument (audit round 2, DOM-6): one live entity per network id, no zero ids, and a rebind frees the old id.
/// </summary>
public sealed class NetworkIdentityBindingTests
{
    private readonly WorldSimulation _world = new(tickRate: 20, cellSize: 32f);

    [Fact]
    public void SpawnNetworkEntity_BindsTheIdBothWays()
    {
        Entity entity = _world.SpawnNetworkEntity(42UL, new Vector2(8f, 8f), radius: 3f);

        _world.TryGetNetworkIdentity(entity, out NetworkIdentity identity).Should().BeTrue();
        identity.Should().Be(new NetworkIdentity(42UL));
        _world.TryGetEntityByNetworkId(42UL, out Entity resolved).Should().BeTrue();
        resolved.Should().Be(entity);
    }

    [Fact]
    public void SpawnNetworkEntity_ZeroId_Throws()
    {
        Action act = () => _world.SpawnNetworkEntity(0UL, Vector2.Zero);

        _ = act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SpawnNetworkEntity_IdAlreadyLive_Throws()
    {
        _ = _world.SpawnNetworkEntity(7UL, Vector2.Zero);
        int alive = _world.Entities.EntityCount;

        Action act = () => _world.SpawnNetworkEntity(7UL, new Vector2(64f, 0f));

        _ = act.Should().Throw<InvalidOperationException>().WithMessage("*7*");
        _world.Entities.EntityCount.Should().Be(alive, "a rejected spawn must not leave an orphan entity behind");
    }

    [Fact]
    public void BindNetworkIdentity_ZeroId_Throws()
    {
        Entity entity = _world.SpawnEntity(Vector2.Zero);

        Action act = () => _world.BindNetworkIdentity(entity, 0UL);

        _ = act.Should().Throw<ArgumentOutOfRangeException>();
        _world.TryGetNetworkIdentity(entity, out _).Should().BeFalse();
    }

    [Fact]
    public void BindNetworkIdentity_DespawnedEntity_Throws()
    {
        Entity entity = _world.SpawnEntity(Vector2.Zero);
        _world.DespawnEntity(entity);

        Action act = () => _world.BindNetworkIdentity(entity, 5UL);

        _ = act.Should().Throw<InvalidOperationException>().WithMessage("*invalid or has been destroyed*");
        _world.TryGetEntityByNetworkId(5UL, out _).Should().BeFalse();
    }

    [Fact]
    public void BindNetworkIdentity_IdOwnedByAnotherEntity_ThrowsAndKeepsTheOwner()
    {
        Entity owner = _world.SpawnNetworkEntity(9UL, Vector2.Zero);
        Entity other = _world.SpawnEntity(new Vector2(64f, 0f));

        Action act = () => _world.BindNetworkIdentity(other, 9UL);

        _ = act.Should().Throw<InvalidOperationException>();
        _world.TryGetEntityByNetworkId(9UL, out Entity resolved).Should().BeTrue();
        resolved.Should().Be(owner);
        _world.TryGetNetworkIdentity(other, out _).Should().BeFalse();
    }

    [Fact]
    public void BindNetworkIdentity_NewIdForTheSameEntity_FreesTheOldId()
    {
        // The client re-binds its local player to the server's id on rejoin.
        Entity entity = _world.SpawnNetworkEntity(100UL, Vector2.Zero);

        _world.BindNetworkIdentity(entity, 200UL);

        _world.TryGetEntityByNetworkId(100UL, out _).Should().BeFalse();
        _world.TryGetEntityByNetworkId(200UL, out Entity resolved).Should().BeTrue();
        resolved.Should().Be(entity);
        _world.TryGetNetworkIdentity(entity, out NetworkIdentity identity).Should().BeTrue();
        identity.NetworkId.Should().Be(200UL);
        _ = _world.SpawnNetworkEntity(100UL, new Vector2(64f, 0f));
    }

    [Fact]
    public void BindNetworkIdentity_SameIdAgain_IsANoOp()
    {
        Entity entity = _world.SpawnNetworkEntity(300UL, Vector2.Zero);

        _world.BindNetworkIdentity(entity, 300UL);

        _world.TryGetEntityByNetworkId(300UL, out Entity resolved).Should().BeTrue();
        resolved.Should().Be(entity);
    }

    [Fact]
    public void DespawnEntity_FreesItsNetworkIdForReuse()
    {
        Entity entity = _world.SpawnNetworkEntity(400UL, Vector2.Zero);

        _world.DespawnEntity(entity);

        _world.TryGetEntityByNetworkId(400UL, out _).Should().BeFalse();
        Entity reused = _world.SpawnNetworkEntity(400UL, Vector2.Zero);
        _world.TryGetEntityByNetworkId(400UL, out Entity resolved).Should().BeTrue();
        resolved.Should().Be(reused);
    }
}
