// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Economy;
using Carbon.Economy.Config;
using Carbon.Economy.Currency;
using Carbon.Economy.Death;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;

namespace Carbon.Application.World.Commerce;

/// <summary>
/// The world's economy (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): config-driven monster loot rolled
/// on kills (item stacks and gold/diamond piles dropped as ground loot reserved for the killer, ADR-019) and wallet sync messages. Deterministic per (gameplay secret, tick, victim), ADR-026.
/// Called only under the world lock.
/// </summary>
internal sealed class EconomyService
{
    /// <summary>Most item stacks one kill can drop.</summary>
    public const int MaxLootStacksPerKill = 16;

    /// <summary>Squared reach to a trader NPC for trading.</summary>
    public const float VendorRangeSq = 175f * 175f;

    private readonly IGameplayEntropy _entropy;
    private readonly ushort _tickRate;
    private readonly Func<GameEconomy?> _economy;
    private readonly Func<ulong> _currentTick;
    private readonly GroundLootService _groundLoot;
    private readonly OutboundTickBuffer _outbound;

    /// <summary>Initializes a new instance of the <see cref="EconomyService"/> class.</summary>
    public EconomyService(
        IGameplayEntropy entropy,
        ushort tickRate,
        Func<GameEconomy?> economy,
        Func<ulong> currentTick,
        GroundLootService groundLoot,
        OutboundTickBuffer outbound)
    {
        _entropy = entropy ?? throw new ArgumentNullException(nameof(entropy));
        _tickRate = tickRate == 0 ? (ushort)30 : tickRate;
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _groundLoot = groundLoot ?? throw new ArgumentNullException(nameof(groundLoot));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
    }

    /// <summary>Gets whether the world runs an economy (traders, loot tables, currency).</summary>
    public bool IsEnabled => _economy() is not null;

    /// <summary>Maps a <see cref="WorldActorCatalog"/> monster archetype to its <c>loot.json</c> table id.</summary>
    /// <param name="archetypeId">Monster archetype id.</param>
    /// <returns>The loot table id, or <see langword="null"/> for actors without one.</returns>
    public static string? ResolveLootTableId(ushort archetypeId) => archetypeId switch
    {
        WorldActorCatalog.WildSlime => "slime",
        WorldActorCatalog.SkeletonArcher => "skeleton",
        WorldActorCatalog.OrcShaman => "orc",
        WorldActorCatalog.CrimsonDemon => "demon",
        WorldActorCatalog.RockGolem => "golem",
        WorldActorCatalog.FrostWolf => "wolf",
        WorldActorCatalog.AshWraith => "wraith",
        _ => null,
    };

    /// <summary>
    /// Rolls the config loot table of a killed monster and drops everything near the corpse: the item stacks and one pile
    /// each of gold and diamonds. The drops are reserved for <paramref name="killer"/> for
    /// <c>economy.json → groundLoot.ownerProtectionSeconds</c> and despawn after <c>killLootLifetimeSeconds</c>; picking a
    /// pile up credits the wallet (<see cref="InventoryService.TryPickup"/>).
    /// </summary>
    /// <remarks><paramref name="tableId"/> is the monster definition's <c>lootTableId</c> (falls back to <see cref="ResolveLootTableId"/>).</remarks>
    /// <returns><see langword="false"/> when no economy or table applies (caller falls back to legacy drops).</returns>
    public bool TryRollLoot(PlayerState killer, string? tableId, ushort monsterLevel, ulong victimNetworkId, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(killer);
        GameEconomy? economy = _economy();
        if (economy is null
            || tableId is null
            || !economy.Loot.TryGet(tableId, out LootTable? table))
        {
            return false;
        }

        ulong tick = _currentTick();
        LootContext context = new(monsterLevel, killer.Level, IsElite: table.IsElite, IsBoss: table.IsBoss);
        DeterministicRandom random = LootRoller.CreateRandom(_entropy.KeyAt(tick), tick, victimNetworkId);
        LootPityState pity = killer.LootPity;
        Span<LootDrop> drops = stackalloc LootDrop[MaxLootStacksPerKill];
        LootRollResult result = LootRoller.Roll(table, context, ref random, ref pity, drops);
        killer.LootPity = pity;

        LootClaim claim = this.CreateKillClaim(economy.Config.GroundLoot, killer.Session, tick);
        for (int i = 0; i < result.ItemCount; i++)
        {
            LootDrop drop = drops[i];
            float durability = economy.Items.TryGet(drop.ItemId, out ItemCatalogEntry? item) ? item.MaxDurability : 0f;
            ItemSlot stack = new(drop.ItemId, drop.Quantity, Durability: durability);
            Vector2 offset = new(((i % 4) - 1.5f) * 10f, i / 4 * 8f);
            _groundLoot.SpawnClaimed(position + offset, in stack, in claim);
        }

        _groundLoot.SpawnCurrency(position + new Vector2(-6f, -10f), CurrencyKind.Gold, result.Gold, in claim);
        _groundLoot.SpawnCurrency(position + new Vector2(6f, -10f), CurrencyKind.Diamond, result.Diamonds, in claim);
        return true;
    }

