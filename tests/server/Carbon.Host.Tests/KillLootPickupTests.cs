// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commerce;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Entities;
using Carbon.Economy;
using Carbon.Economy.Config;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Items.Systems;
using Carbon.Maps.Building;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-019: kill loot (items, gold, diamonds) drops on the ground, reserved for the killer for a while.</summary>
public sealed class KillLootPickupTests
{
    private const ushort TickRate = 30;
    private const ulong KillTick = 1_000UL;
    private const ulong ProtectionTicks = 60UL * TickRate;
    private const ulong LifetimeTicks = 180UL * TickRate;
    private const float PickupRangeSq = 84f * 84f;

    /// <summary>The shipped economy with a guaranteed gold drop, so every kill drops a gold pile.</summary>
    private static readonly GameEconomy s_alwaysGold = LoadShippedEconomy(json => json.Replace("\"dropChancePercent\": 70", "\"dropChancePercent\": 100", StringComparison.Ordinal));

    /// <summary>Gets the shipped economy with a guaranteed gold drop.</summary>
    internal static GameEconomy AlwaysGoldEconomy => s_alwaysGold;

    [Fact]
    public void TryRollLoot_Kill_SpawnsAGoldPileAndDoesNotCreditTheWallet()
    {
        Harness h = new();

        h.Kill().Should().BeTrue();

        h.Killer.Wallet.Gold.Should().Be(0L, "kill gold now lies on the ground until it is picked up");
        h.Killer.Wallet.Sequence.Should().Be(0UL);
        h.Outbound.Pending.WalletSyncs.Should().BeEmpty("nothing was credited, so there is no balance to sync");
        GroundLootEntry gold = h.SingleGoldPile();
        gold.CurrencyAmount.Should().BePositive();
        gold.OwnerCharacterId.Should().Be(h.Killer.Session.CharacterId);
        gold.OwnerNetworkId.Should().Be(h.Killer.Session.PlayerNetworkId);
        gold.ProtectedUntilTick.Should().Be(KillTick + ProtectionTicks, "groundLoot.ownerProtectionSeconds is 60 at 30 Hz");
        gold.LifetimeTicks.Should().Be(LifetimeTicks, "groundLoot.killLootLifetimeSeconds is 180 at 30 Hz");
    }

    [Fact]
    public void TryRollLoot_Kill_ItemStacksAreReservedForTheKillerToo()
    {
        Harness h = new();

        h.Kill().Should().BeTrue();

        List<GroundLootEntry> items = [.. h.Entries().Where(e => !e.IsCurrency)];
        items.Should().NotBeEmpty("the slime table has a guaranteed drop");
        items.Should().OnlyContain(e => e.OwnerCharacterId == h.Killer.Session.CharacterId && e.LifetimeTicks == LifetimeTicks);
    }

    [Fact]
    public void TryPickup_OwnerDuringProtection_CreditsTheWalletAndQueuesAWalletSync()
    {
        Harness h = new();
        h.Kill();
        (ulong goldId, GroundLootEntry gold) = h.SingleGoldPileWithId();
        h.Tick = KillTick + 30UL;

        h.Inventory.TryPickup(h.Killer, goldId, PickupRangeSq).Should().BeTrue();

        h.Killer.Wallet.Gold.Should().Be(gold.CurrencyAmount);
        h.Outbound.Pending.WalletSyncs.Should().ContainSingle().Which.Should().Be(
            new OutboundWalletSync(h.Killer.Session.ConnectionId, gold.CurrencyAmount, 0L, h.Killer.Wallet.Sequence));
        h.DirtyMarks[h.Killer].Should().Be(1, "the new balance must be persisted");
        h.GroundLoot.TryGet(goldId, out _).Should().BeFalse("the whole pile was taken");
    }

