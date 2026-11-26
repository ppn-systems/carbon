// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Items.Containers;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class PlayerRegistryTests
{
    private const ulong Connection = 5UL;
    private const ulong NetworkId = 500UL;

    [Fact]
    public void TryGetByEntity_RegisteredPlayer_FindsItThroughItsNetworkIdentity()
    {
        WorldSimulation simulation = new();
        PlayerRegistry registry = new();
        PlayerState player = Register(registry, simulation.SpawnNetworkEntity(NetworkId, Vector2.Zero, 7f));

        registry.TryGetByEntity(simulation, player.Entity, out PlayerState? found).Should().BeTrue();
        found.Should().BeSameAs(player);
        registry.TryGetByNetworkId(NetworkId, out PlayerState? byNetId).Should().BeTrue();
        byNetId.Should().BeSameAs(player);
    }

    [Fact]
    public void TryGetByEntity_EntityWithoutAPlayer_Fails()
    {
        WorldSimulation simulation = new();
        PlayerRegistry registry = new();
        _ = Register(registry, simulation.SpawnNetworkEntity(NetworkId, Vector2.Zero, 7f));
        Entity mob = simulation.SpawnNetworkEntity(NetworkId + 1UL, Vector2.Zero, 7f);

        registry.TryGetByEntity(simulation, mob, out PlayerState? found).Should().BeFalse();
        found.Should().BeNull();
        registry.TryGetByNetworkId(999UL, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetByEntity_StaleEntityHandleOfAReplacedPlayer_Fails()
    {
        WorldSimulation simulation = new();
        PlayerRegistry registry = new();
        Entity old = simulation.SpawnNetworkEntity(NetworkId, Vector2.Zero, 7f);
        _ = Register(registry, old);
        simulation.DespawnEntity(old);
        _ = Register(registry, simulation.SpawnNetworkEntity(NetworkId, Vector2.Zero, 7f));

        registry.TryGetByEntity(simulation, old, out _).Should().BeFalse("only the player's current entity resolves");
    }

    private static PlayerState Register(PlayerRegistry registry, Entity entity)
    {
        ConnectedPlayerSession session = new(1UL, Connection, Guid.NewGuid(), Guid.NewGuid(), "Tester", NetworkId, 0, 0);
        CharacterRecord character = new(
            session.CharacterId, session.AccountId, "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
        PlayerState state = new(session, entity, character, new InventoryContainer(4));
        registry.ByConnection[Connection] = state;
        registry.ConnectionByNetworkId[NetworkId] = Connection;
        return state;
    }
}
