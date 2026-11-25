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
using Carbon.Contracts.Entities;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
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

    [Fact]
    public async Task AuthoritativeCombatCraftingAndInventoryPersistence_ShouldWorkEndToEnd()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-combat-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteCharacterStore characters = new(factory);
            SqliteChunkDeltaRepository chunks = new(factory);
            RecordingOutboundSink sink = new();

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer seededBag = new(27);
            StarterGameCatalog.GrantStarterLoadout(seededBag);
            _ = seededBag.TryAdd(new ItemSlot(StarterGameCatalog.WoodLog, 8), 99);
            byte[] bagBytes = InventoryBinaryCodec.Serialize(seededBag);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            CharacterRecord scout = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Scout",
                1,
                0,
                world.DefaultSpawnWorldPosition.X,
                world.DefaultSpawnWorldPosition.Y,
                100f,
                100f,
                2,
                bagBytes,
                now);

            world.TryEnterWorld(connectionId: 101UL, sessionId: 1UL, scout).Status.Should().Be(OpStatus.Ok);
            world.TryCraftRecipe(101UL, recipeId: 1, quantity: 1).Should().Be(OpStatus.Ok);
            sink.SlotChanges.Should().NotBeEmpty();

            List<(ushort SlotIndex, ItemSlot Slot)> invSnapshot = [];
            world.GetInventorySnapshot(101UL, invSnapshot).Should().BeTrue();
            invSnapshot.Should().Contain(s => s.Slot.ItemId == StarterGameCatalog.WoodWallKit && s.Slot.Quantity == 8);

            // Step to populate & stream AoI around world spawn (or enter near wilderness monster chunk).
            world.StepFixedTick();

            // Enter a hunter near chunk (2, 0) where deterministic monsters spawn until one enters AoI.
            EntitySpawnSnapshot? mobSpawn = null;
            for (int probeX = 1; probeX <= 6 && mobSpawn is null; probeX++)
            {
                float wx = ((probeX * 32) + 16) * 32f;
                float wy = 16f * 32f;
                CharacterRecord hunter = scout with { Id = Guid.NewGuid(), PositionX = wx, PositionY = wy };
                world.TryEnterWorld(connectionId: 303UL, sessionId: 3UL, hunter).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();

                for (int i = 0; i < sink.Spawns.Count; i++)
                {
                    if (sink.Spawns[i].ConnectionId == 303UL && sink.Spawns[i].Snapshot.Kind == EntityKind.Monster)
                    {
                        mobSpawn = sink.Spawns[i].Snapshot;
                        break;
                    }
                }
            }

            mobSpawn.Should().NotBeNull("wilderness chunks should deterministically populate monsters");

            // Spawn melee slayer right next to the discovered monster and strike with IronSword (slot 2).
            CharacterRecord slayer = scout with
            {
                Id = Guid.NewGuid(),
                PositionX = mobSpawn!.Value.PositionX + 16f,
                PositionY = mobSpawn.Value.PositionY,
            };
            world.TryEnterWorld(connectionId: 404UL, sessionId: 4UL, slayer).Status.Should().Be(OpStatus.Ok);
            world.EnqueueAttackInput(404UL, mobSpawn.Value.NetworkId, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 2);
            world.StepFixedTick();

            sink.CombatHits.Should().Contain(h => h.DefenderNetworkId == mobSpawn.Value.NetworkId && h.Damage > 0f);
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
        public List<(ulong AttackerNetworkId, ulong DefenderNetworkId, float Damage, bool IsKilled)> CombatHits { get; } = [];
        public List<(ulong ConnectionId, ushort SlotIndex, uint ItemId, int Quantity)> SlotChanges { get; } = [];

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

        public void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled) =>
            this.CombatHits.Add((attackerNetworkId, defenderNetworkId, damage, isKilled));

        public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability) =>
            this.SlotChanges.Add((connectionId, slotIndex, itemId, quantity));
    }
}
