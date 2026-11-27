// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Application.World.Entropy;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Items.S2C;
using Carbon.Economy;
using Carbon.Economy.Trading;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
using Carbon.Primitives.Random;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static Carbon.Host.Tests.AuthoritativeWorldMultiplayerTests;

namespace Carbon.Host.Tests;

/// <summary>Trading with village traders through the real world, persistence included (ADR-025).</summary>
public sealed class TraderWorldTests
{
    private const ulong Seed = 1337UL;

    // The persisted gameplay secret pins the rolled stock (ADR-026), as the world seed did before.
    private const ulong TraderWorldSecret = 2026UL;
    private const ulong Conn = 501UL;

    [Fact]
    public async Task Merchant_OpenBuySell_ValidatesStockEpochWalletAndRange_AndSalesPersist()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-trader-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            await SeedGameplaySecretAsync(factory).ConfigureAwait(true);
            ManualTime time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L));
            (int fx, int fy) = MerchantFrontTile();

            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = await CreateWorldAsync(factory, sink, time).ConfigureAwait(true);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            world.TryEnterWorld(Conn, Conn, Character("Buyer", fx, fy, gold: 5_000)).Status.Should().Be(OpStatus.Ok);
            Step(world, 12);
            ulong merchant = sink.Spawns.First(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.VillageMerchant).Snapshot.NetworkId;

            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn, merchant))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot stock = sink.TraderStocks.Last(t => t.ConnectionId == Conn).Stock;
            stock.Kind.Should().Be(TraderStock.KindMerchant);
            stock.CanRepair.Should().BeFalse("only the blacksmith repairs");
            stock.RepairSlots.Should().BeEmpty();
            stock.ItemIds.Should().NotBeEmpty();
            stock.SecondsUntilRestock.Should().BeInRange(1, 30 * 60);
            int slot = Array.FindIndex(stock.Remaining, r => r >= 2);
            slot.Should().BeGreaterThanOrEqualTo(0);

            long goldBefore = sink.WalletSyncs.Count > 0 ? sink.WalletSyncs.Last().Gold : 5_000L;
            (await Run(world, world.Trade.SubmitTraderBuyAsync(Conn, merchant, (byte)slot, 2, stock.Epoch))).Should().Be(OpStatus.Ok);
            sink.WalletSyncs.Last(w => w.ConnectionId == Conn).Gold.Should().Be(goldBefore - (2L * stock.UnitPrices[slot]));
            TraderStockSnapshot after = sink.TraderStocks.Last(t => t.ConnectionId == Conn).Stock;
            after.Remaining[slot].Should().Be((ushort)(stock.Remaining[slot] - 2), "stock is limited per restock");
            sink.SlotChanges.Should().Contain(c => c.ConnectionId == Conn && c.ItemId == stock.ItemIds[slot]);

            (await Run(world, world.Trade.SubmitTraderBuyAsync(Conn, merchant, (byte)slot, (ushort)(after.Remaining[slot] + 1), stock.Epoch)))
                .Should().Be(OpStatus.Conflict, "more than is left cannot be bought");
            (await Run(world, world.Trade.SubmitTraderBuyAsync(Conn, merchant, (byte)slot, 1, stock.Epoch - 1)))
                .Should().Be(OpStatus.Conflict, "a stale epoch is refused");
            (await Run(world, world.Trade.SubmitTraderBuyAsync(Conn, merchant, 99, 1, stock.Epoch))).Should().Be(OpStatus.Invalid);
            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn, 0xDEADUL))).Should().Be(OpStatus.NotFound);

            // Sell what was bought back: the trader pays the vendor sell price (the 25 % spread), never more than it charged.
            int sellPrice = after.SellPrices[Array.IndexOf(after.SellItemIds, stock.ItemIds[slot])];
            sellPrice.Should().BeLessThan(stock.UnitPrices[slot], "buy-then-sell never mints gold");
            ushort bagSlot = sink.SlotChanges.Last(c => c.ConnectionId == Conn && c.ItemId == stock.ItemIds[slot]).SlotIndex;
            long goldMid = sink.WalletSyncs.Last(w => w.ConnectionId == Conn).Gold;
            (await Run(world, world.Trade.SubmitTraderSellAsync(Conn, merchant, bagSlot, 1))).Should().Be(OpStatus.Ok);
            sink.WalletSyncs.Last(w => w.ConnectionId == Conn).Gold.Should().Be(goldMid + sellPrice);

            // A second player far away is out of reach.
            world.TryEnterWorld(Conn + 1, Conn + 1, Character("Far", fx + 40, fy + 40, gold: 0)).Status.Should().Be(OpStatus.Ok);
            Step(world, 1);
            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn + 1, merchant))).Should().Be(OpStatus.OutOfRange);

            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);

            // A restarted server (same clock) remembers the sale: the slot is not refilled mid-epoch.
            RecordingOutboundSink sink2 = new();
            AuthoritativeWorld restarted = await CreateWorldAsync(factory, sink2, time).ConfigureAwait(true);
            await restarted.InitializeFromStoreAsync().ConfigureAwait(true);
            restarted.TryEnterWorld(Conn, Conn, Character("Buyer2", fx, fy, gold: 10)).Status.Should().Be(OpStatus.Ok);
            Step(restarted, 12);
            ulong merchant2 = sink2.Spawns.First(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.VillageMerchant).Snapshot.NetworkId;
            (await Run(restarted, restarted.Trade.SubmitTraderOpenAsync(Conn, merchant2))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot reloaded = sink2.TraderStocks.Last().Stock;
            reloaded.Epoch.Should().Be(stock.Epoch);
            reloaded.ItemIds.Should().Equal(stock.ItemIds, "stock is derived from (persisted secret, trader, epoch), so a restart with a new boot salt does not reshuffle it");
            reloaded.Remaining[slot].Should().Be(after.Remaining[slot], "sales survive a restart");

            // After the restock the trader is fully stocked again, with a new roll.
            time.Advance(TimeSpan.FromSeconds(reloaded.SecondsUntilRestock));
            (await Run(restarted, restarted.Trade.SubmitTraderOpenAsync(Conn, merchant2))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot restocked = sink2.TraderStocks.Last().Stock;
            restocked.Epoch.Should().Be(stock.Epoch + 1);
            restocked.Remaining.Should().Equal(restocked.Stocked);
            (await Run(restarted, restarted.Trade.SubmitTraderBuyAsync(Conn, merchant2, 0, 1, restocked.Epoch)))
                .Should().Be(restocked.UnitPrices[0] > 10 ? OpStatus.Conflict : OpStatus.Ok, "a buyer who cannot afford it is refused");
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
    public async Task Blacksmith_AtSmithyAndSanctuary_SellsGearQuotesRepairsAndRepairs()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-smith-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            ManualTime time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L));
            (int fx, int fy) = FrontTileOf(VillageBuildingRole.Smithy);

            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = await CreateWorldAsync(factory, sink, time).ConfigureAwait(true);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            GameEconomy economy = world.Economy!;
            ItemSlot worn = new(StarterGameCatalog.IronSword, 1, 9_001UL, Durability: 50f);
            world.TryEnterWorld(Conn, Conn, Character("Smith Client", fx, fy, gold: 5_000, worn)).Status.Should().Be(OpStatus.Ok);
            Step(world, 12);

            // The village blacksmith works at the smithy's forge: the nearest Master Blacksmith to its door.
            System.Numerics.Vector2 door = new((fx + 0.5f) * 32f, (fy + 0.5f) * 32f);
            ulong smith = sink.Spawns
                .Where(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.MasterBlacksmith)
                .OrderBy(s => System.Numerics.Vector2.DistanceSquared(new(s.Snapshot.PositionX, s.Snapshot.PositionY), door))
                .First().Snapshot.NetworkId;

            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn, smith))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot stock = sink.TraderStocks.Last(t => t.ConnectionId == Conn).Stock;
            stock.Kind.Should().Be(TraderStock.KindBlacksmith);
            stock.CanRepair.Should().BeTrue();
            economy.Traders!.TryGet("blacksmith", out TraderProfile profile).Should().BeTrue();
            stock.ItemIds.Should().NotBeEmpty().And.OnlyContain(id => profile.Entries.Any(e => e.ItemId == id), "weapons, armor, tools and smithing stock only");

            // The worn sword (bag slot 0) is quoted at the forge's repair price.
            long quote = economy.Prices.GetRepairCost(worn);
            quote.Should().BePositive();
            stock.RepairSlots.Should().Equal(0);
            stock.RepairCosts.Should().Equal((int)quote);

            long goldBefore = sink.WalletSyncs.Count > 0 ? sink.WalletSyncs.Last(w => w.ConnectionId == Conn).Gold : 5_000L;
            (await Run(world, world.SubmitRepairItemAsync(Conn, 0))).Should().Be(OpStatus.Ok, "the blacksmith is a forge station");
            sink.WalletSyncs.Last(w => w.ConnectionId == Conn).Gold.Should().Be(goldBefore - quote, "the repair costs exactly the quote");
            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn, smith))).Should().Be(OpStatus.Ok);
            sink.TraderStocks.Last(t => t.ConnectionId == Conn).Stock.RepairSlots.Should().BeEmpty("the sword is whole again");

            // The sanctuary blacksmith at the world spawn trades too, from his own (stable) stock.
            System.Numerics.Vector2 spawn = world.DefaultSpawnWorldPosition;
            world.TryEnterWorld(Conn + 1, Conn + 1, Character("Spawn Client", (int)(spawn.X / 32f), (int)(spawn.Y / 32f), gold: 0)).Status.Should().Be(OpStatus.Ok);
            Step(world, 4);
            ulong sanctuarySmith = sink.Spawns
                .Where(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.MasterBlacksmith)
                .OrderBy(s => System.Numerics.Vector2.DistanceSquared(new(s.Snapshot.PositionX, s.Snapshot.PositionY), spawn))
                .First().Snapshot.NetworkId;
            sanctuarySmith.Should().NotBe(smith);
            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn + 1, sanctuarySmith))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot sanctuary = sink.TraderStocks.Last(t => t.ConnectionId == Conn + 1).Stock;
            sanctuary.Kind.Should().Be(TraderStock.KindBlacksmith);
            sanctuary.CanRepair.Should().BeTrue();
            sanctuary.ItemIds.Should().OnlyContain(id => profile.Entries.Any(e => e.ItemId == id));
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

    private static async Task<OpStatus> Run(AuthoritativeWorld world, ValueTask<WorldMutationOutcome> pending)
    {
        world.StepFixedTick();
        return (await pending.ConfigureAwait(true)).Status;
    }

    private static void Step(AuthoritativeWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.StepFixedTick();
        }
    }

    /// <summary>
    /// Stores a fixed gameplay secret as this world's persisted secret (ADR-026), so the rolled stock is known to the
    /// assertions; every boot below still loads it through the store like the host does.
    /// </summary>
    private static async Task SeedGameplaySecretAsync(SqliteConnectionFactory factory)
    {
        byte[] secret = new byte[GameplayKey.SizeInBytes];
        TestGameplayEntropy.Key(TraderWorldSecret).WriteBytes(secret);
        _ = await new SqliteGameplaySecretStore(factory).GetOrCreateAsync(secret).ConfigureAwait(true);
    }

    [Fact]
    public async Task FlushDirtyPersistence_TraderSaveFailsOnce_SaleIsRetriedAndSurvivesRestart()
    {
        // A lost retry would refill sold-out stock after a restart: items minted through the economy.
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-trader-retry-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            await SeedGameplaySecretAsync(factory).ConfigureAwait(true);
            ManualTime time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L));
            FailFirstSaveTraderStockRepository repository = new();
            (int fx, int fy) = MerchantFrontTile();

            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = await CreateWorldAsync(factory, sink, time, repository).ConfigureAwait(true);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            world.TryEnterWorld(Conn, Conn, Character("Buyer", fx, fy, gold: 5_000)).Status.Should().Be(OpStatus.Ok);
            Step(world, 12);
            ulong merchant = sink.Spawns.First(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.VillageMerchant).Snapshot.NetworkId;
            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn, merchant))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot stock = sink.TraderStocks.Last(t => t.ConnectionId == Conn).Stock;
            int slot = Array.FindIndex(stock.Remaining, r => r >= 2);
            (await Run(world, world.Trade.SubmitTraderBuyAsync(Conn, merchant, (byte)slot, 2, stock.Epoch))).Should().Be(OpStatus.Ok);

            Func<Task> failingFlush = () => world.FlushDirtyPersistenceAsync();
            await failingFlush.Should().ThrowAsync<IOException>("the store is down for the first save");
            await world.FlushDirtyPersistenceAsync().ConfigureAwait(true);

            RecordingOutboundSink sink2 = new();
            AuthoritativeWorld restarted = await CreateWorldAsync(factory, sink2, time, repository).ConfigureAwait(true);
            await restarted.InitializeFromStoreAsync().ConfigureAwait(true);
            restarted.TryEnterWorld(Conn, Conn, Character("Buyer2", fx, fy, gold: 10)).Status.Should().Be(OpStatus.Ok);
            Step(restarted, 12);
            ulong merchant2 = sink2.Spawns.First(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.VillageMerchant).Snapshot.NetworkId;
            (await Run(restarted, restarted.Trade.SubmitTraderOpenAsync(Conn, merchant2))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot reloaded = sink2.TraderStocks.Last().Stock;
            reloaded.Epoch.Should().Be(stock.Epoch, "same clock, same restock window");
            reloaded.Remaining[slot].Should().Be((ushort)(stock.Remaining[slot] - 2), "the sale from the failed flush was retried, so it survives the restart");
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

    /// <summary>An in-memory trader store whose first save fails, like a database that is briefly unreachable.</summary>
    private sealed class FailFirstSaveTraderStockRepository : ITraderStockRepository
    {
        private readonly InMemoryTraderStockRepository _inner = new();
        private bool _failed;

        public Task<System.Collections.Generic.IReadOnlyList<PersistedTraderSales>> LoadAllAsync(System.Threading.CancellationToken ct = default) =>
            _inner.LoadAllAsync(ct);

        public Task SaveBatchAsync(System.Collections.Generic.IReadOnlyList<PersistedTraderSales> sales, System.Threading.CancellationToken ct = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("trader store unreachable");
            }

            return _inner.SaveBatchAsync(sales, ct);
        }
    }

    /// <summary>Boots a world like the host does: the gameplay secret comes from the database, the boot salt is fresh.</summary>
    private static async Task<AuthoritativeWorld> CreateWorldAsync(
        SqliteConnectionFactory factory, RecordingOutboundSink sink, TimeProvider time, ITraderStockRepository? traderStock = null)
    {
        GameplayEntropy entropy = await GameplayEntropy.LoadAsync(new SqliteGameplaySecretStore(factory), null, null, 30).ConfigureAwait(true);
        string configDir = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!;
        GameEconomy economy = GameEconomy.LoadFromDirectory(configDir);
        economy.Traders.Should().NotBeNull("traders.json ships with the server config");
        return new AuthoritativeWorld(
            Seed,
            32f,
            0.0008f,
            30,
            new SqliteChunkDeltaRepository(factory),
            new SqliteCharacterStore(factory),
            sink,
            time,
            economy: economy,
            traderStock: traderStock ?? new SqliteTraderStockRepository(factory),
            entropy: entropy);
    }

    private static CharacterRecord Character(string name, int tileX, int tileY, long gold, ItemSlot firstBagItem = default)
    {
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        if (!firstBagItem.IsEmpty)
        {
            bag.SetSlot(0, in firstBagItem);
        }

        return new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            10,
            0,
            (tileX + 0.5f) * 32f,
            (tileY + 0.5f) * 32f,
            100f,
            100f,
            SelectedHotbarSlot: 0,
            InventoryBlob: InventoryBinaryCodec.Serialize(bag),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Gold: gold);
    }

    // The tile in front of the nearest river village's market stall (free ground, beside the merchant).
    private static (int X, int Y) MerchantFrontTile() => FrontTileOf(VillageBuildingRole.Stall);

    // The tile in front of the entrance of the nearest river village's building with this role.
    private static (int X, int Y) FrontTileOf(VillageBuildingRole role)
    {
        ProceduralWorldGenerator gen = new(Seed);
        for (int ring = 0; ring < 6; ring++)
        {
            for (int sy = -ring; sy <= ring; sy++)
            {
                for (int sx = -ring; sx <= ring; sx++)
                {
                    if (gen.Landmarks.GetSite(sx, sy).Type != PointOfInterestType.RiverVillage)
                    {
                        continue;
                    }

                    foreach (VillagePlot plot in gen.Landmarks.GetLayout(sx, sy).Plots)
                    {
                        if (plot.Role == role)
                        {
                            return VillageBlueprint.FrontTile(VillageBuildingCatalog.Default, plot);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException($"No river village with a {role} near the origin.");
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
