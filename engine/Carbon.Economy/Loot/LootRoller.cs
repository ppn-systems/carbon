// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Config;
using Carbon.Primitives.Random;

namespace Carbon.Economy.Loot;

/// <summary>
/// Server-authoritative, deterministic, zero-allocation loot roller. The server seeds a
/// <see cref="DeterministicRandom"/> per kill from its secret roll key (<see cref="CreateRandom"/>), rolls, and sends the
/// result; clients never roll. Same key + table + context + pity state always yields the same loot (replayable audits).
/// </summary>
public static class LootRoller
{
    private const ulong Million = 1_000_000UL;

    /// <summary>
    /// Derives the per-roll random stream from the server's secret roll key (ADR-026), the tick and the source entity.
    /// </summary>
    /// <param name="key">Secret roll key of the tick's epoch (server-only, never the world seed).</param>
    /// <param name="tick">Simulation tick of the kill.</param>
    /// <param name="sourceId">Network id of the killed monster or gathered node.</param>
    /// <returns>A deterministic random stream unique to this roll.</returns>
    public static DeterministicRandom CreateRandom(in GameplayKey key, ulong tick, ulong sourceId) =>
        key.CreateRandom(GameplayDomain.Loot, tick, sourceId);

    /// <summary>
    /// Rolls <paramref name="table"/> once.
    /// </summary>
    /// <param name="table">Loot table of the source.</param>
    /// <param name="context">Kill facts.</param>
    /// <param name="random">Deterministic random stream (advanced by the roll).</param>
    /// <param name="pity">Looter's bad-luck protection state (updated by the roll).</param>
    /// <param name="output">Buffer receiving item stacks (duplicates are merged); size it with <see cref="LootTable.MaxDrops"/>.</param>
    /// <returns>Item count written plus gold and diamonds to credit.</returns>
    public static LootRollResult Roll(
        LootTable table,
        in LootContext context,
        ref DeterministicRandom random,
        ref LootPityState pity,
        Span<LootDrop> output)
    {
        ArgumentNullException.ThrowIfNull(table);

        int level = Math.Clamp(context.SourceLevel, 1, table.Config.MaxLevel);
        int count = 0;
        bool truncated = false;

        ReadOnlySpan<LootEntry> guaranteed = table.Guaranteed;
        for (int i = 0; i < guaranteed.Length; i++)
        {
            LootEntry entry = guaranteed[i];
            if (entry.AllowsLevel(level))
            {
                truncated |= !Emit(output, ref count, entry.ItemId, RollQuantity(entry, ref random));
            }
        }

        ReadOnlySpan<LootEntry> weighted = table.Weighted;
        int totalWeight = table.NothingWeight;
        for (int i = 0; i < weighted.Length; i++)
        {
            if (weighted[i].AllowsLevel(level))
            {
                totalWeight += weighted[i].Weight;
            }
        }

        int rolls = table.GetRollCount(context);
        for (int r = 0; r < rolls && totalWeight > 0; r++)
        {
            int pick = random.NextInt(0, totalWeight) - table.NothingWeight;
            if (pick < 0)
            {
                continue;
            }

            for (int i = 0; i < weighted.Length; i++)
            {
                LootEntry entry = weighted[i];
                if (!entry.AllowsLevel(level))
                {
                    continue;
                }

                pick -= entry.Weight;
                if (pick < 0)
                {
                    truncated |= !Emit(output, ref count, entry.ItemId, RollQuantity(entry, ref random));
                    break;
                }
            }
        }

        long gold = RollGold(table, context, ref random);
        int diamonds = RollDiamond(table, context, ref random, ref pity, out bool pityTriggered);
        return new LootRollResult(count, gold, diamonds, truncated, pityTriggered);
    }

    private static long RollGold(LootTable table, in LootContext context, ref DeterministicRandom random)
    {
        if (table.GoldBase == 0)
        {
            return 0;
        }

        GoldRules rules = table.Config.Gold;
        bool guaranteed = context.IsElite || context.IsBoss;
        if (!guaranteed && random.NextInt(0, 100) >= rules.DropChancePercent)
        {
            return 0;
        }

        long scaled = table.ScaledGoldMean(context);
        if (scaled <= 0)
        {
            return 0;
        }

        int variance = rules.VariancePercent;
        long factor = 100 - variance + random.NextInt(0, (2 * variance) + 1);
        return Math.Max(1L, scaled * factor / 100L);
    }

    private static int RollDiamond(LootTable table, in LootContext context, ref DeterministicRandom random, ref LootPityState pity, out bool pityTriggered)
    {
        pityTriggered = false;
        if (table.DiamondBasePpm == 0 || table.IsTrivial(context))
        {
            // Trivial kills neither drop diamonds nor advance pity, so farming low-level monsters is pointless.
            return 0;
        }

        long ppm = table.DiamondChancePpm(context);
        DiamondPityRules rules = table.Config.Diamond.Pity;
        if (rules.Enabled)
        {
            int streak = pity.KillsSinceDiamond;
            if (streak + 1 >= rules.HardCapKills)
            {
                ppm = (long)Million;
                pityTriggered = true;
            }
            else if (streak >= rules.SoftStartKills)
            {
                ppm += (long)rules.StepPpm * (streak - rules.SoftStartKills + 1);
                pityTriggered = true;
            }
        }

        if ((random.NextULong() % Million) < (ulong)Math.Min(ppm, (long)Million))
        {
            pity = new LootPityState(0);
            return 1;
        }

        pityTriggered = false;
        pity = new LootPityState(pity.KillsSinceDiamond + 1);
        return 0;
    }

    private static int RollQuantity(in LootEntry entry, ref DeterministicRandom random) =>
        entry.Max <= entry.Min ? entry.Min : random.NextInt(entry.Min, entry.Max + 1);

    private static bool Emit(Span<LootDrop> output, ref int count, uint itemId, int quantity)
    {
        for (int i = 0; i < count; i++)
        {
            if (output[i].ItemId == itemId)
            {
                output[i] = output[i] with { Quantity = output[i].Quantity + quantity };
                return true;
            }
        }

        if (count >= output.Length)
        {
            return false;
        }

        output[count++] = new LootDrop(itemId, quantity);
        return true;
    }
}
