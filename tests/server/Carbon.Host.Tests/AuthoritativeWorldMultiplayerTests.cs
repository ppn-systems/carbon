// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Maps.Building;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class AuthoritativeWorldMultiplayerTests
{
    [Fact]
    public async Task TwoPlayersEnteringSteppingBuildingAndLeaving_ShouldSyncAoiAndPersistDeltas()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-world-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteCharacterStore characters = new(factory);
            SqliteChunkDeltaRepository chunks = new(factory);
            RecordingOutboundSink sink = new();

            AuthoritativeWorld world = new(
                worldSeed: 1337UL,
                tileSize: 32f,
                frequency: 0.0008f,
                tickRate: 30,
                chunks,
                characters,
                sink);

            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            CharacterRecord charA = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "PlayerA",
                1,
                0,
                world.DefaultSpawnWorldPosition.X,
                world.DefaultSpawnWorldPosition.Y,
                100f,
                100f,
                0,
                [],
                now);
            CharacterRecord charB = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "PlayerB",
                1,
                1,
                world.DefaultSpawnWorldPosition.X + 64f,
                world.DefaultSpawnWorldPosition.Y,
                100f,
                100f,
                0,
                [],
                now);

            EnterWorldResult enterA = world.TryEnterWorld(connectionId: 101UL, sessionId: 1UL, charA);
            EnterWorldResult enterB = world.TryEnterWorld(connectionId: 202UL, sessionId: 2UL, charB);

            enterA.Status.Should().Be(OpStatus.Ok);
            enterB.Status.Should().Be(OpStatus.Ok);

            world.EnqueueMoveInput(101UL, clientTick: 1U, new Vector2(1f, 0f), sprint: true);
            world.StepFixedTick();

            sink.Spawns.Should().Contain(s => s.ConnectionId == 101UL && s.Snapshot.NetworkId == enterB.PlayerNetworkId);
            sink.Spawns.Should().Contain(s => s.ConnectionId == 202UL && s.Snapshot.NetworkId == enterA.PlayerNetworkId);
            sink.DeltaBatches.Should().Contain(d => d.ConnectionId == 101UL && d.LastProcessedClientTick == 1U);

            int buildTileX = (int)MathF.Floor(enterA.PositionX / 32f) + 2;
            int buildTileY = (int)MathF.Floor(enterA.PositionY / 32f);
            WorldMutationOutcome placeOutcome = world.TryPlaceStructure(101UL, buildTileX, buildTileY, StructureType.WoodWall);
            placeOutcome.Status.Should().Be(OpStatus.Ok);
            placeOutcome.Revision.Should().BeGreaterThan(0U);
            sink.StructureChanges.Should().NotBeEmpty();

            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);
            IReadOnlyList<PersistedChunkDelta> savedDeltas = await chunks.LoadAllAsync().ConfigureAwait(true);
            savedDeltas.Should().NotBeEmpty();

            world.LeaveWorld(202UL).Should().BeTrue();
            world.StepFixedTick();
            sink.Despawns.Should().Contain(d => d.ConnectionId == 101UL && d.NetworkId == enterB.PlayerNetworkId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private sealed class RecordingOutboundSink : IWorldOutboundSink
    {
        public List<(ulong ConnectionId, EntitySpawnSnapshot Snapshot)> Spawns { get; } = [];
        public List<(ulong ConnectionId, ulong NetworkId)> Despawns { get; } = [];
        public List<(ulong ConnectionId, uint LastProcessedClientTick)> DeltaBatches { get; } = [];
        public List<(int TileX, int TileY, byte StructureType)> StructureChanges { get; } = [];

        public void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn) =>
            this.Spawns.Add((connectionId, spawn));

        public void SendEntityDespawn(ulong connectionId, ulong networkId, ulong serverTick) =>
            this.Despawns.Add((connectionId, networkId));

        public void SendEntityDeltaBatch(
            ulong connectionId,
            ulong serverTick,
            uint lastProcessedClientTick,
            float selfX,
            float selfY,
            ReadOnlySpan<EntityDeltaEntry> entries) =>
            this.DeltaBatches.Add((connectionId, lastProcessedClientTick));

        public void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, uint revision, float durability, bool isDestroyed)
        {
        }

        public void SendStructureChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, float durability, byte doorState, byte connectivity) =>
            this.StructureChanges.Add((tileX, tileY, structureType));
    }
}
