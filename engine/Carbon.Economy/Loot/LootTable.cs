// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Config;

namespace Carbon.Economy.Loot;

/// <summary>
/// Compiled, immutable loot table: item codes resolved to ids and the gold/diamond level curves precomputed
/// into integer lookup tables, so <see cref="LootRoller"/> runs with integer math only and zero allocations.
/// </summary>
public sealed class LootTable
{
    private const int Million = 1_000_000;

    private readonly LootEntry[] _guaranteed;
    private readonly LootEntry[] _weighted;
    private readonly long[] _goldMeanByLevel;
    private readonly int[] _diamondPpmByLevel;

    internal LootTable(LootTableDefinition definition, LootEntry[] guaranteed, LootEntry[] weighted, EconomyConfig config)
    {
        this.Id = definition.Id;
        this.Theme = string.IsNullOrEmpty(definition.Theme) ? definition.Id : definition.Theme;
        this.Kind = definition.Kind;
        this.MinLevel = definition.MinLevel;
        this.MaxLevel = definition.MaxLevel;
        this.GoldBase = definition.GoldBase;
        this.DiamondBasePpm = definition.DiamondBasePpm;
        this.Rolls = definition.Rolls;
        this.EliteExtraRolls = definition.EliteExtraRolls;
        this.BossExtraRolls = definition.BossExtraRolls;
        this.NothingWeight = definition.NothingWeight;
        this.Config = config;
        _guaranteed = guaranteed;
        _weighted = weighted;

        _goldMeanByLevel = new long[config.MaxLevel + 1];
        _diamondPpmByLevel = new int[config.MaxLevel + 1];
        for (int level = 1; level <= config.MaxLevel; level++)
        {
            double mean = definition.GoldBase * Math.Pow(level, config.Gold.LevelExponent);
            _goldMeanByLevel[level] = definition.GoldBase == 0 ? 0 : Math.Max(1L, (long)Math.Round(mean, MidpointRounding.AwayFromZero));
            long ppm = definition.DiamondBasePpm * (100L + ((long)level * config.Diamond.LevelBonusPercentPerLevel)) / 100L;
            _diamondPpmByLevel[level] = (int)Math.Min(ppm, Million);
        }
    }

    /// <summary>Gets the source id.</summary>
    public string Id { get; }

    /// <summary>Gets the item theme tag.</summary>
    public string Theme { get; }

    /// <summary>Gets the source kind.</summary>
    public LootSourceKind Kind { get; }

    /// <summary>Gets the lowest designed source level.</summary>
    public int MinLevel { get; }

    /// <summary>Gets the highest designed source level.</summary>
    public int MaxLevel { get; }

    /// <summary>Gets the gold base.</summary>
    public int GoldBase { get; }

    /// <summary>Gets the diamond base chance (ppm).</summary>
    public int DiamondBasePpm { get; }

    /// <summary>Gets the weighted rolls per normal kill.</summary>
    public int Rolls { get; }

    /// <summary>Gets the extra elite rolls.</summary>
    public int EliteExtraRolls { get; }

    /// <summary>Gets the extra boss rolls.</summary>
    public int BossExtraRolls { get; }

    /// <summary>Gets the nothing weight.</summary>
    public int NothingWeight { get; }

    /// <summary>Gets the economy tunables this table was compiled with.</summary>
    public EconomyConfig Config { get; }

    /// <summary>Gets the guaranteed entries.</summary>
    public ReadOnlySpan<LootEntry> Guaranteed => _guaranteed;

    /// <summary>Gets the weighted entries.</summary>
    public ReadOnlySpan<LootEntry> Weighted => _weighted;

    /// <summary>
    /// Gets the upper bound of distinct stacks a roll can emit (size the output buffer with it).
    /// </summary>
    /// <param name="context">Roll context.</param>
    /// <returns>Maximum number of distinct item stacks.</returns>
    public int MaxDrops(in LootContext context) => _guaranteed.Length + this.GetRollCount(context);

    /// <summary>
    /// Gets the mean gold (before multipliers and drop chance) at <paramref name="level"/>.
    /// </summary>
    /// <param name="level">Monster level (clamped to <c>[1, maxLevel]</c>).</param>
    /// <returns>Mean gold.</returns>
    public long GetGoldMean(int level) => _goldMeanByLevel[this.ClampLevel(level)];

    /// <summary>
    /// Gets the diamond chance (ppm, before elite/boss/distance/pity) at <paramref name="level"/>.
    /// </summary>
    /// <param name="level">Monster level (clamped to <c>[1, maxLevel]</c>).</param>
    /// <returns>Chance in parts-per-million.</returns>
    public int GetBaseDiamondPpm(int level) => _diamondPpmByLevel[this.ClampLevel(level)];

    /// <summary>
    /// Gets the analytic expected gold per kill for <paramref name="context"/> (used for docs and balance tests).
    /// </summary>
    /// <param name="context">Roll context.</param>
    /// <returns>Expected gold.</returns>
    public double ExpectedGoldPerKill(in LootContext context)
    {
        double chance = context.IsElite || context.IsBoss ? 1d : this.Config.Gold.DropChancePercent / 100d;
        return chance * this.ScaledGoldMean(context);
    }

    /// <summary>
    /// Gets the per-kill diamond chance in ppm for <paramref name="context"/>, excluding pity.
    /// </summary>
    /// <param name="context">Roll context.</param>
    /// <returns>Chance in parts-per-million.</returns>
    public long DiamondChancePpm(in LootContext context)
    {
        if (this.DiamondBasePpm == 0 || this.IsTrivial(context))
        {
            return 0;
        }

        DiamondRules rules = this.Config.Diamond;
        long ppm = this.GetBaseDiamondPpm(context.SourceLevel);
        if (context.IsBoss)
        {
            ppm *= rules.BossMultiplier;
        }
        else if (context.IsElite)
        {
            ppm *= rules.EliteMultiplier;
        }

        ppm = ppm * (100L + ((long)this.ClampTier(context.DistanceTier) * rules.DistanceBonusPercentPerTier)) / 100L;
        return Math.Min(ppm, rules.MaxChancePpm);
    }

    internal long ScaledGoldMean(in LootContext context)
    {
        GoldRules rules = this.Config.Gold;
        long percent = 100;
        if (context.IsBoss)
        {
            percent *= rules.BossMultiplier;
        }
        else if (context.IsElite)
        {
            percent *= rules.EliteMultiplier;
        }

        percent = percent * (100L + ((long)this.ClampTier(context.DistanceTier) * rules.DistanceBonusPercentPerTier)) / 100L;
        if (this.IsTrivial(context))
        {
            percent = percent * rules.LowLevelMultiplierPercent / 100L;
        }

        return this.GetGoldMean(context.SourceLevel) * percent / 100L;
    }

    internal int GetRollCount(in LootContext context) =>
        this.Rolls + (context.IsBoss ? this.BossExtraRolls : context.IsElite ? this.EliteExtraRolls : 0);

    internal bool IsTrivial(in LootContext context) =>
        context.PlayerLevel > 0 && context.PlayerLevel - context.SourceLevel > this.Config.Gold.LowLevelGap;

    private int ClampLevel(int level) => Math.Clamp(level, 1, this.Config.MaxLevel);

    private int ClampTier(int tier) => Math.Clamp(tier, 0, this.Config.Gold.MaxDistanceTier);
}
