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

            InventoryContainer seededBag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(seededBag);
            _ = PlayerInventoryLayout.TryAddBagItem(seededBag, new ItemSlot(StarterGameCatalog.WoodLog, 8), 99);
            _ = PlayerInventoryLayout.TryAddBagItem(seededBag, new ItemSlot(StarterGameCatalog.StoneChunk, 6), 99);
            _ = PlayerInventoryLayout.TryAddBagItem(seededBag, new ItemSlot(StarterGameCatalog.IronOre, 4), 99);
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

            List<(ushort SlotIndex, ItemSlot Slot)> slayerInv = [];
            world.GetInventorySnapshot(404UL, slayerInv).Should().BeTrue();
            slayerInv.Should().Contain(s =>
                s.Slot.ItemId == StarterGameCatalog.IronSword &&
                s.Slot.Durability < 200f &&
                s.Slot.Durability > 0f);

            // Equip starter LeatherTunic from backpack slot 9 -> Chest armor slot 37
            int equipsBefore = sink.EquipmentChanges.Count;
            world.TryMoveInventorySlot(404UL, PlayerInventoryLayout.BackpackStart, PlayerInventoryLayout.ChestSlot).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.EquipmentChanges.Count.Should().BeGreaterThan(equipsBefore);
            sink.StatsSyncs.Should().Contain(s => s.ConnectionId == 404UL && s.Defense > 3f);

            // Drop WoodAxe from slot 0 (which lands on ground as EntityKind.LootDrop) & pick it back up via EnqueuePickupLoot
            int spawnsBeforeDrop = sink.Spawns.Count;
            world.TryDropInventoryItem(404UL, 0, 1).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            EntitySpawnSnapshot? droppedLoot = null;
            for (int i = spawnsBeforeDrop; i < sink.Spawns.Count; i++)
            {
                if (sink.Spawns[i].ConnectionId == 404UL && sink.Spawns[i].Snapshot.Kind == EntityKind.LootDrop)
                {
                    droppedLoot = sink.Spawns[i].Snapshot;
                    break;
                }
            }

            droppedLoot.Should().NotBeNull();
            world.EnqueuePickupLoot(404UL, droppedLoot!.Value.NetworkId).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            slayerInv.Clear();
            world.GetInventorySnapshot(404UL, slayerInv).Should().BeTrue();
            slayerInv.Should().Contain(s => s.Slot.ItemId == StarterGameCatalog.WoodAxe);

            // Cast Whirlwind skill (AbilitySlot = 101)
            world.EnqueueAttackInput(404UL, 0UL, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 101).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.SkillCasts.Should().Contain(c => c.AbilitySlot == 1);

            // Forge upgrade +0 -> +1 on IronSword (slot 2) using mats in scout inventory (101UL)
            world.TryUpgradeInventoryItem(101UL, 2).Should().Be(OpStatus.Ok);
            invSnapshot.Clear();
            world.GetInventorySnapshot(101UL, invSnapshot).Should().BeTrue();
            invSnapshot.Should().Contain(s =>
                s.Slot.ItemId == StarterGameCatalog.IronSword &&
                ItemEnhancementPolicy.GetUpgradeLevel(in s.Slot) == 1);
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
    public async Task AuthoritativeHardening_MissingKit_EnhancementFailed_And_SessionEviction_ShouldBeEnforced()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-hardening-test-{Guid.NewGuid():N}.db");
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

            InventoryContainer seeded = new(PlayerInventoryLayout.TotalCapacity);
            seeded.SetSlot(0, new ItemSlot(StarterGameCatalog.IronSword, 1, 0UL, 200f, UpgradeLevel: 3));
            seeded.SetSlot(9, new ItemSlot(StarterGameCatalog.IronOre, 50));
            seeded.SetSlot(10, new ItemSlot(StarterGameCatalog.ManaCrystal, 50));
            seeded.SetSlot(11, new ItemSlot(StarterGameCatalog.StoneChunk, 50));
            seeded.SetSlot(12, new ItemSlot(StarterGameCatalog.WoodLog, 50));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            CharacterRecord hero = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Hardener",
                1,
                0,
                world.DefaultSpawnWorldPosition.X,
                world.DefaultSpawnWorldPosition.Y,
                100f,
                100f,
                0,
                InventoryBinaryCodec.Serialize(seeded),
                now);

            world.TryEnterWorld(connectionId: 701UL, sessionId: 71UL, hero).Status.Should().Be(OpStatus.Ok);

            int wallTx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f) + 2;
            int wallTy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            world.TryPlaceStructure(701UL, wallTx, wallTy, StructureType.StoneWall).Status.Should().Be(OpStatus.Conflict);

            // Seed 1U rolls 0.969 (> 0.85 chance at +3 -> +4), deterministically consuming mats without upgrading.
            world.TryUpgradeInventoryItem(701UL, slotIndex: 0, deterministicRollSeed: 1U).Should().Be(OpStatus.EnhancementFailed);
            List<(ushort SlotIndex, ItemSlot Slot)> postFailInv = [];
            world.GetInventorySnapshot(701UL, postFailInv).Should().BeTrue();
            postFailInv.Should().Contain(s => s.SlotIndex == 0 && ItemEnhancementPolicy.GetUpgradeLevel(in s.Slot) == 3);

            // Seed 2U rolls 0.131 (<= 0.85), succeeding +3 -> +4.
            world.TryUpgradeInventoryItem(701UL, slotIndex: 0, deterministicRollSeed: 2U).Should().Be(OpStatus.Ok);
            postFailInv.Clear();
            world.GetInventorySnapshot(701UL, postFailInv).Should().BeTrue();
            postFailInv.Should().Contain(s => s.SlotIndex == 0 && ItemEnhancementPolicy.GetUpgradeLevel(in s.Slot) == 4);

            world.TryGetPendingCharacterSnapshot(hero.Id, out CharacterRecord? pending).Should().BeTrue();
            pending.Should().NotBeNull();

            // Re-entering with the same CharacterId evicts the previous live connection cleanly.
            world.TryEnterWorld(connectionId: 702UL, sessionId: 72UL, hero).Status.Should().Be(OpStatus.Ok);
            world.TryGetSessionByConnection(701UL, out _).Should().BeFalse();
            world.TryGetSessionByConnection(702UL, out _).Should().BeTrue();
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
        public List<(ulong EntityNetworkId, ushort ChestId, ushort WeaponId, ushort Level)> EquipmentChanges { get; } = [];
        public List<(ulong ConnectionId, ushort Level, uint Experience, float Defense)> StatsSyncs { get; } = [];
        public List<(ulong CasterNetworkId, byte AbilitySlot)> SkillCasts { get; } = [];

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

        public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0) =>
            this.SlotChanges.Add((connectionId, slotIndex, itemId, quantity));

        public void SendEntityEquipmentChanged(ReadOnlySpan<ulong> recipientConnectionIds, ulong entityNetworkId, ushort headId, ushort chestId, ushort legsId, ushort feetId, ushort weaponId, byte weaponUpgrade, ushort level, float maxHealth = 0f) =>
            this.EquipmentChanges.Add((entityNetworkId, chestId, weaponId, level));

        public void SendPlayerStatsSync(ulong connectionId, ushort level, uint experience, uint xpToNextLevel, float currentHealth, float maxHealth, float currentMana, float maxMana, float currentStamina, float maxStamina, float defense, float skill1Cd, float skill2Cd, float skill3Cd, bool leveledUp) =>
            this.StatsSyncs.Add((connectionId, level, experience, defense));

        public void SendSkillCastBroadcast(ReadOnlySpan<ulong> recipientConnectionIds, ulong casterNetworkId, byte abilitySlot, float originX, float originY, float aimX, float aimY) =>
            this.SkillCasts.Add((casterNetworkId, abilitySlot));
    }
}
