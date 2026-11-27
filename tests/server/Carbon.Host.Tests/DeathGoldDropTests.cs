// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commerce;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Economy;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-031: a dying player's gold penalty becomes ground piles (ADR-019 entities) reserved for them for
/// <c>deathPenalty.ownerProtectionSeconds</c>, then free for anyone.
/// </summary>
public sealed class DeathGoldDropTests
{
    private const ushort TickRate = 30;
    private const ulong DeathTick = 5_000UL;
    private const ulong ProtectionTicks = 120UL * TickRate; // shipped deathPenalty.ownerProtectionSeconds
    private const float PickupRangeSq = 84f * 84f;

    [Fact]
    public void DropDeathGold_Level20With5000Gold_DebitsExactlyTheGoldLyingOnTheGround()
    {
        Harness h = new();
        h.Victim.Level = 20;
        h.Victim.Wallet.Restore(5_000L, 7L, 0UL);

        long dropped = h.Commerce.DropDeathGold(h.Victim, Vector2.Zero);

        dropped.Should().Be(500L, "10% of 5,000 lies between the floor (40) and the cap (600) at level 20");
        h.Victim.Wallet.Gold.Should().Be(4_500L);
        h.Victim.Wallet.Diamonds.Should().Be(7L, "diamonds are never dropped");
        List<GroundLootEntry> piles = h.GoldPiles();
        piles.Should().HaveCount(2, "500 gold at 250 per pile is two piles");
        piles.Sum(p => p.CurrencyAmount).Should().Be(dropped, "the debit equals the coins dropped: nothing is created or destroyed");
        piles.Should().OnlyContain(p => p.OwnerCharacterId == h.Victim.Session.CharacterId && p.ProtectedUntilTick == DeathTick + ProtectionTicks);
        h.Outbound.Pending.WalletSyncs.Should().ContainSingle(w => w.ConnectionId == h.Victim.Session.ConnectionId && w.Gold == 4_500L);
    }

    [Fact]
    public void DropDeathGold_Level9_DropsNothing()
    {
        Harness h = new();
        h.Victim.Level = 9;
        h.Victim.Wallet.Restore(5_000L, 0L, 0UL);

        h.Commerce.DropDeathGold(h.Victim, Vector2.Zero).Should().Be(0L, "levels below deathPenalty.minLevel (10) are protected");

        h.Victim.Wallet.Gold.Should().Be(5_000L);
        h.GoldPiles().Should().BeEmpty();
    }

    [Fact]
    public void DeathGoldPile_Bystander_CanOnlyTakeItAfterTheOwnerWindow()
    {
        Harness h = new();
        h.Victim.Level = 20;
        h.Victim.Wallet.Restore(1_000L, 0L, 0UL);
        long dropped = h.Commerce.DropDeathGold(h.Victim, Vector2.Zero);
        dropped.Should().Be(100L);
        ulong[] ids = h.GoldPileIds();

        h.Tick = DeathTick + ProtectionTicks - 1UL;
        ids.Should().OnlyContain(id => !h.Inventory.TryPickup(h.Bystander, id, PickupRangeSq), "the dead player has priority during the window");
        h.Bystander.Wallet.Gold.Should().Be(0L);

        h.Tick = DeathTick + ProtectionTicks;
        ids.Should().OnlyContain(id => h.Inventory.TryPickup(h.Bystander, id, PickupRangeSq), "after the window anyone may loot it, like monster coins");
        h.Bystander.Wallet.Gold.Should().Be(dropped);
        h.Victim.Wallet.Gold.Should().Be(900L);
    }

    [Fact]
    public void DeathGoldPile_Owner_CanTakeItBackDuringTheWindow()
    {
        Harness h = new();
        h.Victim.Level = 20;
        h.Victim.Wallet.Restore(1_000L, 0L, 0UL);
        _ = h.Commerce.DropDeathGold(h.Victim, Vector2.Zero);

        h.Tick = DeathTick + 30UL;
        h.GoldPileIds().Should().OnlyContain(id => h.Inventory.TryPickup(h.Victim, id, PickupRangeSq));

        h.Victim.Wallet.Gold.Should().Be(1_000L, "the owner recovers exactly what they dropped");
    }

    private sealed class Harness
    {
        private readonly List<ulong> _lootIds = [];
        private ulong _nextNetworkId = 5_000UL;

        public Harness()
        {
            GameEconomy economy = KillLootPickupTests.AlwaysGoldEconomy;
            this.GroundLoot = new GroundLootService(
                this.Simulation,
                () => economy.ItemRules,
                this.AllocateLootId,
                () => this.Tick,
                (_, _) => { },
                _ => 0UL);
            this.Inventory = new InventoryService(
                this.Simulation,
                new BuildingGrid(),
                32f,
                TestGameplayEntropy.Create(1UL),
                () => economy.ItemRules,
                () => economy,
                () => this.Tick,
                (_, _, _) => false,
                this.GroundLoot,
                this.Outbound,
                _ => { });
            this.Commerce = new EconomyService(TestGameplayEntropy.Create(42UL), TickRate, () => economy, () => this.Tick, this.GroundLoot, this.Inventory, this.Outbound, (_, _) => false);
            this.Victim = this.CreatePlayer(1UL, 900UL, "Victim");
            this.Bystander = this.CreatePlayer(2UL, 901UL, "Bystander");
        }

        public ulong Tick { get; set; } = DeathTick;

        public WorldSimulation Simulation { get; } = new();

        public OutboundTickBuffer Outbound { get; } = new();

        public GroundLootService GroundLoot { get; }

        public InventoryService Inventory { get; }

        public EconomyService Commerce { get; }

        public PlayerState Victim { get; }

        public PlayerState Bystander { get; }

        public ulong[] GoldPileIds() =>
            [.. _lootIds.Where(id => this.GroundLoot.TryGet(id, out GroundLootEntry e) && e.Currency == CurrencyKind.Gold)];

        public List<GroundLootEntry> GoldPiles() =>
            [.. this.GoldPileIds().Select(id => this.GroundLoot.TryGet(id, out GroundLootEntry e) ? e : default)];

        private ulong AllocateLootId()
        {
            ulong id = ++_nextNetworkId;
            _lootIds.Add(id);
            return id;
        }

        private PlayerState CreatePlayer(ulong connectionId, ulong networkId, string name)
        {
            ConnectedPlayerSession session = new(connectionId, Guid.NewGuid(), Guid.NewGuid(), name, networkId, 0, 0);
            CharacterRecord character = new(session.CharacterId, session.AccountId, name, 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            return new PlayerState(session, this.Simulation.SpawnNetworkEntity(networkId, Vector2.Zero, 7f), character, new InventoryContainer(PlayerInventoryLayout.TotalCapacity));
        }
    }
}
