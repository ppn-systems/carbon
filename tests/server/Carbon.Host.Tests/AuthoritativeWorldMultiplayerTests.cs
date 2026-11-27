// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Identity;
using Carbon.Economy;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Serialization;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class AuthoritativeWorldMultiplayerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 23)]
    public async Task TryEnterWorld_PlayerInHeartlandNearSpawn_SeesWildSlimeWithinOneSecond(int offsetTilesX, int offsetTilesY)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-heartland-spawn-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            Vector2 position = world.DefaultSpawnWorldPosition + (new Vector2(offsetTilesX, offsetTilesY) * 32f);
            CharacterRecord hero = new(Guid.NewGuid(), Guid.NewGuid(), "Newcomer", 1, 0, position.X, position.Y, 100f, 100f, 0, [], DateTimeOffset.UtcNow);
            world.TryEnterWorld(connectionId: 77UL, hero).Status.Should().Be(OpStatus.Ok);

            // One second at 30 Hz: the entry scan must populate the heartland immediately, not after spawn intervals.
            for (int tick = 0; tick < 30; tick++)
            {
                world.StepFixedTick();
            }

            sink.Spawns.Should().Contain(
                s => s.ConnectionId == 77UL && s.Snapshot.Kind == EntityKind.Monster && s.Snapshot.ArchetypeId == WorldActorCatalog.WildSlime,
                "a new player in the heartland must see at least one slime right away");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

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
                sink,
                entropy: TestGameplayEntropy.Create(1337UL));

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

            EnterWorldResult enterA = world.TryEnterWorld(connectionId: 101UL, charA);
            EnterWorldResult enterB = world.TryEnterWorld(connectionId: 202UL, charB);

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

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink, entropy: TestGameplayEntropy.Create(1337UL));
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
                now,
                Gold: 5_000);

            world.TryEnterWorld(connectionId: 101UL, scout).Status.Should().Be(OpStatus.Ok);
            // recipes.json: planks are hand-crafted anywhere; walls need a built workbench nearby.
            world.ItemRules.RecipeCatalog.TryGetByCode("wood_plank", out CraftingRecipe? plankRecipe).Should().BeTrue();
            world.ItemRules.RecipeCatalog.TryGetByCode("wood_wall", out CraftingRecipe? wallRecipe).Should().BeTrue();
            world.TryCraftRecipe(101UL, recipeId: (ushort)plankRecipe!.Id, quantity: 2).Should().Be(OpStatus.Ok);
            sink.SlotChanges.Should().NotBeEmpty();
            world.TryCraftRecipe(101UL, recipeId: (ushort)wallRecipe!.Id, quantity: 1).Should().Be(OpStatus.OutOfRange, "wood walls are crafted at a workbench");
            world.TryCraftRecipe(101UL, recipeId: 9999, quantity: 1).Should().Be(OpStatus.NotFound);

            List<(ushort SlotIndex, ItemSlot Slot)> invSnapshot = [];
            world.GetInventorySnapshot(101UL, invSnapshot).Should().BeTrue();
            invSnapshot.Should().Contain(s => s.Slot.ItemId == plankRecipe.Output.ItemId && s.Slot.Quantity == 4, "two crafts of 1 log → 2 planks");

            // Step to populate & stream AoI around world spawn (or enter near wilderness monster chunk).
            world.StepFixedTick();

            // Enter a hunter near chunk (2, 0) where deterministic monsters spawn until one enters AoI.
            EntitySpawnSnapshot? mobSpawn = null;
            for (int probeX = 1; probeX <= 6 && mobSpawn is null; probeX++)
            {
                float wx = ((probeX * 32) + 16) * 32f;
                float wy = 16f * 32f;
                CharacterRecord hunter = scout with { Id = Guid.NewGuid(), PositionX = wx, PositionY = wy };
                world.TryEnterWorld(connectionId: 303UL, hunter).Status.Should().Be(OpStatus.Ok);
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
            world.TryEnterWorld(connectionId: 404UL, slayer).Status.Should().Be(OpStatus.Ok);
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
            EnterWorldResult advEnter = world.TryEnterWorld(connectionId: 405UL, vanguardAdv);
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

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink, entropy: TestGameplayEntropy.Create(1337UL));
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
                now,
                Gold: 5_000);

            world.TryEnterWorld(connectionId: 701UL, hero).Status.Should().Be(OpStatus.Ok);

            int wallTx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f) + 2;
            int wallTy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            // No kit needed any more: the blueprint takes 4 of the hero's 50 stone chunks.
            world.TryPlaceStructure(701UL, wallTx, wallTy, StructureType.StoneWall).Status.Should().Be(OpStatus.Ok);
            world.TryDemolishStructure(701UL, wallTx, wallTy).Status.Should().Be(OpStatus.Ok, "cancelling the blueprint refunds the stone");
            List<(ushort SlotIndex, ItemSlot Slot)> refunded = [];
            world.GetInventorySnapshot(701UL, refunded).Should().BeTrue();
            refunded.Where(s => s.Slot.ItemId == StarterGameCatalog.StoneChunk).Sum(s => s.Slot.Quantity).Should().Be(50);

            // +3 -> +4 is a safe level (enhancement.json): a failed roll spends the costs but keeps +3. Fixed seeds keep it deterministic.
            List<(ushort SlotIndex, ItemSlot Slot)> postFailInv = [];
            OpStatus attempt = OpStatus.EnhancementFailed;
            for (uint seed = 1U; seed < 64U && attempt == OpStatus.EnhancementFailed; seed++)
            {
                attempt = world.TryUpgradeInventoryItem(701UL, slotIndex: 0, deterministicRollSeed: seed);
                if (attempt == OpStatus.EnhancementFailed)
                {
                    postFailInv.Clear();
                    world.GetInventorySnapshot(701UL, postFailInv).Should().BeTrue();
                    postFailInv.Should().Contain(s => s.SlotIndex == 0 && ItemEnhancementPolicy.GetUpgradeLevel(in s.Slot) == 3, "a safe-level failure never downgrades");
                }
            }

            attempt.Should().Be(OpStatus.Ok, "an 80% roll succeeds within a few fixed seeds");
            postFailInv.Clear();
            world.GetInventorySnapshot(701UL, postFailInv).Should().BeTrue();
            postFailInv.Should().Contain(s => s.SlotIndex == 0 && ItemEnhancementPolicy.GetUpgradeLevel(in s.Slot) == 4);

            world.TryGetPendingCharacterSnapshot(hero.Id, out CharacterRecord? pending).Should().BeTrue();
            pending.Should().NotBeNull();

            // Re-entering with the same CharacterId evicts the previous live connection cleanly.
            world.TryEnterWorld(connectionId: 702UL, hero).Status.Should().Be(OpStatus.Ok);
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

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink, entropy: TestGameplayEntropy.Create(1337UL));
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
                CharacterRecord rec = new(Guid.NewGuid(), Guid.NewGuid(), $"Ranger{probeX}", 1, 0, hunterX, hunterY, 120f, 120f, 2, rangerBytes, now, CurrentMana: 110f, MaxMana: 110f, Gold: 5_000);
                world.TryEnterWorld(cid, rec).Status.Should().Be(OpStatus.Ok);
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
            CharacterRecord sniper = new(Guid.NewGuid(), Guid.NewGuid(), "Sniper", 1, 0, shotOrigin.X, shotOrigin.Y, 120f, 120f, 2, rangerBytes, now, CurrentMana: 110f, MaxMana: 110f, Gold: 5_000);
            world.TryEnterWorld(950UL, sniper).Status.Should().Be(OpStatus.Ok);
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
    public async Task TryHarvestProp_RepeatedWithinAttackCooldown_IsRateLimitedUntilCooldownElapses()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-harvest-rate-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            SqliteChunkDeltaRepository chunks = new(factory);
            RecordingOutboundSink sink = new();

            DateTimeOffset now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
            Guid accountId = Guid.NewGuid();
            AccountRecord account = new(accountId, "harvester", "HARVESTER", [1, 2, 3], [4, 5, 6], now, false);
            (await accounts.TryCreateAsync(account).ConfigureAwait(true)).Should().BeTrue();

            const ulong seed = 1337UL;
            AuthoritativeWorld world = new(seed, 32f, 0.0008f, 30, chunks, characters, sink, entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            // Mirror the server's deterministic generator to find a destructible prop near spawn.
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(seed), 32f, 0.0008f);
            int spawnTx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f);
            int spawnTy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            (int X, int Y)? target = null;
            for (int r = 0; r <= 64 && target is null; r++)
            {
                for (int dy = -r; dy <= r && target is null; dy++)
                {
                    for (int dx = -r; dx <= r && target is null; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r &&
                            mirror.TryGetPropAt(spawnTx + dx, spawnTy + dy, out MapProp prop) &&
                            prop.Destructibility.IsDestructible && !prop.Destructibility.IsDestroyed)
                        {
                            target = (spawnTx + dx, spawnTy + dy);
                        }
                    }
                }
            }

            target.Should().NotBeNull("the seeded world should have a destructible prop near spawn");
            (int tx, int ty) = target!.Value;

            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(inv, startingClass: 0);
            CharacterRecord hero = new(
                Guid.NewGuid(),
                accountId,
                "Harvester",
                Level: 1,
                AppearancePreset: 0,
                PositionX: ((tx + 0.5f) * 32f) + 40f,
                PositionY: (ty + 0.5f) * 32f,
                CurrentHealth: 100f,
                MaxHealth: 100f,
                SelectedHotbarSlot: 0,
                InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: now,
                Flags: CharacterTraits.StarterLoadoutGranted);
            (await characters.TryCreateAsync(hero).ConfigureAwait(true)).Should().BeTrue();
            world.TryEnterWorld(connectionId: 701UL, hero).Status.Should().Be(OpStatus.Ok);

            OpStatus first = world.TryHarvestProp(701UL, tx, ty).Status;
            OpStatus second = world.TryHarvestProp(701UL, tx, ty).Status;

            first.Should().NotBe(OpStatus.RateLimited, "the first strike is within the interact range and off cooldown");
            first.Should().NotBe(OpStatus.OutOfRange);
            second.Should().Be(OpStatus.RateLimited, "a second strike in the same tick must wait for the shared attack cooldown");

            for (int i = 0; i < 90; i++)
            {
                world.StepFixedTick();
            }

            world.TryHarvestProp(701UL, tx, ty).Status.Should().NotBe(OpStatus.RateLimited, "the cooldown has elapsed after 3 seconds");
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
    public async Task TryGetPendingCharacterSnapshot_WhileSaveIsInFlight_ReturnsUnsavedRecord()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-inflight-save-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteAccountStore accounts = new(factory);
            GatedCharacterStore characters = new(new SqliteCharacterStore(factory));
            SqliteChunkDeltaRepository chunks = new(factory);
            RecordingOutboundSink sink = new();

            DateTimeOffset now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
            Guid accountId = Guid.NewGuid();
            AccountRecord account = new(accountId, "saver", "SAVER", [1, 2, 3], [4, 5, 6], now, false);
            (await accounts.TryCreateAsync(account).ConfigureAwait(true)).Should().BeTrue();

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink, entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(inv, startingClass: 0);
            CharacterRecord hero = new(
                Guid.NewGuid(),
                accountId,
                "Saver",
                Level: 1,
                AppearancePreset: 0,
                PositionX: world.DefaultSpawnWorldPosition.X,
                PositionY: world.DefaultSpawnWorldPosition.Y,
                CurrentHealth: 100f,
                MaxHealth: 100f,
                SelectedHotbarSlot: 0,
                InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: now,
                Flags: CharacterTraits.StarterLoadoutGranted);
            (await characters.TryCreateAsync(hero).ConfigureAwait(true)).Should().BeTrue();

            world.TryEnterWorld(connectionId: 801UL, hero).Status.Should().Be(OpStatus.Ok);
            world.TrySelectHotbarSlot(801UL, 3).Should().Be(OpStatus.Ok);
            world.LeaveWorld(801UL).Should().BeTrue();

            Task flush = world.FlushDirtyPersistenceAsync();
            await characters.SaveStarted.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            // The player logged out and the flush took the record out of the dirty set, but the save has not
            // committed. A re-select now must see the unsaved record, not the stale database row.
            world.TryGetPendingCharacterSnapshot(hero.Id, out CharacterRecord? pending).Should().BeTrue();
            pending!.SelectedHotbarSlot.Should().Be(3);
            (await characters.Inner.FindByIdAsync(hero.Id).ConfigureAwait(true))!.SelectedHotbarSlot.Should().Be(0);

            characters.ReleaseSave();
            await flush.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            world.TryGetPendingCharacterSnapshot(hero.Id, out _).Should().BeFalse("the committed row is now authoritative");
            (await characters.Inner.FindByIdAsync(hero.Id).ConfigureAwait(true))!.SelectedHotbarSlot.Should().Be(3);
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

            AuthoritativeWorld world = new(1337UL, 32f, 0.0008f, 30, chunks, characters, sink, entropy: TestGameplayEntropy.Create(1337UL));
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

            world.TryEnterWorld(connectionId: 601UL, hero).Status.Should().Be(OpStatus.Ok);

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
                world.TryEnterWorld((ulong)(610 + probeX), scout).Status.Should().Be(OpStatus.Ok);
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
            world.TryEnterWorld(602UL, farStriker).Status.Should().Be(OpStatus.Ok);
            int hitsBeforeFar = sink.CombatHits.Count;
            world.EnqueueAttackInput(602UL, mobSpawn.Value.NetworkId, new Vector2(mobSpawn.Value.PositionX, mobSpawn.Value.PositionY), 2).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.CombatHits.Skip(hitsBeforeFar).Should().NotContain(h => h.DefenderNetworkId == mobSpawn.Value.NetworkId);

            // Within strict melee range (36px <= MeleeStrikeRangePx = 56px)
            CharacterRecord closeStriker = hero with { Id = Guid.NewGuid(), Name = "CloseStriker", PositionX = mobSpawn.Value.PositionX + 36f, PositionY = mobSpawn.Value.PositionY };
            world.TryEnterWorld(603UL, closeStriker).Status.Should().Be(OpStatus.Ok);
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
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                economy: economy,
                entropy: TestGameplayEntropy.Create(1337UL));
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
                world.TryEnterWorld(probeConn, template with { Id = Guid.NewGuid(), PositionX = ((probeX * 32) + 16) * 32f, PositionY = 16f * 32f }).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();
                mob = sink.Spawns
                    .Where(x => x.ConnectionId == probeConn && x.Snapshot.Kind == EntityKind.Monster && AuthoritativeWorld.ResolveLootTableId(x.Snapshot.ArchetypeId) is not null)
                    .Select(x => (EntitySpawnSnapshot?)x.Snapshot)
                    .FirstOrDefault();
            }

            mob.Should().NotBeNull("wilderness chunks should populate a slime/skeleton/orc/demon");
            economy.Loot.TryGet(AuthoritativeWorld.ResolveLootTableId(mob!.Value.ArchetypeId)!, out Carbon.Economy.Loot.LootTable? table).Should().BeTrue();

            const ulong slayerConn = 800UL;
            world.TryEnterWorld(slayerConn, template with { Id = Guid.NewGuid(), PositionX = mob.Value.PositionX + 16f, PositionY = mob.Value.PositionY }).Status.Should().Be(OpStatus.Ok);
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
                    .Where(x => x.ConnectionId == slayerConn && x.Snapshot.Kind == EntityKind.LootDrop && x.Snapshot.LootCurrency == LootCurrency.None)
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
            gold.Should().Be(0L, "kill gold drops on the ground instead of being credited (ADR-019)");
            diamonds.Should().Be(0L);
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
    public async Task MonsterKill_ThatLevelsUp_DoesNotRefillHealth()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-levelup-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(bag);
            CharacterRecord template = new(
                Guid.NewGuid(), Guid.NewGuid(), "Hunter", 1, 0, 0f, 0f, 30f, 100f, 2, InventoryBinaryCodec.Serialize(bag), DateTimeOffset.UtcNow);

            EntitySpawnSnapshot? mob = null;
            for (int probeX = 1; probeX <= 12 && mob is null; probeX++)
            {
                ulong probeConn = 700UL + (ulong)probeX;
                world.TryEnterWorld(probeConn, template with { Id = Guid.NewGuid(), PositionX = ((probeX * 32) + 16) * 32f, PositionY = 16f * 32f }).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();
                mob = sink.Spawns
                    .Where(x => x.ConnectionId == probeConn && x.Snapshot.Kind == EntityKind.Monster)
                    .Select(x => (EntitySpawnSnapshot?)x.Snapshot)
                    .FirstOrDefault();
            }

            mob.Should().NotBeNull("wilderness chunks should populate monsters");

            // One experience point short of level 2, so the kill levels the slayer up; health is far below max.
            const ulong slayerConn = 800UL;
            CharacterRecord slayer = template with
            {
                Id = Guid.NewGuid(),
                PositionX = mob!.Value.PositionX + 16f,
                PositionY = mob.Value.PositionY,
                Experience = world.Progression.GetXpToNextLevel(1) - 1U,
            };
            world.TryEnterWorld(slayerConn, slayer).Status.Should().Be(OpStatus.Ok);
            world.TryGetPendingCharacterSnapshot(slayer.Id, out CharacterRecord? before).Should().BeTrue();
            float maxBefore = before!.MaxHealth;

            bool killed = false;
            for (int tick = 0; tick < 900 && !killed; tick++)
            {
                world.EnqueueAttackInput(slayerConn, mob.Value.NetworkId, new Vector2(mob.Value.PositionX, mob.Value.PositionY), 2);
                world.StepFixedTick();
                killed = sink.CombatHits.Any(h => h.DefenderNetworkId == mob.Value.NetworkId && h.IsKilled);
            }

            killed.Should().BeTrue("the slayer should eventually kill the monster");
            world.StepFixedTick();

            world.TryGetPendingCharacterSnapshot(slayer.Id, out CharacterRecord? after).Should().BeTrue();
            after!.Level.Should().BeGreaterThan(1, "the kill grants enough experience to level up");
            float growth = after.MaxHealth - maxBefore;
            after.CurrentHealth.Should().BeLessThanOrEqualTo(
                before.CurrentHealth + growth,
                "a level-up only adds the max-health growth; killing a monster must not heal the player to full");
            after.CurrentHealth.Should().BeLessThan(after.MaxHealth);
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

    [Theory]
    [InlineData("roast_meat")] // built-in StarterGameCatalog consumable
    [InlineData("berries")] // defined only in items.json (economy catalog)
    public async Task HungryPlayerEatingFoodFromHotbar_ConsumesItemAndSyncsNeeds(string foodCode)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-survival-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();

            // 600 food/min (×0.5 idle) empties the meter fast enough to test in a few hundred ticks.
            NeedsConfig needs = new() { FoodDecayPerMinute = 600f };
            AuthoritativeWorld world = CreateSurvivalWorld(factory, sink, needs, out GameEconomy economy, out FoodCatalog food);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            food.TryGetIndex(foodCode, out int foodIndex).Should().BeTrue();
            float nutrition = food.Get(foodIndex).Nutrition;

            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            inv.SetSlot(0, new ItemSlot(economy.Items.ResolveId(foodCode), 3));
            CharacterRecord hero = new(
                Guid.NewGuid(), Guid.NewGuid(), "Glutton", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, SelectedHotbarSlot: 0, InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: DateTimeOffset.UtcNow);
            world.TryEnterWorld(connectionId: 700UL, hero).Status.Should().Be(OpStatus.Ok);

            world.StepFixedTick();
            sink.NeedsSyncs.Should().Contain(n => n.ConnectionId == 700UL && n.Needs.MaxFood == 100f && n.Needs.EatProgress < 0f);

            // Full → eating is refused (overeating cap) and nothing is consumed.
            world.EnqueueAttackInput(700UL, 0UL, world.DefaultSpawnWorldPosition, 0).Should().Be(OpStatus.Ok);
            StepTicks(world, 130);
            sink.SlotChanges.Should().NotContain(c => c.ConnectionId == 700UL && c.SlotIndex == 0 && c.Quantity < 3, "a full player cannot eat (a perishable stack may still be stamped)");

            // 6 s idle at 5 food/s → well below the 90 % cap.
            StepTicks(world, 180);
            float before = sink.NeedsSyncs.Last(n => n.ConnectionId == 700UL).Needs.Food;
            before.Should().BeLessThan(60f);

            world.EnqueueAttackInput(700UL, 0UL, world.DefaultSpawnWorldPosition, 0).Should().Be(OpStatus.Ok);
            StepTicks(world, 30);
            sink.NeedsSyncs.Last(n => n.ConnectionId == 700UL).Needs.EatProgress.Should().BeInRange(0f, 1f, "the HUD shows meal progress");

            StepTicks(world, 160);
            sink.SlotChanges.Should().Contain(c => c.ConnectionId == 700UL && c.SlotIndex == 0 && c.Quantity == 2, $"one {foodCode} is eaten");
            PlayerNeedsSnapshot after = sink.NeedsSyncs.Last(n => n.ConnectionId == 700UL).Needs;
            after.EatProgress.Should().BeLessThan(0f);
            after.Food.Should().BeGreaterThan(before + nutrition - 40f, $"{foodCode} restores {nutrition} food (minus up to ~8 s of decay)");
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
    public async Task StarvingPlayer_DiesFromMalnutritionAndRespawnsHalfFed()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-starve-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();

            // Empty in 12 s idle, then lethal malnutrition after 60 s.
            NeedsConfig needs = new() { FoodDecayPerMinute = 1000f, MalnutritionGainPerMinute = 1f };
            AuthoritativeWorld world = CreateSurvivalWorld(factory, sink, needs, out _, out _);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            CharacterRecord hero = new(
                Guid.NewGuid(), Guid.NewGuid(), "Faster", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, 0, [], DateTimeOffset.UtcNow);
            EnterWorldResult enter = world.TryEnterWorld(connectionId: 701UL, hero);
            enter.Status.Should().Be(OpStatus.Ok);

            StepTicks(world, 30 * 30);
            sink.NeedsSyncs.Last(n => n.ConnectionId == 701UL).Needs.HungerState.Should().Be(3, "food ran out after 12 s");

            int ticksUntilDeath = 0;
            while (!sink.CombatHits.Any(h => h.DefenderNetworkId == enter.PlayerNetworkId && h.IsKilled) && ticksUntilDeath < 30 * 70)
            {
                world.StepFixedTick();
                ticksUntilDeath++;
            }

            sink.CombatHits.Should().Contain(h => h.DefenderNetworkId == enter.PlayerNetworkId && h.IsKilled, "maximum malnutrition kills");
            ((900 + ticksUntilDeath) / 30f).Should().BeInRange(70f, 74f, "12 s to empty + 60 s of malnutrition");
            world.StepFixedTick();
            PlayerNeedsSnapshot respawned = sink.NeedsSyncs.Last(n => n.ConnectionId == 701UL).Needs;
            respawned.Malnutrition.Should().BeLessThan(0.1f, "death clears malnutrition");
            respawned.Food.Should().BeGreaterThan(40f, "respawn restores half the food meter");
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
    public async Task NeedsPersistence_SurvivesLogoutAndMigration_AndLegacyCharactersSpawnFed()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-needs-persist-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);
            await migrator.MigrateAsync().ConfigureAwait(true); // idempotent re-run keeps 0006_character_needs applied once

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            Guid accountId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (await accounts.TryCreateAsync(new AccountRecord(accountId, "hungry", "HUNGRY", [1, 2, 3], [4, 5, 6], now, false)).ConfigureAwait(true)).Should().BeTrue();

            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = CreateSurvivalWorld(factory, sink, new NeedsConfig(), out _, out _);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            CharacterRecord famished = new(
                Guid.NewGuid(), accountId, "Famished", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, 0, [], now,
                NeedsFood: 20f, NeedsMalnutrition: 0.3f, NeedsPoisonSeconds: 30f);
            CharacterRecord legacy = famished with { Id = Guid.NewGuid(), Name = "Legacy", NeedsFood = -1f, NeedsMalnutrition = 0f, NeedsPoisonSeconds = 0f };
            (await characters.TryCreateAsync(famished).ConfigureAwait(true)).Should().BeTrue();
            (await characters.TryCreateAsync(legacy).ConfigureAwait(true)).Should().BeTrue();

            CharacterRecord? stored = await characters.FindByIdAsync(famished.Id).ConfigureAwait(true);
            stored!.NeedsFood.Should().Be(20f);
            stored.NeedsMalnutrition.Should().Be(0.3f);
            stored.NeedsPoisonSeconds.Should().Be(30f);

            world.TryEnterWorld(910UL, stored).Status.Should().Be(OpStatus.Ok);
            world.TryEnterWorld(911UL, (await characters.FindByIdAsync(legacy.Id).ConfigureAwait(true))!).Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();

            PlayerNeedsSnapshot restored = sink.NeedsSyncs.Last(n => n.ConnectionId == 910UL).Needs;
            restored.Food.Should().BeApproximately(20f, 0.1f, "logging out must not refill the hunger meter");
            restored.HungerState.Should().Be(1, "20 % food is Hungry");
            restored.Malnutrition.Should().BeApproximately(0.3f, 0.01f);
            restored.FoodPoisoned.Should().BeTrue();
            sink.NeedsSyncs.Last(n => n.ConnectionId == 911UL).Needs.Food.Should().BeGreaterThan(99f, "characters saved before 0006 spawn fully fed");

            StepTicks(world, 60);
            world.LeaveWorld(910UL).Should().BeTrue();
            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);

            CharacterRecord? reloaded = await characters.FindByIdAsync(famished.Id).ConfigureAwait(true);
            reloaded!.NeedsFood.Should().BeInRange(19f, 20f, "two seconds of decay are saved on logout");
            reloaded.NeedsMalnutrition.Should().BeInRange(0.29f, 0.3f, "malnutrition recovers slowly while fed");
            reloaded.NeedsPoisonSeconds.Should().BeInRange(27.5f, 28.5f, "poisoning keeps counting down");
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
    public async Task RawFoodInInventory_IsStampedThenSpoilsIntoRottenFood()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-spoil-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();

            GameEconomy economy = GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);
            NeedsConfig needs = new();
            // Same foods as food.json, but raw meat spoils after 3 s so the test runs in a few seconds of ticks.
            FoodCatalog food = new(
                [
                    new FoodDefinition { ItemId = "raw_meat", Nutrition = 25f, Quality = FoodQuality.Raw, EatSeconds = 4f, SpoilSeconds = 3f, SpoilsInto = "rotten_food" },
                    new FoodDefinition { ItemId = "rotten_food", Nutrition = 5f, Quality = FoodQuality.Raw, EatSeconds = 2f, PoisonChance = 0.6f },
                ],
                needs.FoodMax);
            AuthoritativeWorld world = new(
                1337UL, 32f, 0.0008f, 30, new SqliteChunkDeltaRepository(factory), new SqliteCharacterStore(factory), sink,
                time: null, needs, food, economy,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            uint rawMeat = economy.Items.ResolveId("raw_meat");
            uint rotten = economy.Items.ResolveId("rotten_food");
            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            inv.SetSlot(5, new ItemSlot(rawMeat, 4));
            CharacterRecord hero = new(
                Guid.NewGuid(), Guid.NewGuid(), "Hoarder", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, 0, InventoryBinaryCodec.Serialize(inv), DateTimeOffset.UtcNow);
            world.TryEnterWorld(920UL, hero).Status.Should().Be(OpStatus.Ok);

            StepTicks(world, 30);
            sink.SlotFreshness.Should().Contain(c => c.ConnectionId == 920UL && c.SlotIndex == 5 && c.ItemId == rawMeat && c.FreshnessSeconds == 3f, "the first spoilage pass stamps the stack");

            List<(ushort SlotIndex, ItemSlot Slot)> snapshot = [];
            StepTicks(world, 30);
            world.GetInventorySnapshot(920UL, snapshot).Should().BeTrue();
            snapshot.Single(s => s.SlotIndex == 5).Slot.FreshnessSeconds.Should().Be(2f, "aging is server-side only");
            sink.SlotFreshness.Count(c => c.ConnectionId == 920UL && c.SlotIndex == 5).Should().Be(1, "aging sends no packets");

            StepTicks(world, 30 * 3);
            sink.SlotChanges.Should().Contain(c => c.ConnectionId == 920UL && c.SlotIndex == 5 && c.ItemId == rotten && c.Quantity == 4, "spoiled meat turns into rotten food");

            snapshot.Clear();
            world.GetInventorySnapshot(920UL, snapshot).Should().BeTrue();
            ItemSlot spoiled = snapshot.Single(s => s.SlotIndex == 5).Slot;
            spoiled.ItemId.Should().Be(rotten);
            spoiled.IsPerishable.Should().BeFalse();
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
    public async Task CampfireCooking_RawMeatToCookedMeat_ThenEatingGivesCookedMoodBonus()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-cooking-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();

            // 600 food/min (x0.5 idle) makes the player hungry enough to eat within a few seconds.
            NeedsConfig needs = new() { FoodDecayPerMinute = 600f };
            AuthoritativeWorld world = CreateSurvivalWorld(factory, sink, needs, out GameEconomy economy, out FoodCatalog food);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            uint rawMeat = economy.Items.ResolveId("raw_meat");
            uint cookedMeat = economy.Items.ResolveId("cooked_meat");
            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            inv.SetSlot(0, new ItemSlot(rawMeat, 1));
            inv.SetSlot(1, new ItemSlot(StarterGameCatalog.WoodLog, 10));
            CharacterRecord cook = new(
                Guid.NewGuid(), Guid.NewGuid(), "Cook", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, SelectedHotbarSlot: 1, InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: DateTimeOffset.UtcNow, Gold: 100);
            EnterWorldResult enter = world.TryEnterWorld(930UL, cook);
            enter.Status.Should().Be(OpStatus.Ok);

            world.ItemRules.RecipeCatalog.TryGetByCode("cooked_meat", out CraftingRecipe? recipe).Should().BeTrue();
            world.TryCraftRecipe(930UL, (ushort)recipe!.Id, 1).Should().Be(OpStatus.OutOfRange, "cooking needs a stove or campfire nearby");

            // Build a campfire (4 wood logs, 40 work) next to the player.
            int px = (int)MathF.Floor(enter.PositionX / 32f);
            int py = (int)MathF.Floor(enter.PositionY / 32f);
            WorldMutationOutcome placed = default;
            foreach ((int dx, int dy) in new[] { (0, 2), (0, -2), (-2, 0), (2, 0), (2, 2), (-2, -2), (2, -2), (-2, 2) })
            {
                placed = world.TryPlaceStructure(930UL, px + dx, py + dy, StructureType.Campfire);
                if (placed.Status == OpStatus.Ok)
                {
                    break;
                }
            }

            placed.Status.Should().Be(OpStatus.Ok, "a campfire blueprint fits next to the player");
            StepTicks(world, 60);
            sink.StructureChanges.Should().Contain(c => c.StructureType == (byte)StructureType.Campfire, "the campfire is built");

            world.TryCraftRecipe(930UL, (ushort)recipe.Id, 1).Should().Be(OpStatus.Ok, "a campfire counts as a stove (the 1-gold stove fee is paid)");
            List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
            world.GetInventorySnapshot(930UL, slots).Should().BeTrue();
            slots.Should().NotContain(x => x.Slot.ItemId == rawMeat, "the raw meat was cooked");
            (ushort mealSlot, ItemSlot meal) = slots.Single(x => x.Slot.ItemId == cookedMeat);
            meal.IsPerishable.Should().BeFalse("cooked food does not spoil");
            mealSlot.Should().BeLessThan(PlayerInventoryLayout.HotbarCount, "the cooked meat lands on the hotbar");

            StepTicks(world, 90);
            world.EnqueueAttackInput(930UL, 0UL, world.DefaultSpawnWorldPosition, (byte)mealSlot).Should().Be(OpStatus.Ok);
            StepTicks(world, (4 * 30) + 5);

            sink.SlotChanges.Should().Contain(c => c.ConnectionId == 930UL && c.SlotIndex == mealSlot && c.Quantity == 0, "the cooked meat was eaten");
            food.TryGetIndex("cooked_meat", out int cookedIndex).Should().BeTrue();
            float bonus = food.Get(cookedIndex).MoodOffset;
            bonus.Should().BeGreaterThan(0f);
            // Cooked meat keeps a small poisoning chance; the roll is seeded by entity and tick, so account for it
            // rather than depend on which entity id the player happens to get.
            PlayerNeedsSnapshot last = sink.NeedsSyncs.Last(n => n.ConnectionId == 930UL).Needs;
            float poison = last.FoodPoisoned ? needs.PoisonMood : 0f;
            last.Mood.Should().BeApproximately(
                needs.MoodBaseline + bonus + poison, 0.01f, "a fed player who ate a cooked meal gets its mood bonus from food.json");
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
    public async Task Resting_SitsWithoutBed_SleepsInBuiltBed_WakesOnMove_AndPersistsRest()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-rest-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            SqliteCharacterStore characters = new(factory);
            RecordingOutboundSink sink = new();
            NeedsConfig needs = new();
            AuthoritativeWorld world = CreateSurvivalWorld(factory, sink, needs, out GameEconomy economy, out _);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            Guid accountId = Guid.NewGuid();
            (await new SqliteAccountStore(factory).TryCreateAsync(new AccountRecord(accountId, "sleepy", "SLEEPY", [1, 2, 3], [4, 5, 6], DateTimeOffset.UtcNow, false)).ConfigureAwait(true)).Should().BeTrue();

            // Starter loadout: a tier-1 axe in hotbar slot 0 (beds need tier 1 to build), plus logs and planks.
            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(inv, startingClass: 0);
            inv.TryAdd(new ItemSlot(economy.Items.ResolveId("wood_plank"), 20)).Should().Be(0);
            inv.TryAdd(new ItemSlot(StarterGameCatalog.WoodLog, 10)).Should().Be(0);
            CharacterRecord sleeper = new(
                Guid.NewGuid(), accountId, "Sleeper", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, 0, InventoryBinaryCodec.Serialize(inv), DateTimeOffset.UtcNow, NeedsRest: 50f);
            (await characters.TryCreateAsync(sleeper).ConfigureAwait(true)).Should().BeTrue();
            EnterWorldResult enter = world.TryEnterWorld(940UL, sleeper);
            enter.Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.NeedsSyncs.Last(n => n.ConnectionId == 940UL).Needs.Rest.Should().BeApproximately(50f, 0.1f, "rest is restored from the record");

            // No bed nearby: resting means sitting. The handler path is tick-queued (ADR-017).
            ValueTask<WorldMutationOutcome> sit = world.SubmitSetRestingAsync(940UL, rest: true);
            world.StepFixedTick();
            (await sit.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok, "the queued rest request runs on the tick");
            sink.NeedsSyncs.Last(n => n.ConnectionId == 940UL).Needs.RestMode.Should().Be(RestMode.Sitting);

            // Moving stands the player up.
            world.EnqueueMoveInput(940UL, 2U, new Vector2(1f, 0f), sprint: false);
            world.StepFixedTick();
            world.EnqueueMoveInput(940UL, 3U, Vector2.Zero, sprint: false);
            StepTicks(world, 10);
            sink.NeedsSyncs.Last(n => n.ConnectionId == 940UL).Needs.RestMode.Should().Be(RestMode.None, "moving wakes a resting player");

            // Build a wood bed (10 planks, 160 work) next to the player. Beds need a finished floor under both tiles.
            int ptx = (int)MathF.Floor(enter.PositionX / 32f);
            int pty = (int)MathF.Floor(enter.PositionY / 32f);
            List<string> attempts = [];
            bool bedPlaced = false;
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (1, -1), (-1, -1), (2, 0), (-2, 0), (0, 1), (0, -2) })
            {
                int bx = ptx + dx;
                int by = pty + dy;
                OpStatus floorA = world.TryPlaceStructure(940UL, bx, by, StructureType.WoodFloor).Status;
                StepTicks(world, 40);
                OpStatus floorB = world.TryPlaceStructure(940UL, bx, by + 1, StructureType.WoodFloor).Status;
                StepTicks(world, 40);
                OpStatus bed = world.TryPlaceStructure(940UL, bx, by, StructureType.WoodBed).Status;
                attempts.Add($"({dx},{dy}) floors={floorA}/{floorB} bed={bed}");
                if (bed == OpStatus.Ok)
                {
                    bedPlaced = true;
                    break;
                }
            }

            bedPlaced.Should().BeTrue($"a bed fits next to the player; attempts: {string.Join(" | ", attempts)}");
            StepTicks(world, 120);
            sink.StructureChanges.Should().Contain(c => c.StructureType == (byte)StructureType.WoodBed, "the bed is built");

            world.TrySetResting(940UL, rest: true).Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            PlayerNeedsSnapshot asleep = sink.NeedsSyncs.Last(n => n.ConnectionId == 940UL).Needs;
            asleep.RestMode.Should().Be(RestMode.Sleeping, "a bed within reach outdoors means plain sleep");
            float restAtBedtime = asleep.Rest;

            StepTicks(world, 30 * 60);
            PlayerNeedsSnapshot rested = sink.NeedsSyncs.Last(n => n.ConnectionId == 940UL).Needs;
            rested.RestMode.Should().Be(RestMode.Sleeping);
            rested.Rest.Should().BeApproximately(restAtBedtime + needs.RestRecoveryPerMinute, 0.3f, "a bed recovers 10 rest per minute");

            world.LeaveWorld(940UL).Should().BeTrue();
            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);
            CharacterRecord? reloaded = await characters.FindByIdAsync(sleeper.Id).ConfigureAwait(true);
            reloaded!.NeedsRest.Should().BeApproximately(rested.Rest, 0.5f, "rest is saved on logout (0007_character_rest)");
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

    private static AuthoritativeWorld CreateSurvivalWorld(
        SqliteConnectionFactory factory,
        RecordingOutboundSink sink,
        NeedsConfig needs,
        out GameEconomy economy,
        out FoodCatalog food)
    {
        // food.json and the economy files are copied next to the host binary (and this test assembly) under config/.
        string configDir = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!;
        economy = GameEconomy.LoadFromDirectory(configDir);
        food = FoodCatalog.LoadFromJson(File.ReadAllText(Path.Combine(configDir, "food.json")), needs.FoodMax);
        return new AuthoritativeWorld(
            1337UL,
            32f,
            0.0008f,
            30,
            new SqliteChunkDeltaRepository(factory),
            new SqliteCharacterStore(factory),
            sink,
            time: null,
            needs,
            food,
            economy,
            entropy: TestGameplayEntropy.Create(1337UL));
    }

    private static void StepTicks(AuthoritativeWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.StepFixedTick();
        }
    }

    [Fact]
    public async Task Wallet_PersistsAcrossSessions_SyncsToClient_AndPaysForVendorSinks()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-wallet-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);
            await migrator.MigrateAsync().ConfigureAwait(true); // idempotent re-run keeps 0005_character_wallet applied once

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            RecordingOutboundSink sink = new();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Guid accountId = Guid.NewGuid();
            (await accounts.TryCreateAsync(new AccountRecord(accountId, "merchant", "MERCHANT", [1, 2, 3], [4, 5, 6], now, false)).ConfigureAwait(true)).Should().BeTrue();

            GameEconomy economy = GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                characters,
                sink,
                economy: economy,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(bag);
            CharacterRecord trader = new(
                Guid.NewGuid(), accountId, "Trader", 1, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, 0, InventoryBinaryCodec.Serialize(bag), now,
                Gold: 500, Diamonds: 2, WalletSequence: 7, DiamondPity: 42);
            (await characters.TryCreateAsync(trader).ConfigureAwait(true)).Should().BeTrue();
            CharacterRecord? stored = await characters.FindByIdAsync(trader.Id).ConfigureAwait(true);
            stored!.Gold.Should().Be(500);
            stored.DiamondPity.Should().Be(42);

            const ulong conn = 901UL;
            world.TryEnterWorld(conn, stored).Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            sink.WalletSyncs.Should().Contain((conn, 500L, 2L, 7UL), "the client receives its persisted balance on world entry");

            // Sanctuary blacksmith next to spawn acts as vendor and repair station.
            uint woodLog = economy.Items.ResolveId("wood_log");
            long logPrice = economy.Prices.GetBuyPrice(woodLog);
            world.TryVendorBuy(conn, (ushort)woodLog, 10).Should().Be(OpStatus.Ok);
            world.TryGetWalletBalances(conn, out long gold, out _).Should().BeTrue();
            gold.Should().Be(500 - (10 * logPrice));
            sink.WalletSyncs[^1].Gold.Should().Be(gold);
            sink.SlotChanges.Should().Contain(c => c.ConnectionId == conn && c.ItemId == woodLog);

            world.TryVendorBuy(conn, (ushort)economy.Items.ResolveId("demon_horn"), 1).Should().Be(OpStatus.Forbidden, "rare drops are not sold by vendors");
            world.TryVendorBuy(conn, (ushort)woodLog, 60_000).Should().Be(OpStatus.Invalid);
            world.TryVendorSell(conn, PlayerInventoryLayout.HeadSlot, 1).Should().Be(OpStatus.Invalid, "equipped armor cannot be sold");

            List<(ushort SlotIndex, ItemSlot Slot)> inventory = [];
            world.GetInventorySnapshot(conn, inventory).Should().BeTrue();
            (ushort swordSlot, ItemSlot sword) = inventory.First(x => x.Slot.ItemId == StarterGameCatalog.IronSword);
            world.TryRepairItem(conn, swordSlot).Should().Be(OpStatus.Conflict, "a full-durability item needs no repair");
            world.TryVendorSell(conn, swordSlot, 1).Should().Be(OpStatus.Ok);
            world.TryGetWalletBalances(conn, out long afterSale, out _).Should().BeTrue();
            afterSale.Should().Be(gold + economy.Prices.GetSellPrice(sword.ItemId));

            // A player far from any town NPC is refused.
            CharacterRecord wanderer = stored with { Id = Guid.NewGuid(), Name = "Wanderer", PositionX = stored.PositionX + 9000f };
            world.TryEnterWorld(902UL, wanderer).Status.Should().Be(OpStatus.Ok);
            world.TryVendorBuy(902UL, (ushort)woodLog, 1).Should().Be(OpStatus.OutOfRange);

            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);
            CharacterRecord? reloaded = await characters.FindByIdAsync(trader.Id).ConfigureAwait(true);
            reloaded!.Gold.Should().Be(afterSale);
            reloaded.Diamonds.Should().Be(2);
            reloaded.WalletSequence.Should().Be(9UL, "one purchase and one sale were recorded after the persisted sequence 7");
            reloaded.DiamondPity.Should().Be(42);
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
    public async Task DataDrivenMonster_AttacksThroughPhaseTimeline_BroadcastsPhasesAndDamagesOnlyAfterActive()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-creature-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(bag);
            CharacterRecord scout = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Scout",
                1,
                0,
                world.DefaultSpawnWorldPosition.X,
                world.DefaultSpawnWorldPosition.Y,
                5000f,
                5000f,
                2,
                InventoryBinaryCodec.Serialize(bag),
                DateTimeOffset.UtcNow);

            EntitySpawnSnapshot? mob = null;
            for (int probeX = 1; probeX <= 6 && mob is null; probeX++)
            {
                CharacterRecord hunter = scout with { Id = Guid.NewGuid(), PositionX = ((probeX * 32) + 16) * 32f, PositionY = 16f * 32f };
                world.TryEnterWorld(connectionId: 303UL, hunter).Status.Should().Be(OpStatus.Ok);
                world.StepFixedTick();
                mob = sink.Spawns.Find(s => s.ConnectionId == 303UL && s.Snapshot.Kind == EntityKind.Monster).Snapshot;
                mob = mob is { NetworkId: > 0UL } ? mob : null;
            }

            mob.Should().NotBeNull("the data-driven spawner populates cells around an entering player");
            mob!.Value.ArchetypeId.Should().BeInRange(101, 109, "monsters replicate their WorldActorCatalog rig archetype");

            // Aggro needs line of sight; with seed 1337 the west side of this mob is clear of props.
            CharacterRecord bait = scout with { Id = Guid.NewGuid(), Name = "Bait", PositionX = mob.Value.PositionX - 30f, PositionY = mob.Value.PositionY };
            EnterWorldResult baitEnter = world.TryEnterWorld(connectionId: 404UL, bait);
            baitEnter.Status.Should().Be(OpStatus.Ok);

            int firstActiveTick = -1;
            int firstHitTick = -1;
            for (int t = 0; t < 300 && firstHitTick < 0; t++)
            {
                int phasesBefore = sink.AttackPhases.Count;
                int hitsBefore = sink.CombatHits.Count;
                world.StepFixedTick();
                for (int i = phasesBefore; i < sink.AttackPhases.Count && firstActiveTick < 0; i++)
                {
                    if (sink.AttackPhases[i].Snapshot.NetworkId == mob.Value.NetworkId && sink.AttackPhases[i].Snapshot.Phase == EntityAttackPhase.PhaseActive)
                    {
                        firstActiveTick = t;
                    }
                }

                for (int i = hitsBefore; i < sink.CombatHits.Count && firstHitTick < 0; i++)
                {
                    if (sink.CombatHits[i].AttackerNetworkId == mob.Value.NetworkId && sink.CombatHits[i].DefenderNetworkId == baitEnter.PlayerNetworkId)
                    {
                        firstHitTick = t;
                    }
                }
            }

            sink.AttackPhases.Should().Contain(
                p => p.ConnectionId == 404UL && p.Snapshot.NetworkId == mob.Value.NetworkId && p.Snapshot.Phase == EntityAttackPhase.PhaseWindup && p.Snapshot.PhaseDurationTicks > 0,
                "observers receive the wind-up telegraph");
            firstHitTick.Should().BeGreaterThanOrEqualTo(0, "the monster eventually lands a hit");
            firstActiveTick.Should().BeInRange(0, firstHitTick, "damage is only resolved once an attack reaches its active phase");
            sink.SkillCasts.Should().NotContain(c => c.CasterNetworkId == mob.Value.NetworkId, "monster attacks no longer overload SkillCastBroadcast");
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
    public async Task ForgeRecipe_AtSanctuaryBlacksmith_EnforcesUnlockLevelAndChargesStationFee()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-forge-craft-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            RecordingOutboundSink sink = new();
            GameEconomy economy = GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);
            AuthoritativeWorld world = new(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                economy: economy,
                entropy: TestGameplayEntropy.Create(1337UL));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            economy.Recipes.TryGetByCode("copper_ingot", out CraftingRecipe? copper).Should().BeTrue();
            economy.Recipes.TryGetByCode("iron_ingot", out CraftingRecipe? iron).Should().BeTrue();
            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            bag.SetSlot(0, new ItemSlot(economy.Items.ResolveId("copper_ore"), 2));
            bag.SetSlot(1, new ItemSlot(economy.Items.ResolveId("coal"), 2));
            bag.SetSlot(2, new ItemSlot(economy.Items.ResolveId("iron_ore"), 2));
            CharacterRecord smith = new(
                Guid.NewGuid(), Guid.NewGuid(), "Smith", (ushort)copper!.UnlockLevel, 0,
                world.DefaultSpawnWorldPosition.X, world.DefaultSpawnWorldPosition.Y,
                100f, 100f, 0, InventoryBinaryCodec.Serialize(bag), DateTimeOffset.UtcNow, Gold: 100);
            world.TryEnterWorld(990UL, smith).Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick(); // spawns the sanctuary blacksmith next to spawn

            world.TryCraftRecipe(990UL, (ushort)iron!.Id, 1).Should().Be(OpStatus.Forbidden, $"iron ingots unlock at level {iron.UnlockLevel}");
            world.TryCraftRecipe(990UL, (ushort)copper.Id, 1).Should().Be(OpStatus.Ok, "the sanctuary blacksmith is a forge station");

            long fee = economy.Prices.GetCraftingFee(copper);
            fee.Should().BePositive();
            world.TryGetWalletBalances(990UL, out long gold, out _).Should().BeTrue();
            gold.Should().Be(100 - fee, "the forge fee is a gold sink");
            sink.WalletSyncs.Should().Contain(w => w.ConnectionId == 990UL && w.Gold == gold);
            List<(ushort SlotIndex, ItemSlot Slot)> inventory = [];
            world.GetInventorySnapshot(990UL, inventory).Should().BeTrue();
            inventory.Should().Contain(x => x.Slot.ItemId == copper.Output.ItemId && x.Slot.Quantity == 1);
            inventory.Should().NotContain(x => x.Slot.ItemId == economy.Items.ResolveId("copper_ore"));

            world.TryCraftRecipe(990UL, (ushort)copper.Id, 1).Should().Be(OpStatus.Conflict, "the ore is used up and nothing else changes");
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

    /// <summary>Character store whose <see cref="SaveBatchAsync"/> blocks until the test releases it.</summary>
    private sealed class GatedCharacterStore(ICharacterStore inner) : ICharacterStore
    {
        private readonly TaskCompletionSource _saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ICharacterStore Inner { get; } = inner;

        public Task SaveStarted => _saveStarted.Task;

        public void ReleaseSave() => _release.TrySetResult();

        public Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) =>
            this.Inner.ListByAccountAsync(accountId, ct);

        public Task<CharacterRecord?> FindByIdAsync(Guid characterId, CancellationToken ct = default) =>
            this.Inner.FindByIdAsync(characterId, ct);

        public Task<bool> TryCreateAsync(CharacterRecord character, CancellationToken ct = default) =>
            this.Inner.TryCreateAsync(character, ct);

        public async Task SaveBatchAsync(IReadOnlyList<CharacterRecord> characters, CancellationToken ct = default)
        {
            _saveStarted.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            await this.Inner.SaveBatchAsync(characters, ct).ConfigureAwait(false);
        }
    }

    internal sealed class RecordingOutboundSink : IWorldOutboundSink
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
        public List<(ulong ConnectionId, AttackPhaseSnapshot Snapshot)> AttackPhases { get; } = [];
        public List<(ulong ConnectionId, long Gold, long Diamonds, ulong Sequence)> WalletSyncs { get; } = [];
        public List<(ulong[] Recipients, ulong PlayerNetworkId, string PlayerName, ushort ItemId, EnhancementOutcome Outcome, byte Level)> Announcements { get; } = [];

        public void SendEnhancementAnnouncement(ReadOnlySpan<ulong> recipientConnectionIds, ulong playerNetworkId, string playerName, ushort itemId, EnhancementOutcome outcome, byte level) =>
            this.Announcements.Add((recipientConnectionIds.ToArray(), playerNetworkId, playerName, itemId, outcome, level));

        public void SendWalletSync(ulong connectionId, long gold, long diamonds, ulong sequence) =>
            this.WalletSyncs.Add((connectionId, gold, diamonds, sequence));

        public List<(ulong ConnectionId, TraderStockSnapshot Stock)> TraderStocks { get; } = [];

        public void SendTraderStock(ulong connectionId, TraderStockSnapshot stock) =>
            this.TraderStocks.Add((connectionId, stock));

        public List<(ulong ConnectionId, PlayerNeedsSnapshot Needs)> NeedsSyncs { get; } = [];
        public List<(ulong ConnectionId, ushort SlotIndex, uint ItemId, float FreshnessSeconds)> SlotFreshness { get; } = [];

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
            ReadOnlySpan<EntityDeltaEntry> entries)
        {
            this.DeltaBatches.Add((connectionId, lastProcessedClientTick));
            this.SelfPositions[connectionId] = new Vector2(selfX, selfY);
            foreach (EntityDeltaEntry entry in entries)
            {
                this.LastPositions[entry.NetworkId] = new Vector2(entry.PositionX, entry.PositionY);
                this.DeltaEntityIds.Add((connectionId, entry.NetworkId));
            }
        }

        /// <summary>Gets every (recipient, entity) pair delivered in a delta batch, in send order.</summary>
        public List<(ulong ConnectionId, ulong NetworkId)> DeltaEntityIds { get; } = [];

        public Dictionary<ulong, Vector2> LastPositions { get; } = [];

        public Dictionary<ulong, Vector2> SelfPositions { get; } = [];

        public List<(int TileX, int TileY, float Durability)> PropChanges { get; } = [];

        public void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, uint revision, float durability, bool isDestroyed) =>
            this.PropChanges.Add((tileX, tileY, durability));

        public List<(ulong ConnectionId, bool Exhausted, uint RefusalCount)> StaminaStatuses { get; } = [];

        public void SendStaminaStatus(ulong connectionId, bool exhausted, uint refusalCount) =>
            this.StaminaStatuses.Add((connectionId, exhausted, refusalCount));

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

        public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0, float freshnessSeconds = 0f)
        {
            this.SlotChanges.Add((connectionId, slotIndex, itemId, quantity));
            this.SlotFreshness.Add((connectionId, slotIndex, itemId, freshnessSeconds));
        }

        public void SendEntityEquipmentChanged(ReadOnlySpan<ulong> recipientConnectionIds, ulong entityNetworkId, ushort headId, ushort chestId, ushort legsId, ushort feetId, ushort weaponId, byte weaponUpgrade, ushort level, float maxHealth = 0f) =>
            this.EquipmentChanges.Add((entityNetworkId, chestId, weaponId, level));

        public void SendPlayerStatsSync(ulong connectionId, in PlayerStatsSnapshot snapshot)
        {
            this.NeedsSyncs.Add((connectionId, snapshot.Needs));
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

        public void SendEntityAttackPhase(ReadOnlySpan<ulong> recipientConnectionIds, in AttackPhaseSnapshot snapshot)
        {
            for (int i = 0; i < recipientConnectionIds.Length; i++)
            {
                this.AttackPhases.Add((recipientConnectionIds[i], snapshot));
            }
        }
    }
}
