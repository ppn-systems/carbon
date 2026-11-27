// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Numerics;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Entities;
using Carbon.Simulation.ECS;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class AoiReplicatorTests
{
    private const ulong Connection = 11UL;
    private const ulong ObserverNetId = 100UL;
    private const ulong MobNetId = 200UL;

    [Fact]
    public void ReplicateTo_EntityEntersView_StagesItsSpawnWithTheResolvedIdentity()
    {
        Harness h = new();
        _ = h.Simulation.SpawnNetworkEntity(MobNetId, new Vector2(50f, 0f), 9f);

        _ = h.Replicate();

        OutboundSpawn spawn = h.Outbound.Pending.Spawns.Should().ContainSingle().Subject;
        spawn.ConnectionId.Should().Be(Connection);
        spawn.Snapshot.NetworkId.Should().Be(MobNetId);
        spawn.Snapshot.Kind.Should().Be(EntityKind.Monster);
        spawn.Snapshot.Name.Should().Be("Slime");
        spawn.Snapshot.PositionX.Should().Be(50f);
        spawn.Snapshot.Radius.Should().Be(9f);
        h.Outbound.Pending.Despawns.Should().BeEmpty();
    }

    [Fact]
    public void ReplicateTo_DirtyVisibleEntity_SendsADeltaBatchAndNothingOnceFlagsAreCleared()
    {
        Harness h = new();
        Entity mob = h.Simulation.SpawnNetworkEntity(MobNetId, new Vector2(50f, 0f), 9f);
        _ = h.Replicate();
        h.Replication.ClearDirtyFlags();
        h.Outbound.Swap();

        h.Simulation.MarkDirty(mob, EntityDirtyMask.Health);
        _ = h.Replicate();

        OutboundDelta delta = h.Outbound.Pending.Deltas.Should().ContainSingle().Subject;
        delta.Count.Should().Be(1);
        h.Outbound.Pending.DeltaEntries[delta.Offset].NetworkId.Should().Be(MobNetId);

        h.Replication.ClearDirtyFlags();
        h.Outbound.Swap();
        EntityDirtyMask selfDirty = h.Replicate();
        selfDirty.Should().Be(EntityDirtyMask.None);
        h.Outbound.Pending.Deltas.Should().BeEmpty("nothing changed since the flags were cleared");
    }

    [Fact]
    public void ReplicateTo_VisibleEntityDespawned_StagesItsDespawn()
    {
        Harness h = new();
        Entity mob = h.Simulation.SpawnNetworkEntity(MobNetId, new Vector2(50f, 0f), 9f);
        _ = h.Replicate();
        h.Outbound.Swap();

        h.Simulation.DespawnEntity(mob);
        _ = h.Replicate();

        h.Outbound.Pending.Despawns.Should().ContainSingle()
            .Which.Should().Be(new OutboundDespawn(Connection, MobNetId, 7UL));
    }

    [Fact]
    public void Track_AgainAfterReconnect_RespawnsEverythingInView()
    {
        Harness h = new();
        _ = h.Simulation.SpawnNetworkEntity(MobNetId, new Vector2(50f, 0f), 9f);
        _ = h.Replicate();
        h.Outbound.Swap();

        h.Replication.Track(Connection);
        _ = h.Replicate();

        h.Outbound.Pending.Spawns.Should().ContainSingle(s => s.Snapshot.NetworkId == MobNetId);
    }

    [Fact]
    public void Dispatch_TwoObserversFarApart_EachReceivesOnlyTheDeltasOfEntitiesInItsOwnView()
    {
        // Both observers are replicated into the same tick buffer, so their delta entries share one list and each
        // batch is a slice of it. A wrong offset or count would hand observer B the entities observer A sees.
        WorldSimulation simulation = new();
        OutboundTickBuffer outbound = new();
        AoiReplicator replication = new(simulation, outbound, viewRadius: 768f, defaultRadius: 7f, _ => SpawnIdentity.Unknown);
        const ulong connA = 21UL, connB = 22UL, observerA = 300UL, observerB = 301UL, mobNearA = 400UL, mobNearB = 401UL;
        Vector2 posA = Vector2.Zero;
        Vector2 posB = new(5_000f, 0f);
        Entity a = simulation.SpawnNetworkEntity(observerA, posA, 7f);
        Entity b = simulation.SpawnNetworkEntity(observerB, posB, 7f);
        Entity mobA = simulation.SpawnNetworkEntity(mobNearA, posA + new Vector2(50f, 0f), 9f);
        Entity mobB = simulation.SpawnNetworkEntity(mobNearB, posB + new Vector2(50f, 0f), 9f);
        replication.Track(connA);
        replication.Track(connB);
        ReplicateBoth();
        replication.ClearDirtyFlags();
        outbound.Swap();

        simulation.MarkDirty(mobA, EntityDirtyMask.Health);
        simulation.MarkDirty(mobB, EntityDirtyMask.Health);
        ReplicateBoth();
        outbound.Swap();
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        outbound.Dispatch(sink);

        sink.DeltaEntityIds.Where(d => d.ConnectionId == connA).Select(d => d.NetworkId).Should().Equal([mobNearA], "A sees only the mob next to it");
        sink.DeltaEntityIds.Where(d => d.ConnectionId == connB).Select(d => d.NetworkId).Should().Equal([mobNearB], "B must not receive A's view");

        void ReplicateBoth()
        {
            _ = replication.ReplicateTo(connA, observerA, a, posA, lastClientTick: 1U, serverTick: 9UL);
            _ = replication.ReplicateTo(connB, observerB, b, posB, lastClientTick: 1U, serverTick: 9UL);
        }
    }

    [Fact]
    public void TryBuildSpawnSnapshot_UnknownNetworkId_Fails()
    {
        Harness h = new();

        h.Replication.TryBuildSpawnSnapshot(999UL, out _).Should().BeFalse();
    }

    private sealed class Harness
    {
        public Harness()
        {
            this.Replication = new AoiReplicator(
                this.Simulation,
                this.Outbound,
                viewRadius: 768f,
                defaultRadius: 7f,
                netId => netId == MobNetId
                    ? SpawnIdentity.Unknown with { Kind = EntityKind.Monster, Name = "Slime" }
                    : SpawnIdentity.Unknown);
            this.Observer = this.Simulation.SpawnNetworkEntity(ObserverNetId, Vector2.Zero, 7f);
            this.Replication.Track(Connection);
        }

        public WorldSimulation Simulation { get; } = new();

        public OutboundTickBuffer Outbound { get; } = new();

        public AoiReplicator Replication { get; }

        public Entity Observer { get; }

        public EntityDirtyMask Replicate() =>
            this.Replication.ReplicateTo(Connection, ObserverNetId, this.Observer, Vector2.Zero, lastClientTick: 3U, serverTick: 7UL);
    }
}
