// Copyright (c) 2026 PPN Systems. All rights reserved.

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