    /// <summary>
    /// Takes the death penalty from a player who just died (ADR-031): debits <c>economy.json → deathPenalty</c>'s share of
    /// their carried gold and drops exactly that amount as one to <c>maxPiles</c> gold piles around <paramref name="position"/>.
    /// The piles are reserved for the dead player for <c>ownerProtectionSeconds</c>, then anyone may take them. Sizes and
    /// scatter come from the secret gameplay entropy (ADR-026). Diamonds are never taken.
    /// </summary>
    /// <returns>The gold dropped (0 without an economy, below the minimum level, or with an empty purse).</returns>
    public long DropDeathGold(PlayerState victim, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(victim);
        GameEconomy? economy = _economy();
        if (economy is null)
        {
            return 0L;
        }

        DeathPenaltyRules rules = economy.Config.DeathPenalty;
        long loss = DeathGoldPenalty.ComputeLoss(rules, victim.Wallet.Gold, victim.Level);
        if (loss <= 0L)
        {
            return 0L;
        }

        ulong tick = _currentTick();
        if (!victim.Wallet.TrySpend(CurrencyKind.Gold, loss, CurrencyReason.DeathDrop, tick).IsOk)
        {
            return 0L;
        }

        DeterministicRandom random = _entropy.KeyAt(tick).CreateRandom(GameplayDomain.DeathDrop, tick, victim.Session.PlayerNetworkId);
        Span<long> piles = stackalloc long[DeathGoldPenalty.MaxPilesLimit];
        int count = DeathGoldPenalty.SplitIntoPiles(rules, loss, ref random, piles);
        ulong protectionTicks = (ulong)Math.Max(0, rules.OwnerProtectionSeconds) * _tickRate;
        ulong lifetimeTicks = (ulong)Math.Max(1, rules.LootLifetimeSeconds) * _tickRate;
        LootClaim claim = protectionTicks == 0UL
            ? new LootClaim(Guid.Empty, 0UL, tick, lifetimeTicks)
            : new LootClaim(victim.Session.CharacterId, victim.Session.PlayerNetworkId, tick + protectionTicks, lifetimeTicks);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = random.NextUnitVector2() * random.NextFloat(10f, 30f);
            _groundLoot.SpawnCurrency(position + offset, CurrencyKind.Gold, piles[i], in claim, CurrencyReason.DeathDrop);
        }

        this.QueueWalletSync(victim);
        return loss;
    }

    /// <summary>Queues the player's wallet balances for its client.</summary>
    public void QueueWalletSync(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _outbound.Pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
    }

    /// <summary>Builds the claim of a kill at <paramref name="tick"/>: owner protection and ground lifetime from config.</summary>
    private LootClaim CreateKillClaim(GroundLootRules rules, ConnectedPlayerSession killer, ulong tick)
    {
        ulong protectionTicks = (ulong)Math.Max(0, rules.OwnerProtectionSeconds) * _tickRate;
        ulong lifetimeTicks = (ulong)Math.Max(1, rules.KillLootLifetimeSeconds) * _tickRate;
        return protectionTicks == 0UL
            ? new LootClaim(Guid.Empty, 0UL, tick, lifetimeTicks)
            : new LootClaim(killer.CharacterId, killer.PlayerNetworkId, tick + protectionTicks, lifetimeTicks);
    }
}
