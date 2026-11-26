// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Economy;
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
            InventoryContainer builderBag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(builderBag);
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
                InventoryBinaryCodec.Serialize(builderBag),
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

            // Player B stands two tiles east of A, so that tile is rejected (entities block walls); find a free one.
            int playerTileX = (int)MathF.Floor(enterA.PositionX / 32f);
            int playerTileY = (int)MathF.Floor(enterA.PositionY / 32f);
            world.TryPlaceStructure(101UL, playerTileX + 2, playerTileY, StructureType.WoodWall).Status
                .Should().Be(OpStatus.Conflict, "player B occupies that tile");

            WorldMutationOutcome placeOutcome = default;
            int buildTileX = 0;
            int buildTileY = 0;
            foreach ((int dx, int dy) in new[] { (0, 2), (0, -2), (-2, 0), (2, 2), (-2, -2), (2, -2), (-2, 2), (1, 2), (-1, -2) })
            {
                buildTileX = playerTileX + dx;
                buildTileY = playerTileY + dy;
                placeOutcome = world.TryPlaceStructure(101UL, buildTileX, buildTileY, StructureType.WoodWall);
                if (placeOutcome.Status == OpStatus.Ok)
                {
                    break;
                }
            }

            placeOutcome.Status.Should().Be(OpStatus.Ok, "a blueprint is placed and its 3 wood logs delivered");
            placeOutcome.Revision.Should().BeGreaterThan(0U);
            sink.StructureChanges.Should().NotContain(c => c.TileX == buildTileX && c.TileY == buildTileY, "a blueprint is not a structure yet");

            // 120 work at 60 work/s and 30 ticks/s = 60 ticks.
            for (int tick = 0; tick < 70; tick++)
            {
                world.StepFixedTick();
            }

            sink.StructureChanges.Should().Contain(c => c.TileX == buildTileX && c.TileY == buildTileY && c.StructureType == (byte)StructureType.WoodWall);

            // Clients saw the frame, throttled progress steps, and the site's removal once the wall completed.
            sink.ConstructionChanges.Should().Contain(c => c.TileX == buildTileX && c.TileY == buildTileY && c.Stage == (byte)ConstructionStage.Frame && !c.IsDeconstruction);
            sink.ConstructionChanges.Should().Contain(c => c.TileX == buildTileX && c.Progress > 0.4f && c.Progress < 0.6f && !c.IsDeconstruction);
            sink.ConstructionChanges.Count(c => c.TileX == buildTileX && c.TileY == buildTileY && !c.IsDeconstruction)
                .Should().BeLessThan(20, "progress is pushed in 10% steps, not every tick");
            sink.ConstructionChanges.Should().Contain(c => c.TileX == buildTileX && c.TileY == buildTileY && c.Stage == (byte)ConstructionStage.Removed && !c.IsDeconstruction);

            // Player B does not own the wall and may not deconstruct it.
            world.TryDemolishStructure(202UL, buildTileX, buildTileY).Status.Should().Be(OpStatus.Forbidden);

            // The owner deconstructs it over 30 ticks (50% of 120 work) and gets 75% of 3 logs (2) back: 18 - 3 + 2.
            world.TryDemolishStructure(101UL, buildTileX, buildTileY).Status.Should().Be(OpStatus.Ok);
            for (int tick = 0; tick < 40; tick++)
            {
                world.StepFixedTick();
            }

            sink.StructureChanges.Should().Contain(c => c.TileX == buildTileX && c.TileY == buildTileY && c.StructureType == 0);
            sink.ConstructionChanges.Should().Contain(c => c.TileX == buildTileX && c.IsDeconstruction && c.Stage == (byte)ConstructionStage.Complete && c.Progress > 0f);
            sink.ConstructionChanges.Should().Contain(c => c.TileX == buildTileX && c.IsDeconstruction && c.Stage == (byte)ConstructionStage.Removed);
            List<(ushort SlotIndex, ItemSlot Slot)> builderSlots = [];
            world.GetInventorySnapshot(101UL, builderSlots).Should().BeTrue();
            builderSlots.Where(x => x.Slot.ItemId == StarterGameCatalog.WoodLog).Sum(x => x.Slot.Quantity).Should().Be(17);

            // Rebuild it so the persisted delta below carries a structure.
            world.TryPlaceStructure(101UL, buildTileX, buildTileY, StructureType.WoodWall).Status.Should().Be(OpStatus.Ok);

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

            // Unequip starter LeatherTunic from ChestSlot (37) -> empty backpack slot 12, then re-equip 12 -> 37
            int equipsBefore = sink.EquipmentChanges.Count;
            const ushort emptyBackpackSlot = PlayerInventoryLayout.BackpackStart + 3;
            world.TryMoveInventorySlot(404UL, PlayerInventoryLayout.ChestSlot, emptyBackpackSlot).Should().Be(OpStatus.Ok);
            world.TryMoveInventorySlot(404UL, emptyBackpackSlot, PlayerInventoryLayout.ChestSlot).Should().Be(OpStatus.Ok);
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

            // Rank-0 skill must be rejected (not free!), and starter characters without ClassAdvanced cannot unlock class skills.
            world.EnqueueAttackInput(404UL, 0UL, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), AbilityInputEncoding.EncodeSkill(SkillCatalog.WhirlwindId)).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.SkillCasts.Should().BeEmpty("unlearned rank-0 skills cannot be cast for free");
            world.TryAllocateCharacterPoint(404UL, PointAllocationKind.Skill, SkillCatalog.WhirlwindId, 1).Should().Be(OpStatus.Forbidden);

            // An advanced Vanguard character can unlock Vanguard skills, while another class's skill stays forbidden.
            CharacterRecord vanguardAdv = scout with
            {
                Id = Guid.NewGuid(),
                Name = "vanguard_adv",
                Class = HeroArchetypeClass.Vanguard,
                Flags = CharacterTraits.ClassAdvanced,
                PositionX = mobSpawn.Value.PositionX + 40f,
                PositionY = mobSpawn.Value.PositionY,
            };
            EnterWorldResult advEnter = world.TryEnterWorld(connectionId: 405UL, sessionId: 5UL, vanguardAdv);
            advEnter.Status.Should().Be(OpStatus.Ok);

            world.TryAllocateCharacterPoint(405UL, PointAllocationKind.Skill, SkillCatalog.ArcaneNovaId, 1).Should().Be(OpStatus.Forbidden);
            world.TryAllocateCharacterPoint(405UL, PointAllocationKind.Skill, SkillCatalog.WhirlwindId, 1).Should().Be(OpStatus.Ok);
            world.EnqueueAttackInput(405UL, 0UL, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), AbilityInputEncoding.EncodeSkill(SkillCatalog.WhirlwindId)).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.SkillCasts.Should().Contain(c => c.CasterNetworkId == advEnter.PlayerNetworkId && c.AbilitySlot == SkillCatalog.WhirlwindId);

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
            // No kit needed any more: the blueprint takes 4 of the hero's 50 stone chunks.
            world.TryPlaceStructure(701UL, wallTx, wallTy, StructureType.StoneWall).Status.Should().Be(OpStatus.Ok);
            world.TryDemolishStructure(701UL, wallTx, wallTy).Status.Should().Be(OpStatus.Ok, "cancelling the blueprint refunds the stone");
            List<(ushort SlotIndex, ItemSlot Slot)> refunded = [];
            world.GetInventorySnapshot(701UL, refunded).Should().BeTrue();
            refunded.Where(s => s.Slot.ItemId == StarterGameCatalog.StoneChunk).Sum(s => s.Slot.Quantity).Should().Be(50);

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

    [Fact]
    public async Task RangedBowAndStaffCombat_And_StationBoundForging_ShouldWorkEndToEnd()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-ranged-test-{Guid.NewGuid():N}.db");
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

            // 1. Discover a wilderness monster away from starter sanctuary.
            EntitySpawnSnapshot? mobSpawn = null;
            InventoryContainer scoutInv = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(scoutInv, startingClass: 1);
            scoutInv.SetSlot(3, new ItemSlot(StarterGameCatalog.ArcaneStaff, 1, 0UL, 190f));
            scoutInv.SetSlot(4, new ItemSlot(StarterGameCatalog.ForgeAnvilKit, 2));
            scoutInv.SetSlot(9, new ItemSlot(StarterGameCatalog.IronOre, 30));
            scoutInv.SetSlot(10, new ItemSlot(StarterGameCatalog.ManaCrystal, 30));
            scoutInv.SetSlot(11, new ItemSlot(StarterGameCatalog.StoneChunk, 30));
            byte[] rangerBytes = InventoryBinaryCodec.Serialize(scoutInv);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ulong hunterConn = 0UL;
            float hunterX = 0f;
            float hunterY = 0f;
            for (int probeX = 2; probeX <= 7 && mobSpawn is null; probeX++)
            {
                hunterX = ((probeX * 32) + 16) * 32f;
                hunterY = 16f * 32f;
                ulong cid = (ulong)(900 + probeX);
                CharacterRecord rec = new(Guid.NewGuid(), Guid.NewGuid(), $"Ranger{probeX}", 1, 0, hunterX, hunterY, 120f, 120f, 2, rangerBytes, now, CurrentMana: 110f, MaxMana: 110f);
                world.TryEnterWorld(cid, cid, rec).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();

                for (int i = 0; i < sink.Spawns.Count; i++)
                {
                    if (sink.Spawns[i].ConnectionId == cid && sink.Spawns[i].Snapshot.Kind == EntityKind.Monster)
                    {
                        mobSpawn = sink.Spawns[i].Snapshot;
                        hunterConn = cid;
                        break;
                    }
                }
            }

            mobSpawn.Should().NotBeNull();

            // 2. Station-bound Forging: trying to enhance in open wilderness without NPC/Anvil must return OutOfRange,
            //    then placing StructureType.ForgeAnvil nearby unlocks OpStatus.Ok enhancement!
            world.TryUpgradeInventoryItem(hunterConn, slotIndex: 2).Should().Be(OpStatus.OutOfRange);

            int anvilTx = (int)MathF.Floor(hunterX / 32f) + 2;
            int anvilTy = (int)MathF.Floor(hunterY / 32f);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    WorldMutationOutcome tryAnvil = world.TryPlaceStructure(hunterConn, (int)MathF.Floor(hunterX / 32f) + dx, (int)MathF.Floor(hunterY / 32f) + dy, StructureType.ForgeAnvil);
                    if (tryAnvil.Status == OpStatus.Ok)
                    {
                        dx = 99;
                        break;
                    }
                }
            }

            // The anvil is only a blueprint (10 stone + 6 iron ore delivered, 300 work = 150 ticks) until it is built.
            world.TryUpgradeInventoryItem(hunterConn, slotIndex: 2).Should().Be(OpStatus.OutOfRange);

            // 3. Fire HuntingBow projectile from ~120px away towards discovered monster and step ticks until swept projectile hits.
            Vector2 shotOrigin = new(mobSpawn!.Value.PositionX - 110f, mobSpawn.Value.PositionY);
            CharacterRecord sniper = new(Guid.NewGuid(), Guid.NewGuid(), "Sniper", 1, 0, shotOrigin.X, shotOrigin.Y, 120f, 120f, 2, rangerBytes, now, CurrentMana: 110f, MaxMana: 110f);
            world.TryEnterWorld(950UL, 950UL, sniper).Status.Should().Be(OpStatus.Ok);
            world.EnqueueAttackInput(950UL, 0UL, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 2).Should().Be(OpStatus.Ok);
            for (int t = 0; t < 16; t++)
            {
                world.StepFixedTick();
            }

            sink.Spawns.Should().Contain(s => s.Snapshot.Kind == EntityKind.Projectile && s.Snapshot.ArchetypeId == 1);
            sink.CombatHits.Should().Contain(h => h.DefenderNetworkId == mobSpawn.Value.NetworkId && h.Damage > 0f);

            // 4. Fire ArcaneStaff Mana Bolt (slot 3 -> ArchetypeId 2) & confirm projectile spawns.
            int hitsBeforeStaff = sink.CombatHits.Count;
            world.TrySelectHotbarSlot(950UL, 3).Should().Be(OpStatus.Ok);
            for (int w = 0; w < 20; w++)
            {
                world.StepFixedTick();
            }

            world.EnqueueAttackInput(950UL, 0UL, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 3).Should().Be(OpStatus.Ok);
            for (int t = 0; t < 16; t++)
            {
                world.StepFixedTick();
            }

            sink.Spawns.Should().Contain(s => s.Snapshot.Kind == EntityKind.Projectile && s.Snapshot.ArchetypeId == 2);
            sink.CombatHits.Count.Should().BeGreaterThan(hitsBeforeStaff);

            // The hunter kept building the anvil during the fight; finish the remaining work, then forge.
            for (int t = 0; t < 120; t++)
            {
                world.StepFixedTick();
            }

            world.TryUpgradeInventoryItem(hunterConn, slotIndex: 2, deterministicRollSeed: 2U).Should().Be(OpStatus.Ok);
            _ = anvilTx;
            _ = anvilTy;
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
    public async Task StrictCombatReachAndPointAllocation_ShouldEnforceShortRangeAndPersistPoints()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-reach-ap-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            SqliteChunkDeltaRepository chunks = new(factory);
            RecordingOutboundSink sink = new();

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Guid accountId = Guid.NewGuid();
            AccountRecord account = new(accountId, "tester", "TESTER", [1, 2, 3], [4, 5, 6], now, false);
            (await accounts.TryCreateAsync(account).ConfigureAwait(true)).Should().BeTrue();

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(inv, startingClass: 0);
            byte[] invBytes = InventoryBinaryCodec.Serialize(inv);

            CharacterRecord hero = new(
                Guid.NewGuid(),
                accountId,
                "ReachTester",
                Level: 3,
                AppearancePreset: 0,
                PositionX: world.DefaultSpawnWorldPosition.X,
                PositionY: world.DefaultSpawnWorldPosition.Y,
                CurrentHealth: 120f,
                MaxHealth: 120f,
                SelectedHotbarSlot: 2,
                InventoryBlob: invBytes,
                UpdatedAtUtc: now,
                Class: HeroArchetypeClass.Arcanist,
                Flags: CharacterTraits.StarterLoadoutGranted | CharacterTraits.ClassAdvanced);
            (await characters.TryCreateAsync(hero).ConfigureAwait(true)).Should().BeTrue();

            world.TryEnterWorld(connectionId: 601UL, sessionId: 601UL, hero).Status.Should().Be(OpStatus.Ok);

            int startTx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f);
            int startTy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            world.TryHarvestProp(601UL, startTx + 5, startTy).Status.Should().Be(OpStatus.OutOfRange);
            world.TrySelectHotbarSlot(601UL, 9).Should().Be(OpStatus.Invalid);
            world.EnqueueMoveInput(601UL, 99U, new Vector2(float.NaN, 0f), sprint: false);
            world.StepFixedTick();
            sink.DeltaBatches.Should().NotContain(d => d.ConnectionId == 601UL && d.LastProcessedClientTick == 99U);
            world.EnqueueAttackInput(601UL, 0UL, new Vector2(float.PositiveInfinity, 0f), 2).Should().Be(OpStatus.Invalid);

            world.TryAllocateCharacterPoint(601UL, PointAllocationKind.Attribute, (ushort)PrimaryAttributeId.Vitality, 2).Should().Be(OpStatus.Ok);
            world.TryAllocateCharacterPoint(601UL, PointAllocationKind.Skill, SkillCatalog.ArcaneNovaId, 2).Should().Be(OpStatus.Ok);
            sink.StatsSyncs.Should().Contain(s => s.ConnectionId == 601UL && s.VitPoints == 2);

            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);
            CharacterRecord? reloaded = await characters.FindByIdAsync(hero.Id).ConfigureAwait(true);
            reloaded.Should().NotBeNull();
            reloaded!.Class.Should().Be(HeroArchetypeClass.Arcanist);
            reloaded.Flags.Should().HaveFlag(CharacterTraits.StarterLoadoutGranted);
            CharacterSkillMaskHelper.HasSkill(reloaded.UnlockedSkills, SkillCatalog.ArcaneNovaId).Should().BeTrue();

            CharacterProgressionState reloadedProg = new();
            CharacterProgressionBinaryCodec.TryDeserializeInto(reloaded.ProgressionBlob ?? [], reloadedProg).Should().BeTrue();
            reloadedProg.GetAttribute(PrimaryAttributeId.Vitality).Should().Be(2);
            reloadedProg.GetSkillRank(SkillCatalog.ArcaneNovaId).Should().Be(2);

            EntitySpawnSnapshot? mobSpawn = null;
            for (int probeX = 2; probeX <= 7 && mobSpawn is null; probeX++)
            {
                float wx = ((probeX * 32) + 16) * 32f;
                float wy = 16f * 32f;
                CharacterRecord scout = hero with { Id = Guid.NewGuid(), Name = $"Scout{probeX}", PositionX = wx, PositionY = wy };
                world.TryEnterWorld((ulong)(610 + probeX), (ulong)(610 + probeX), scout).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();

                for (int i = 0; i < sink.Spawns.Count; i++)
                {
                    if (sink.Spawns[i].ConnectionId == (ulong)(610 + probeX) && sink.Spawns[i].Snapshot.Kind == EntityKind.Monster)
                    {
                        mobSpawn = sink.Spawns[i].Snapshot;
                        break;
                    }
                }
            }

            mobSpawn.Should().NotBeNull();

            // Too far for melee strike (120px > MeleeStrikeRangePx = 56px)
            CharacterRecord farStriker = hero with { Id = Guid.NewGuid(), Name = "FarStriker", PositionX = mobSpawn!.Value.PositionX + 120f, PositionY = mobSpawn.Value.PositionY };
            world.TryEnterWorld(602UL, 602UL, farStriker).Status.Should().Be(OpStatus.Ok);
            int hitsBeforeFar = sink.CombatHits.Count;
            world.EnqueueAttackInput(602UL, mobSpawn.Value.NetworkId, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 2).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.CombatHits.Skip(hitsBeforeFar).Should().NotContain(h => h.DefenderNetworkId == mobSpawn.Value.NetworkId);

            // Within strict melee range (36px <= MeleeStrikeRangePx = 56px)
            CharacterRecord closeStriker = hero with { Id = Guid.NewGuid(), Name = "CloseStriker", PositionX = mobSpawn.Value.PositionX + 36f, PositionY = mobSpawn.Value.PositionY };
            world.TryEnterWorld(603UL, 603UL, closeStriker).Status.Should().Be(OpStatus.Ok);
            int hitsBeforeClose = sink.CombatHits.Count;
            world.EnqueueAttackInput(603UL, mobSpawn.Value.NetworkId, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 2).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.CombatHits.Skip(hitsBeforeClose).Should().Contain(h => h.DefenderNetworkId == mobSpawn.Value.NetworkId && h.Damage > 0f);
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
    public async Task MonsterKill_WithEconomyConfig_DropsThemedLootFromLootTables()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-economy-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();
            string configDir = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!;
            configDir.Should().NotBeNull("the host ships resources/config next to its binaries");
            GameEconomy economy = GameEconomy.LoadFromDirectory(configDir);
            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, new SqliteChunkDeltaRepository(factory), new SqliteCharacterStore(factory), sink, economy: economy);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(bag);
            CharacterRecord template = new(
                Guid.NewGuid(), Guid.NewGuid(), "Hunter", 1, 0, 0f, 0f, 100f, 100f, 2, InventoryBinaryCodec.Serialize(bag), DateTimeOffset.UtcNow);

            // Probe wilderness chunks until a monster backed by a loot table enters AoI.
            EntitySpawnSnapshot? mob = null;
            for (int probeX = 1; probeX <= 12 && mob is null; probeX++)
            {
                ulong probeConn = 700UL + (ulong)probeX;
                world.TryEnterWorld(probeConn, probeConn, template with { Id = Guid.NewGuid(), PositionX = ((probeX * 32) + 16) * 32f, PositionY = 16f * 32f }).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();
                mob = sink.Spawns
                    .Where(x => x.ConnectionId == probeConn && x.Snapshot.Kind == EntityKind.Monster && AuthoritativeWorld.ResolveLootTableId(x.Snapshot.ArchetypeId) is not null)
                    .Select(x => (EntitySpawnSnapshot?)x.Snapshot)
                    .FirstOrDefault();
            }

            mob.Should().NotBeNull("wilderness chunks should populate a slime/skeleton/orc/demon");
            economy.Loot.TryGet(AuthoritativeWorld.ResolveLootTableId(mob!.Value.ArchetypeId)!, out Carbon.Economy.Loot.LootTable? table).Should().BeTrue();

            const ulong slayerConn = 800UL;
            world.TryEnterWorld(slayerConn, slayerConn, template with { Id = Guid.NewGuid(), PositionX = mob.Value.PositionX + 16f, PositionY = mob.Value.PositionY }).Status.Should().Be(OpStatus.Ok);
            int killTickSpawnIndex = -1;
            for (int tick = 0; tick < 900 && killTickSpawnIndex < 0; tick++)
            {
                world.EnqueueAttackInput(slayerConn, mob.Value.NetworkId, new Vector2(mob.Value.PositionX, mob.Value.PositionY), 2);
                int before = sink.Spawns.Count;
                world.StepFixedTick();
                if (sink.CombatHits.Any(h => h.DefenderNetworkId == mob.Value.NetworkId && h.IsKilled))
                {
                    killTickSpawnIndex = before;
                }
            }

            killTickSpawnIndex.Should().BeGreaterThanOrEqualTo(0, "the slayer should eventually kill the monster");
            world.StepFixedTick();

            List<uint> droppedItems =
            [
                .. sink.Spawns
                    .Skip(killTickSpawnIndex)
                    .Where(x => x.ConnectionId == slayerConn && x.Snapshot.Kind == EntityKind.LootDrop)
                    .Select(x => (uint)x.Snapshot.ArchetypeId)
                    .Distinct(),
            ];
            droppedItems.Should().NotBeEmpty("loot tables always include a guaranteed drop");
            foreach (uint itemId in droppedItems)
            {
                economy.Items.TryGet(itemId, out Carbon.Items.Definitions.ItemCatalogEntry? item).Should().BeTrue($"dropped item {itemId} must exist in items.json");
                item!.Tags.Should().Contain(t => t == table!.Theme || t == "generic", $"{table!.Id} dropped '{item.Code}'");
            }

            world.TryGetWalletBalances(slayerConn, out long gold, out long diamonds).Should().BeTrue();
            gold.Should().BeGreaterThanOrEqualTo(0);
            diamonds.Should().BeGreaterThanOrEqualTo(0);
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
        public List<(ulong ConnectionId, ushort Level, uint Experience, float Defense, ushort VitPoints, byte Skill1Rank)> StatsSyncs { get; } = [];
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

        public List<(int TileX, int TileY, byte Stage, float Progress, bool IsDeconstruction)> ConstructionChanges { get; } = [];

        public void SendConstructionChanged(
            ReadOnlySpan<ulong> recipientConnectionIds,
            int tileX,
            int tileY,
            byte structureType,
            byte rotation,
            byte stage,
            byte size,
            float progress,
            bool isDeconstruction) =>
            this.ConstructionChanges.Add((tileX, tileY, stage, progress, isDeconstruction));

        public void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled) =>
            this.CombatHits.Add((attackerNetworkId, defenderNetworkId, damage, isKilled));

        public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0) =>
            this.SlotChanges.Add((connectionId, slotIndex, itemId, quantity));

        public void SendEntityEquipmentChanged(ReadOnlySpan<ulong> recipientConnectionIds, ulong entityNetworkId, ushort headId, ushort chestId, ushort legsId, ushort feetId, ushort weaponId, byte weaponUpgrade, ushort level, float maxHealth = 0f) =>
            this.EquipmentChanges.Add((entityNetworkId, chestId, weaponId, level));

        public void SendPlayerStatsSync(ulong connectionId, in PlayerStatsSnapshot snapshot)
        {
            CharacterProgressionState prog = new();
            _ = CharacterProgressionBinaryCodec.TryDeserializeInto(snapshot.ProgressionBlob ?? [], prog);
            this.StatsSyncs.Add((
                connectionId,
                snapshot.Level,
                snapshot.Experience,
                snapshot.Defense,
                prog.GetAttribute(PrimaryAttributeId.Vitality),
                prog.GetSkillRank(SkillCatalog.WhirlwindId)));
        }

        public void SendSkillCastBroadcast(ReadOnlySpan<ulong> recipientConnectionIds, ulong casterNetworkId, byte abilitySlot, float originX, float originY, float aimX, float aimY) =>
            this.SkillCasts.Add((casterNetworkId, abilitySlot));
    }
}
