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
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Economy;
using Carbon.Economy.Repair;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Building;
using Carbon.Simulation.World;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Nalix.Abstractions.Primitives;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Repair through the authoritative world (ADR-028): the server checks the venue, tier, materials and fee, spends them
/// atomically on the tick, changes only the selected item and persists it. Values are pinned by hand from the shipped
/// <c>repair.json</c>: a half-worn iron chestplate (130/260, tier 2, uncommon, 180 base) takes 5 steps, 2 iron ingots
/// and a 27 gold blacksmith fee.
/// </summary>
public sealed class RepairWorldTests
{
    private const ulong Conn = 611UL;
    private const ushort WornSwordSlot = 0;
    private const ushort SpareChestSlot = 5;
    private const ushort IngotSlot = 10;
    private const ushort Chest = PlayerInventoryLayout.ChestSlot;

    [Fact]
    public async Task SubmitRepairItemAsync_EquippedChestplateAtTheBlacksmith_SpendsExactCostsChangesOnlyItAndPersists()
    {
        using Fixture f = await Fixture.CreateAsync().ConfigureAwait(true);
        CharacterRecord record = await f.EnterAsync(chestDurability: 130f, ingots: 5, gold: 1_000).ConfigureAwait(true);
        List<(ushort SlotIndex, ItemSlot Slot)> before = f.Snapshot();

        ValueTask<WorldMutationOutcome> pending = f.World.SubmitRepairItemAsync(Conn, Chest, (ushort)f.Chestplate, 5, RepairVenue.Blacksmith);
        f.World.StepFixedTick();
        WorldMutationOutcome outcome = await pending.ConfigureAwait(true);

        outcome.Status.Should().Be(OpStatus.Ok);
        RepairReceipt.Unpack(outcome.Revision).Should().Be(new RepairReceipt(RepairError.None, 5));
        List<(ushort SlotIndex, ItemSlot Slot)> after = f.Snapshot();
        Slot(after, Chest).Durability.Should().Be(260f);
        Slot(after, IngotSlot).Quantity.Should().Be(3, "2 of the 5 ingots were spent");
        after.Where(s => s.SlotIndex is not Chest and not IngotSlot).Should().Equal(
            before.Where(s => s.SlotIndex is not Chest and not IngotSlot),
            "only the selected item and the spent materials change (the worn sword and the spare chestplate are untouched)");
        f.World.TryGetWalletBalances(Conn, out long gold, out _).Should().BeTrue();
        gold.Should().Be(1_000 - 27);

        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);
        CharacterRecord? stored = await f.Characters.FindByIdAsync(record.Id).ConfigureAwait(true);
        stored!.Gold.Should().Be(973);
        InventoryContainer reloaded = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(stored.InventoryBlob, reloaded).Should().BeTrue();
        reloaded.GetSlot(Chest).Durability.Should().Be(260f);
        reloaded.CountItem(f.Ingot).Should().Be(3);
    }

    [Fact]
    public async Task TryRepairItem_NotEnoughMaterials_IsRefusedWithNothingConsumed()
    {
        using Fixture f = await Fixture.CreateAsync().ConfigureAwait(true);
        await f.EnterAsync(chestDurability: 130f, ingots: 1, gold: 1_000).ConfigureAwait(true);
        List<(ushort SlotIndex, ItemSlot Slot)> before = f.Snapshot();

        WorldMutationOutcome outcome = f.World.TryRepairItem(Conn, Chest, (ushort)f.Chestplate, 5, RepairVenue.Blacksmith);

        outcome.Status.Should().Be(OpStatus.Conflict);
        RepairReceipt.Unpack(outcome.Revision).Error.Should().Be(RepairError.MissingMaterials);
        f.AssertNothingChanged(before, gold: 1_000);
    }

    [Fact]
    public async Task TryRepairItem_UndamagedItem_IsRefused()
    {
        using Fixture f = await Fixture.CreateAsync().ConfigureAwait(true);
        await f.EnterAsync(chestDurability: 260f, ingots: 5, gold: 1_000).ConfigureAwait(true);
        List<(ushort SlotIndex, ItemSlot Slot)> before = f.Snapshot();

        WorldMutationOutcome outcome = f.World.TryRepairItem(Conn, Chest, (ushort)f.Chestplate, 1, RepairVenue.Blacksmith);

        RepairReceipt.Unpack(outcome.Revision).Error.Should().Be(RepairError.NotDamaged);
        f.AssertNothingChanged(before, gold: 1_000);
    }

    [Fact]
    public async Task TryRepairItem_ItemWithoutDurability_IsNotRepairable()
    {
        using Fixture f = await Fixture.CreateAsync().ConfigureAwait(true);
        await f.EnterAsync(chestDurability: 130f, ingots: 5, gold: 1_000).ConfigureAwait(true);
        List<(ushort SlotIndex, ItemSlot Slot)> before = f.Snapshot();

        WorldMutationOutcome outcome = f.World.TryRepairItem(Conn, IngotSlot, (ushort)f.Ingot, 1, RepairVenue.Blacksmith);

        RepairReceipt.Unpack(outcome.Revision).Error.Should().Be(RepairError.NotRepairable);
        f.AssertNothingChanged(before, gold: 1_000);
    }

    [Theory]
    [InlineData(RepairVenue.Blacksmith)]
    [InlineData(RepairVenue.Station)]
    public async Task TryRepairItem_FarFromAnyBlacksmithOrStation_IsOutOfRange(RepairVenue venue)
    {
        using Fixture f = await Fixture.CreateAsync().ConfigureAwait(true);
        await f.EnterAsync(chestDurability: 130f, ingots: 5, gold: 1_000, offsetX: 9_000f).ConfigureAwait(true);
        List<(ushort SlotIndex, ItemSlot Slot)> before = f.Snapshot();

        WorldMutationOutcome outcome = f.World.TryRepairItem(Conn, Chest, (ushort)f.Chestplate, 5, venue);

        outcome.Status.Should().Be(OpStatus.OutOfRange);
        RepairReceipt.Unpack(outcome.Revision).Error.Should().Be(RepairError.OutOfRange);
        f.AssertNothingChanged(before, gold: 1_000);
    }

    [Fact]
    public void GetRepairStationTier_StationsInReach_ReportTheirConfiguredTier()
    {
        StationHarness h = new();
        h.Inventory.GetRepairStationTier(h.Position, RepairVenue.Station, h.Table).Should().Be(0, "nothing is built yet");

        h.Grid.ForcePlaceStructure(6, 0, StructureType.Workbench);
        h.Inventory.GetRepairStationTier(h.Position, RepairVenue.Station, h.Table).Should().Be(0, "6 tiles is beyond stations.stationReachTiles (5)");

        h.Grid.ForcePlaceStructure(2, 0, StructureType.Workbench);
        h.Inventory.GetRepairStationTier(h.Position, RepairVenue.Station, h.Table).Should().Be(1, "repair.json stations.workbench");
        h.Inventory.GetRepairStationTier(h.Position, RepairVenue.Blacksmith, h.Table).Should().Be(0, "no blacksmith in reach");

        h.Grid.ForcePlaceStructure(-2, 0, StructureType.ForgeAnvil);
        h.Inventory.GetRepairStationTier(h.Position, RepairVenue.Station, h.Table).Should().Be(2, "an anvil beats a workbench");

        h.BlacksmithNearby = true;
        h.Inventory.GetRepairStationTier(h.Position, RepairVenue.Blacksmith, h.Table).Should().Be(3);
    }

    [Fact]
    public void Repair_IronGearAtAWorkbenchOnly_IsRefusedForTheStationTier()
    {
        StationHarness h = new();
        h.Grid.ForcePlaceStructure(2, 0, StructureType.Workbench);
        uint chest = h.Rules.ItemCatalog.ResolveId("iron_chestplate");
        uint ingot = h.Rules.ItemCatalog.ResolveId("iron_ingot");
        h.Player.Inventory.SetSlot(Chest, new ItemSlot(chest, 1, InstanceId: 3, Durability: 130f));
        h.Player.Inventory.SetSlot(IngotSlot, new ItemSlot(ingot, 5));

        RepairAttempt attempt = h.Inventory.Repair(h.Player, Chest, (ushort)chest, 5, RepairVenue.Station, h.Position, []);

        attempt.Error.Should().Be(RepairError.StationTierTooLow, "iron needs a forge anvil (tier 2)");
        h.Player.Inventory.GetSlot(Chest).Durability.Should().Be(130f);
        h.Player.Inventory.CountItem(ingot).Should().Be(5);
    }

    private static ItemSlot Slot(List<(ushort SlotIndex, ItemSlot Slot)> slots, ushort index) => slots.First(s => s.SlotIndex == index).Slot;

    private sealed class StationHarness
    {
        public StationHarness()
        {
            WorldSimulation simulation = new();
            GroundLootService loot = new(simulation, () => this.Rules, () => 9_000UL, () => 50UL, (_, _) => { });
            this.Inventory = new InventoryService(
                simulation, this.Grid, 32f, TestGameplayEntropy.Create(1UL), () => this.Rules, () => null, () => 50UL,
                (_, _, _) => this.BlacksmithNearby, loot, new OutboundTickBuffer(), _ => { });
            ConnectedPlayerSession session = new(9UL, Guid.NewGuid(), Guid.NewGuid(), "Smith", 900UL, 0, 0);
            CharacterRecord character = new(session.CharacterId, session.AccountId, "Smith", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            this.Player = new PlayerState(session, simulation.SpawnNetworkEntity(900UL, this.Position, 7f), character, new InventoryContainer(PlayerInventoryLayout.TotalCapacity));
        }

        public ItemRules Rules { get; } = ItemRules.LoadShippedDefaults();

        public Items.Repair.RepairTable Table => this.Rules.Repair;

        public BuildingGrid Grid { get; } = new();

        public InventoryService Inventory { get; }

        public PlayerState Player { get; }

        public Vector2 Position { get; } = new(16f, 16f);

        public bool BlacksmithNearby { get; set; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;
        private readonly Guid _accountId = Guid.NewGuid();

        private Fixture(string dbPath, SqliteConnectionFactory factory, GameEconomy economy)
        {
            _dbPath = dbPath;
            _factory = factory;
            this.Economy = economy;
            this.Characters = new SqliteCharacterStore(factory);
            this.World = new AuthoritativeWorld(
                1337UL,
                32f,
                0.0008f,
                30,
                new SqliteChunkDeltaRepository(factory),
                this.Characters,
                new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
                economy: economy,
                entropy: TestGameplayEntropy.Create(1337UL));
        }

        public GameEconomy Economy { get; }

        public SqliteCharacterStore Characters { get; }

        public AuthoritativeWorld World { get; }

        public uint Chestplate => this.Economy.Items.ResolveId("iron_chestplate");

        public uint Ingot => this.Economy.Items.ResolveId("iron_ingot");

        public static async Task<Fixture> CreateAsync()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-repair-test-{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            GameEconomy economy = GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);
            Fixture fixture = new(dbPath, factory, economy);
            await fixture.World.InitializeFromStoreAsync().ConfigureAwait(true);
            (await new SqliteAccountStore(factory).TryCreateAsync(new AccountRecord(fixture._accountId, "smith", "SMITH", Bytes32.Zero, [4, 5, 6], DateTimeOffset.UtcNow, false)).ConfigureAwait(true))
                .Should().BeTrue();
            return fixture;
        }

        // The player stands at the world spawn, beside the sanctuary blacksmith, unless moved by offsetX.
        public async Task<CharacterRecord> EnterAsync(float chestDurability, int ingots, long gold, float offsetX = 0f)
        {
            InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
            bag.SetSlot(WornSwordSlot, new ItemSlot(StarterGameCatalog.IronSword, 1, InstanceId: 41, Durability: 50f));
            bag.SetSlot(SpareChestSlot, new ItemSlot(this.Chestplate, 1, InstanceId: 43, Durability: 60f));
            bag.SetSlot(Chest, new ItemSlot(this.Chestplate, 1, InstanceId: 42, Durability: chestDurability));
            bag.SetSlot(IngotSlot, new ItemSlot(this.Ingot, ingots));
            CharacterRecord record = new(
                Guid.NewGuid(),
                _accountId,
                "Smith",
                1,
                0,
                this.World.DefaultSpawnWorldPosition.X + offsetX,
                this.World.DefaultSpawnWorldPosition.Y,
                100f,
                100f,
                0,
                InventoryBinaryCodec.Serialize(bag),
                DateTimeOffset.UtcNow,
                Gold: gold);
            (await this.Characters.TryCreateAsync(record).ConfigureAwait(true)).Should().BeTrue();
            this.World.TryEnterWorld(Conn, record).Status.Should().Be(OpStatus.Ok);
            return record;
        }

        public List<(ushort SlotIndex, ItemSlot Slot)> Snapshot()
        {
            List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
            this.World.GetInventorySnapshot(Conn, slots).Should().BeTrue();
            return slots;
        }

        public void AssertNothingChanged(List<(ushort SlotIndex, ItemSlot Slot)> before, long gold)
        {
            this.Snapshot().Should().Equal(before, "a refused repair changes and consumes nothing");
            this.World.TryGetWalletBalances(Conn, out long now, out _).Should().BeTrue();
            now.Should().Be(gold, "a refused repair charges nothing");
        }

        public void Dispose()
        {
            _factory.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
    }
}
