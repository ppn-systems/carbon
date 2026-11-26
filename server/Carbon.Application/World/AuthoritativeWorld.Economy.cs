// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;

namespace Carbon.Application.World;

/// <summary>
/// Economy integration: config-driven monster loot tables and wallet currency credited on kills.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    private const int MaxLootStacksPerKill = 16;

    /// <summary>
    /// Gets the gold and diamond balances of the player on <paramref name="connectionId"/>.
    /// </summary>
    /// <param name="connectionId">Player connection.</param>
    /// <param name="gold">Gold balance.</param>
    /// <param name="diamonds">Diamond balance.</param>
    /// <returns><see langword="true"/> when the player is in the world.</returns>
    public bool TryGetWalletBalances(ulong connectionId, out long gold, out long diamonds)
    {
        lock (_gate)
        {
            if (_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                gold = state.Wallet.Gold;
                diamonds = state.Wallet.Diamonds;
                return true;
            }
        }

        gold = 0;
        diamonds = 0;
        return false;
    }

    /// <summary>
    /// Maps a <see cref="WorldActorCatalog"/> monster archetype to its <c>loot.json</c> table id.
    /// </summary>
    /// <param name="archetypeId">Monster archetype id.</param>
    /// <returns>The loot table id, or <see langword="null"/> when the monster still uses its legacy spec drops.</returns>
    public static string? ResolveLootTableId(ushort archetypeId) => archetypeId switch
    {
        WorldActorCatalog.WildSlime => "slime",
        WorldActorCatalog.SkeletonArcher => "skeleton",
        WorldActorCatalog.OrcShaman => "orc",
        WorldActorCatalog.CrimsonDemon => "demon",
        _ => null
    };

    /// <summary>
    /// Rolls the config loot table of a killed monster, spawns the item stacks as ground loot and credits gold and
    /// diamonds to the killer's wallet. Server-authoritative and deterministic per (world seed, tick, victim).
    /// </summary>
    /// <remarks><paramref name="tableId"/> is the monster definition's <c>lootTableId</c> (falls back to <see cref="ResolveLootTableId"/>).</remarks>
    /// <returns><see langword="false"/> when no economy or table applies (caller falls back to legacy drops).</returns>
    private bool TryRollEconomyLootLocked(ActivePlayerState killer, string? tableId, ushort monsterLevel, ulong victimNetworkId, Vector2 position)
    {
        if (_economy is null
            || tableId is null
            || !_economy.Loot.TryGet(tableId, out LootTable? table))
        {
            return false;
        }

        LootContext context = new(monsterLevel, killer.Level);
        DeterministicRandom random = LootRoller.CreateRandom(this.WorldSeed, _currentTick, victimNetworkId);
        LootPityState pity = killer.LootPity;
        Span<LootDrop> drops = stackalloc LootDrop[MaxLootStacksPerKill];
        LootRollResult result = LootRoller.Roll(table, context, ref random, ref pity, drops);
        killer.LootPity = pity;

        for (int i = 0; i < result.ItemCount; i++)
        {
            LootDrop drop = drops[i];
            float durability = _economy.Items.TryGet(drop.ItemId, out ItemCatalogEntry? item) ? item.MaxDurability : 0f;
            ItemSlot stack = new(drop.ItemId, drop.Quantity, Durability: durability);
            Vector2 offset = new(((i % 4) - 1.5f) * 10f, i / 4 * 8f);
            this.SpawnGroundLootLocked(position + offset, in stack);
        }

        // Rejections (wallet at cap) leave the balance unchanged; the drop is simply forfeited.
        _ = killer.Wallet.TryCredit(CurrencyKind.Gold, result.Gold, CurrencyReason.MonsterLoot, _currentTick);
        _ = killer.Wallet.TryCredit(CurrencyKind.Diamond, result.Diamonds, CurrencyReason.MonsterLoot, _currentTick);
        return true;
    }
}
