// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Pins what <c>AuthoritativeWorld.TryEnterWorld</c> binds now that the duplicate session-id argument is gone (audit
/// round 2, DOM-6): a session is identified by its transport connection, and it points at the entity that was spawned.
/// </summary>
public sealed class EnterWorldSessionBindingTests : IDisposable
{
    private static readonly DateTimeOffset s_createdAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TickAllocationHarness _harness = TickAllocationHarness.Create(players: 0);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void TryEnterWorld_BindsTheSessionToItsConnectionAndSpawnedEntity()
    {
        CharacterRecord hero = this.Hero("Aria");

        EnterWorldResult result = _harness.World.TryEnterWorld(5_001UL, hero);

        result.Status.Should().Be(OpStatus.Ok);
        result.PlayerNetworkId.Should().NotBe(0UL);
        _harness.World.TryGetSessionByConnection(5_001UL, out ConnectedPlayerSession? session).Should().BeTrue();
        session!.ConnectionId.Should().Be(5_001UL);
        session.AccountId.Should().Be(hero.AccountId);
        session.CharacterId.Should().Be(hero.Id);
        session.CharacterName.Should().Be("Aria");
        session.PlayerNetworkId.Should().Be(result.PlayerNetworkId);
    }

    [Fact]
    public void TryEnterWorld_TwoConnections_GetTheirOwnSessionsAndEntities()
    {
        EnterWorldResult first = _harness.World.TryEnterWorld(6_001UL, this.Hero("One"));
        EnterWorldResult second = _harness.World.TryEnterWorld(6_002UL, this.Hero("Two"));

        first.PlayerNetworkId.Should().NotBe(second.PlayerNetworkId);
        _harness.World.TryGetSessionByConnection(6_001UL, out ConnectedPlayerSession? one).Should().BeTrue();
        _harness.World.TryGetSessionByConnection(6_002UL, out ConnectedPlayerSession? two).Should().BeTrue();
        one!.CharacterName.Should().Be("One");
        one.PlayerNetworkId.Should().Be(first.PlayerNetworkId);
        two!.CharacterName.Should().Be("Two");
        two.PlayerNetworkId.Should().Be(second.PlayerNetworkId);
    }

    [Fact]
    public void TryEnterWorld_SameConnectionAgain_ReplacesItsSessionWithANewEntity()
    {
        EnterWorldResult first = _harness.World.TryEnterWorld(7_001UL, this.Hero("Before"));
        CharacterRecord after = this.Hero("After");

        EnterWorldResult second = _harness.World.TryEnterWorld(7_001UL, after);

        second.Status.Should().Be(OpStatus.Ok);
        second.PlayerNetworkId.Should().NotBe(first.PlayerNetworkId);
        _harness.World.TryGetSessionByConnection(7_001UL, out ConnectedPlayerSession? session).Should().BeTrue();
        session!.CharacterId.Should().Be(after.Id);
        session.PlayerNetworkId.Should().Be(second.PlayerNetworkId);
    }

    [Fact]
    public void TryEnterWorld_SameConnectionAgain_DespawnsThePreviousEntityForObservers()
    {
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        using TickAllocationHarness observed = TickAllocationHarness.Create(players: 0, sink);
        _ = observed.World.TryEnterWorld(9_001UL, this.Hero("Observer"));
        EnterWorldResult first = observed.World.TryEnterWorld(9_002UL, this.Hero("Before"));
        observed.World.StepFixedTick();

        _ = observed.World.TryEnterWorld(9_002UL, this.Hero("After"));
        observed.World.StepFixedTick();

        sink.Despawns.Should().Contain((9_001UL, first.PlayerNetworkId), "the replaced entity leaves the world, it is not leaked");
    }

    [Fact]
    public void LeaveWorld_RemovesTheSessionForThatConnectionOnly()
    {
        _ = _harness.World.TryEnterWorld(8_001UL, this.Hero("Stays"));
        _ = _harness.World.TryEnterWorld(8_002UL, this.Hero("Leaves"));

        _harness.World.LeaveWorld(8_002UL).Should().BeTrue();

        _harness.World.TryGetSessionByConnection(8_002UL, out _).Should().BeFalse();
        _harness.World.TryGetSessionByConnection(8_001UL, out _).Should().BeTrue();
    }

    private CharacterRecord Hero(string name)
    {
        System.Numerics.Vector2 spawn = _harness.World.DefaultSpawnWorldPosition;
        return new CharacterRecord(Guid.NewGuid(), Guid.NewGuid(), name, 1, default, spawn.X, spawn.Y, 100f, 100f, 0, [], s_createdAt);
    }
}
