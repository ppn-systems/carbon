// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commerce;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Economy;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class EconomyServiceTests
{
    private static readonly GameEconomy s_shipped = GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!);

    [Fact]
    public void ResolveLootTableId_KnownMonsterAndOther_MapsToTheTableOrNull()
    {
        EconomyService.ResolveLootTableId(WorldActorCatalog.WildSlime).Should().Be("slime");
        EconomyService.ResolveLootTableId(WorldActorCatalog.MasterBlacksmith).Should().BeNull();
    }

    [Fact]
    public void TryRollLoot_WithoutAnEconomy_FallsBackToLegacyDrops()
    {
        Harness h = new(economy: null);

        h.Commerce.TryRollLoot(h.Player, "slime", 1, 1UL, Vector2.Zero).Should().BeFalse();
        h.Commerce.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void TryRollLoot_ShippedSlimeTable_IsDeterministicPerVictimAndTick()
    {
        Harness first = new(s_shipped);
        Harness second = new(s_shipped);

        first.Commerce.TryRollLoot(first.Player, "slime", 3, 77UL, Vector2.Zero).Should().BeTrue();
        second.Commerce.TryRollLoot(second.Player, "slime", 3, 77UL, Vector2.Zero).Should().BeTrue();

        second.GroundLoot.Count.Should().Be(first.GroundLoot.Count);
        second.Player.Wallet.Gold.Should().Be(first.Player.Wallet.Gold);
        if (first.Player.Wallet.Sequence != 0UL)
        {
            first.Outbound.Pending.WalletSyncs.Should().ContainSingle().Which.Gold.Should().Be(first.Player.Wallet.Gold);
        }
    }

    [Fact]
    public void RunVendorOperation_WithoutAnEconomy_IsForbidden()
    {
        Harness h = new(economy: null) { NpcNearby = true };

        h.Commerce.RunVendorOperation(h.Player, Vector2.Zero, VendorOperation.Buy, 1, 1, []).Should().Be(OpStatus.Forbidden);
    }

    [Fact]
    public void RunVendorOperation_TradeAwayFromAnyTownNpc_IsOutOfRange()
    {
        Harness h = new(s_shipped) { NpcNearby = false };

        h.Commerce.RunVendorOperation(h.Player, Vector2.Zero, VendorOperation.Sell, 0, 1, []).Should().Be(OpStatus.OutOfRange);
    }

    [Fact]
    public void QueueWalletSync_StagesTheCurrentBalances()
    {
        Harness h = new(s_shipped);
        h.Player.Wallet.Restore(120L, 3L, 9UL);

        h.Commerce.QueueWalletSync(h.Player);

        h.Outbound.Pending.WalletSyncs.Should().Equal(new OutboundWalletSync(h.Player.Session.ConnectionId, 120L, 3L, 9UL));
    }

    private sealed class Harness
    {
        private ulong _nextNetworkId = 3000UL;

        public Harness(GameEconomy? economy)
        {
            ItemRules rules = economy?.ItemRules ?? ItemRules.LoadShippedDefaults();
            WorldSimulation simulation = new();
            this.GroundLoot = new GroundLootService(simulation, () => rules, () => ++_nextNetworkId, () => 50UL, (_, _) => { });
            this.Commerce = new EconomyService(TestGameplayEntropy.Create(42UL), 30, () => economy, () => 50UL, this.GroundLoot, this.Outbound, (_, _) => this.NpcNearby);

            ConnectedPlayerSession session = new(9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
            CharacterRecord character = new(
                session.CharacterId, session.AccountId, "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            this.Player = new PlayerState(session, simulation.SpawnNetworkEntity(900UL, Vector2.Zero, 7f), character, new InventoryContainer(PlayerInventoryLayout.TotalCapacity));
        }

        public OutboundTickBuffer Outbound { get; } = new();

        public GroundLootService GroundLoot { get; }

        public EconomyService Commerce { get; }

        public PlayerState Player { get; }

        public bool NpcNearby { get; init; }
    }
}