    [Fact]
    public void TryPickup_OtherPlayerDuringProtection_IsRefusedThenAllowedAfterIt()
    {
        Harness h = new();
        h.Kill();
        (ulong goldId, GroundLootEntry gold) = h.SingleGoldPileWithId();

        h.Tick = KillTick + ProtectionTicks - 1UL;
        h.Inventory.TryPickup(h.Bystander, goldId, PickupRangeSq).Should().BeFalse("the killer still owns the pile");
        h.Bystander.Wallet.Gold.Should().Be(0L);
        h.Outbound.Pending.WalletSyncs.Should().BeEmpty();
        h.GroundLoot.TryGet(goldId, out _).Should().BeTrue();

        h.Tick = KillTick + ProtectionTicks;
        h.Inventory.TryPickup(h.Bystander, goldId, PickupRangeSq).Should().BeTrue("protection is over, anyone may take it");
        h.Bystander.Wallet.Gold.Should().Be(gold.CurrencyAmount);
        h.Killer.Wallet.Gold.Should().Be(0L);
    }

    [Fact]
    public void TryPickup_OtherPlayerDuringProtection_CannotTakeTheKillItemStacks()
    {
        Harness h = new();
        h.Kill();
        ulong itemId = h.Ids().First(id => h.GroundLoot.TryGet(id, out GroundLootEntry e) && !e.IsCurrency);
        h.Tick = KillTick + 30UL;

        h.Inventory.TryPickup(h.Bystander, itemId, PickupRangeSq).Should().BeFalse();
        h.Bystander.Inventory.Slots.ToArray().Should().OnlyContain(s => s.IsEmpty);

        h.Inventory.TryPickup(h.Killer, itemId, PickupRangeSq).Should().BeTrue();
    }

    [Fact]
    public void TryPickup_KillerReconnectsDuringProtection_KeepsTheReservation()
    {
        Harness h = new();
        h.Kill();
        (ulong goldId, GroundLootEntry gold) = h.SingleGoldPileWithId();
        h.Tick = KillTick + 300UL;

        PlayerState reconnected = h.ReconnectKiller();

        reconnected.Session.PlayerNetworkId.Should().NotBe(h.Killer.Session.PlayerNetworkId, "a reconnect gets a new entity");
        h.Inventory.TryPickup(h.Bystander, goldId, PickupRangeSq).Should().BeFalse("the reservation survives the owner's reconnect");
        h.Inventory.TryPickup(reconnected, goldId, PickupRangeSq).Should().BeTrue("the reservation is keyed by character, not network id");
        reconnected.Wallet.Gold.Should().Be(gold.CurrencyAmount);
    }

    [Fact]
    public void TryGetSpawnIdentity_OwnerReconnectedOrOffline_ReplicatesTheCurrentOrLastKnownNetworkId()
    {
        Harness h = new();
        h.Kill();
        (ulong goldId, _) = h.SingleGoldPileWithId();
        ulong killedAs = h.Killer.Session.PlayerNetworkId;

        _ = h.OnlineNetworkIds.Remove(h.Killer.Session.CharacterId);
        h.GroundLoot.TryGetSpawnIdentity(goldId, TickRate, out SpawnIdentity offline).Should().BeTrue();
        offline.LootOwnerNetworkId.Should().Be(killedAs, "an offline owner keeps the pile locked for everyone else");

        PlayerState reconnected = h.ReconnectKiller();
        h.GroundLoot.TryGetSpawnIdentity(goldId, TickRate, out SpawnIdentity online).Should().BeTrue();
        online.LootOwnerNetworkId.Should().Be(reconnected.Session.PlayerNetworkId, "the reconnected owner's client must see the pile as its own");
    }

    [Fact]
    public void CollectNearbyGroundLoot_ProtectedPile_IsAutoCollectedOnlyByTheOwner()
    {
        Harness h = new();
        h.Kill();
        (_, GroundLootEntry gold) = h.SingleGoldPileWithId();
        h.Tick = KillTick + 30UL;

        h.Inventory.CollectNearbyGroundLoot(h.Bystander, Vector2.Zero);
        h.Bystander.Wallet.Gold.Should().Be(0L, "auto-collect follows the same ownership rule as a manual pickup");

        h.Inventory.CollectNearbyGroundLoot(h.Killer, Vector2.Zero);
        h.Killer.Wallet.Gold.Should().Be(gold.CurrencyAmount);
    }

