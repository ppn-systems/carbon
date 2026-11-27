// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Items.S2C;
using Carbon.Economy;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static Carbon.Host.Tests.AuthoritativeWorldMultiplayerTests;

namespace Carbon.Host.Tests;

/// <summary>Trading with village traders through the real world, persistence included (ADR-025).</summary>
public sealed class TraderWorldTests
{
    private const ulong Seed = 1337UL;
    private const ulong Conn = 501UL;

    [Fact]
    public async Task Merchant_OpenBuySell_ValidatesStockEpochWalletAndRange_AndSalesPersist()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-trader-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(true);
            ManualTime time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L));
            (int fx, int fy) = MerchantFrontTile();

            RecordingOutboundSink sink = new();
            AuthoritativeWorld world = CreateWorld(factory, sink, time);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            world.TryEnterWorld(Conn, Conn, Character("Buyer", fx, fy, gold: 5_000)).Status.Should().Be(OpStatus.Ok);
            Step(world, 12);
            ulong merchant = sink.Spawns.First(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.VillageMerchant).Snapshot.NetworkId;

            (await Run(world, world.Trade.SubmitTraderOpenAsync(Conn, merchant))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot stock = sink.TraderStocks.Last(t => t.ConnectionId == Conn).Stock;
            stock.Kind.Should().Be(TraderStock.KindMerchant);
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
            AuthoritativeWorld restarted = CreateWorld(factory, sink2, time);
            await restarted.InitializeFromStoreAsync().ConfigureAwait(true);
            restarted.TryEnterWorld(Conn, Conn, Character("Buyer2", fx, fy, gold: 10)).Status.Should().Be(OpStatus.Ok);
            Step(restarted, 12);
            ulong merchant2 = sink2.Spawns.First(s => s.Snapshot.Kind == EntityKind.Npc && s.Snapshot.ArchetypeId == WorldActorCatalog.VillageMerchant).Snapshot.NetworkId;
            (await Run(restarted, restarted.Trade.SubmitTraderOpenAsync(Conn, merchant2))).Should().Be(OpStatus.Ok);
            TraderStockSnapshot reloaded = sink2.TraderStocks.Last().Stock;
            reloaded.Epoch.Should().Be(stock.Epoch);
            reloaded.ItemIds.Should().Equal(stock.ItemIds, "stock is derived from (seed, trader, epoch)");
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

    private static AuthoritativeWorld CreateWorld(SqliteConnectionFactory factory, RecordingOutboundSink sink, TimeProvider time)
    {
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
            traderStock: new SqliteTraderStockRepository(factory));
    }

    private static CharacterRecord Character(string name, int tileX, int tileY, long gold) => new(
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
        InventoryBlob: InventoryBinaryCodec.Serialize(new InventoryContainer(PlayerInventoryLayout.TotalCapacity)),
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        Gold: gold);

    // The tile in front of the nearest river village's market stall (free ground, beside the merchant).
    private static (int X, int Y) MerchantFrontTile()
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
                        if (plot.Role == VillageBuildingRole.Stall)
                        {
                            return VillageBlueprint.FrontTile(VillageBuildingCatalog.Default, plot);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("No river village with a market stall near the origin.");
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