    [Fact]
    public void LootExpirationSystem_KillLoot_DespawnsAt180Seconds()
    {
        Harness h = new();
        h.Kill();
        (ulong goldId, _) = h.SingleGoldPileWithId();
        h.Simulation.TryGetEntityByNetworkId(goldId, out Entity pile).Should().BeTrue();
        h.Simulation.Entities.GetComponent<LootDropComponent>(pile).DespawnAtTick.Should().Be(KillTick + LifetimeTicks);
        LootExpirationSystem expiry = new();

        expiry.Update(h.Simulation, new FixedTickArgs(KillTick + LifetimeTicks - 1UL, 1f / TickRate, 0d));
        h.Simulation.Entities.IsAlive(pile).Should().BeTrue("the pile lies 180 s, not 120 s");

        expiry.Update(h.Simulation, new FixedTickArgs(KillTick + LifetimeTicks, 1f / TickRate, 0d));
        h.Simulation.Entities.IsAlive(pile).Should().BeFalse();
        h.GroundLoot.ForgetDeadStacks();
        h.GroundLoot.TryGet(goldId, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetSpawnIdentity_ProtectedGoldPile_ReplicatesCurrencyOwnerAndProtectionLeft()
    {
        Harness h = new();
        h.Kill();
        (ulong goldId, GroundLootEntry gold) = h.SingleGoldPileWithId();
        h.Tick = KillTick + (10UL * TickRate);

        h.GroundLoot.TryGetSpawnIdentity(goldId, TickRate, out SpawnIdentity identity).Should().BeTrue();

        identity.Kind.Should().Be(EntityKind.LootDrop);
        identity.LootCurrency.Should().Be(LootCurrency.Gold);
        identity.LootCount.Should().Be((int)gold.CurrencyAmount);
        identity.LootOwnerNetworkId.Should().Be(h.Killer.Session.PlayerNetworkId);
        identity.LootProtectionMs.Should().Be(50_000U);

        h.Tick = KillTick + ProtectionTicks;
        h.GroundLoot.TryGetSpawnIdentity(goldId, TickRate, out identity).Should().BeTrue();
        identity.LootOwnerNetworkId.Should().Be(0UL, "nobody owns it any more");
        identity.LootProtectionMs.Should().Be(0U);
    }

    [Fact]
    public void Spawn_PlayerDropNextToAProtectedKillStack_DoesNotMergeIntoIt()
    {
        Harness h = new();
        h.Kill();
        GroundLootEntry killStack = h.Entries().First(e => !e.IsCurrency);
        int before = h.GroundLoot.Count;

        h.GroundLoot.Spawn(Vector2.Zero, killStack.Item with { Quantity = 1 });

        h.GroundLoot.Count.Should().Be(before + 1, "a free drop must not top up, and so unlock, a reserved stack");
    }

    [Fact]
    public void LoadFromJson_GroundLootProtectionLongerThanLifetime_IsRejected()
    {
        string json = File.ReadAllText(Path.Combine(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!, GameEconomy.EconomyFile));
        EconomyConfig.LoadFromJson(json).GroundLoot.Should().Be(new GroundLootRules { OwnerProtectionSeconds = 60, KillLootLifetimeSeconds = 180 });

        Action invalid = () => EconomyConfig.LoadFromJson(json.Replace("\"ownerProtectionSeconds\": 60", "\"ownerProtectionSeconds\": 600", StringComparison.Ordinal));

        invalid.Should().Throw<InvalidDataException>().WithMessage("*groundLoot*");
    }

    private static GameEconomy LoadShippedEconomy(Func<string, string> patchEconomy)
    {
        string dir = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!;
        string economyJson = File.ReadAllText(Path.Combine(dir, GameEconomy.EconomyFile));
        patchEconomy(economyJson).Should().NotBe(economyJson, "the patch must apply to the shipped economy.json");
        return GameEconomy.Load(
            File.ReadAllText(Path.Combine(dir, GameEconomy.ItemsFile)),
            File.ReadAllText(Path.Combine(dir, GameEconomy.RecipesFile)),
            patchEconomy(economyJson),
            File.ReadAllText(Path.Combine(dir, GameEconomy.LootFile)));
    }

    private sealed class Harness
    {
        private readonly List<ulong> _lootIds = [];
        private ulong _nextNetworkId = 5_000UL;

        public Harness()
        {
            GameEconomy economy = s_alwaysGold;
            this.GroundLoot = new GroundLootService(
                this.Simulation,
                () => economy.ItemRules,
                this.AllocateLootId,
                () => this.Tick,
                (_, _) => { },
                characterId => this.OnlineNetworkIds.GetValueOrDefault(characterId));
            this.Inventory = new InventoryService(
                this.Simulation,
                new BuildingGrid(),
                32f,
                1UL,
                () => economy.ItemRules,
                () => economy,
                () => this.Tick,
                (_, _, _) => false,
                this.GroundLoot,
                this.Outbound,
                state => this.DirtyMarks[state] = this.DirtyMarks.GetValueOrDefault(state) + 1);
            this.Commerce = new EconomyService(42UL, TickRate, () => economy, () => this.Tick, this.GroundLoot, this.Inventory, this.Outbound, (_, _) => false);
            this.Killer = this.CreatePlayer(1UL, 900UL, "Killer");
            this.Bystander = this.CreatePlayer(2UL, 901UL, "Bystander");
        }

        public ulong Tick { get; set; } = KillTick;

        public WorldSimulation Simulation { get; } = new();

        public OutboundTickBuffer Outbound { get; } = new();

        public GroundLootService GroundLoot { get; }

        public InventoryService Inventory { get; }

        public EconomyService Commerce { get; }

        public PlayerState Killer { get; }

        public PlayerState Bystander { get; }

        public Dictionary<PlayerState, int> DirtyMarks { get; } = [];

        /// <summary>Gets the current player network id of each online character.</summary>
        public Dictionary<Guid, ulong> OnlineNetworkIds { get; } = [];

        /// <summary>The killer logs out and back in: same character, new connection and new network id.</summary>
        public PlayerState ReconnectKiller()
        {
            _ = this.OnlineNetworkIds.Remove(this.Killer.Session.CharacterId);
            this.Simulation.DespawnEntity(this.Killer.Entity);
            return this.CreatePlayer(3UL, 950UL, "Killer", this.Killer.Session.CharacterId);
        }

        public bool Kill() => this.Commerce.TryRollLoot(this.Killer, "slime", 3, 77UL, Vector2.Zero);

        public IEnumerable<ulong> Ids() => _lootIds.Where(id => this.GroundLoot.TryGet(id, out _));

        public IEnumerable<GroundLootEntry> Entries() =>
            this.Ids().Select(id => this.GroundLoot.TryGet(id, out GroundLootEntry e) ? e : default);

        public GroundLootEntry SingleGoldPile() => this.SingleGoldPileWithId().Entry;

        public (ulong Id, GroundLootEntry Entry) SingleGoldPileWithId()
        {
            List<(ulong Id, GroundLootEntry Entry)> piles =
            [
                .. this.Ids()
                    .Select(id => (id, this.GroundLoot.TryGet(id, out GroundLootEntry e) ? e : default))
                    .Where(x => x.Item2.Currency == CurrencyKind.Gold),
            ];
            piles.Should().ContainSingle("one kill drops one gold pile");
            return piles[0];
        }

        private ulong AllocateLootId()
        {
            ulong id = ++_nextNetworkId;
            _lootIds.Add(id);
            return id;
        }

        private PlayerState CreatePlayer(ulong connectionId, ulong networkId, string name, Guid characterId = default)
        {
            Guid id = characterId == Guid.Empty ? Guid.NewGuid() : characterId;
            ConnectedPlayerSession session = new(connectionId, connectionId, Guid.NewGuid(), id, name, networkId, 0, 0);
            this.OnlineNetworkIds[id] = networkId;
            CharacterRecord character = new(session.CharacterId, session.AccountId, name, 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            return new PlayerState(session, this.Simulation.SpawnNetworkEntity(networkId, Vector2.Zero, 7f), character, new InventoryContainer(PlayerInventoryLayout.TotalCapacity));
        }
    }
}
