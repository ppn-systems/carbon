// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Props;

/// <summary>One growth stage of a tree in <c>props.json</c> <c>trees</c> (ADR-040 D1).</summary>
/// <param name="Seconds">Seconds the stage lasts before the tree grows to the next one (0 for mature: it stays).</param>
/// <param name="Durability">Durability of the stage (for young trees: the share of the mature durability, 0..1).</param>
/// <param name="Yield">Wood logs the stage drops when it is felled (or, for a stump, dug up).</param>
public readonly record struct TreeStageRule(float Seconds, float Durability, int Yield);

/// <summary>
/// How trees grow back (ADR-040 D1): a felled tree leaves a stump, which grows through sapling and young tree to mature.
/// Felling a young tree or a sapling leaves a stump again. Digging up a stump removes it for good. Part of
/// <see cref="PropRegrowthRules"/>.
/// </summary>
public sealed class TreeGrowthRules
{
    /// <summary>Initializes a new instance of the <see cref="TreeGrowthRules"/> class.</summary>
    /// <param name="stump">The stump stage.</param>
    /// <param name="sapling">The sapling stage.</param>
    /// <param name="young">The young stage (<see cref="TreeStageRule.Durability"/> is a share of the mature durability).</param>
    /// <param name="matureYield">Wood logs a felled mature tree drops.</param>
    /// <param name="saplingDropMin">Fewest sapling items a felled mature tree drops.</param>
    /// <param name="saplingDropMax">Most sapling items a felled mature tree drops.</param>
    /// <param name="stumpSaplingChance">Chance (0..1) that a dug-up stump drops one sapling item.</param>
    /// <param name="deferWithinTiles">A stage change waits while a player is within this many tiles (0: never waits).</param>
    /// <param name="digBareHandDamage">Damage of a bare-hand strike on a stump.</param>
    /// <param name="digAxeEfficiency">Share (0..1) of an axe's wood damage it deals to a stump; a pickaxe digs at full damage.</param>
    public TreeGrowthRules(
        TreeStageRule stump,
        TreeStageRule sapling,
        TreeStageRule young,
        int matureYield,
        int saplingDropMin,
        int saplingDropMax,
        float stumpSaplingChance,
        int deferWithinTiles,
        float digBareHandDamage,
        float digAxeEfficiency)
    {
        this.Stump = stump;
        this.Sapling = sapling;
        this.Young = young;
        this.MatureYield = matureYield;
        this.SaplingDropMin = saplingDropMin;
        this.SaplingDropMax = saplingDropMax;
        this.StumpSaplingChance = stumpSaplingChance;
        this.DeferWithinTiles = deferWithinTiles;
        this.DigBareHandDamage = digBareHandDamage;
        this.DigAxeEfficiency = digAxeEfficiency;
    }

    /// <summary>Gets the stump stage.</summary>
    public TreeStageRule Stump { get; }

    /// <summary>Gets the sapling stage.</summary>
    public TreeStageRule Sapling { get; }

    /// <summary>Gets the young stage; its durability is a share of the mature durability.</summary>
    public TreeStageRule Young { get; }

    /// <summary>Gets the wood logs a felled mature tree drops.</summary>
    public int MatureYield { get; }

    /// <summary>Gets the fewest sapling items a felled mature tree drops.</summary>
    public int SaplingDropMin { get; }

    /// <summary>Gets the most sapling items a felled mature tree drops.</summary>
    public int SaplingDropMax { get; }

    /// <summary>Gets the chance (0..1) that a dug-up stump drops one sapling item.</summary>
    public float StumpSaplingChance { get; }

    /// <summary>Gets the radius in tiles within which a player defers a stage change (0: never deferred).</summary>
    public int DeferWithinTiles { get; }

    /// <summary>Gets the damage of a bare-hand strike on a stump.</summary>
    public float DigBareHandDamage { get; }

    /// <summary>Gets the share of an axe's wood damage it deals to a stump.</summary>
    public float DigAxeEfficiency { get; }

    /// <summary>Gets the stage a tree grows into after <paramref name="stage"/>.</summary>
    /// <param name="stage">The current stage.</param>
    /// <returns>The next stage (mature stays mature).</returns>
    public static TreeGrowthStage NextStage(TreeGrowthStage stage) => stage switch
    {
        TreeGrowthStage.Stump => TreeGrowthStage.Sapling,
        TreeGrowthStage.Sapling => TreeGrowthStage.Young,
        TreeGrowthStage.Young or TreeGrowthStage.Mature => TreeGrowthStage.Mature,
        _ => TreeGrowthStage.Mature,
    };

    /// <summary>Gets the wood logs a tree at <paramref name="stage"/> drops when felled (a stump: when dug up).</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The number of logs.</returns>
    public int YieldOf(TreeGrowthStage stage) => stage switch
    {
        TreeGrowthStage.Stump => this.Stump.Yield,
        TreeGrowthStage.Sapling => this.Sapling.Yield,
        TreeGrowthStage.Young => this.Young.Yield,
        TreeGrowthStage.Mature => this.MatureYield,
        _ => 0,
    };

    /// <summary>Gets the ticks a tree stays at <paramref name="stage"/> (0 for mature: it stays).</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="tickRate">World ticks per second.</param>
    /// <returns>Ticks, at least 1 for a growing stage.</returns>
    public ulong StageTicks(TreeGrowthStage stage, int tickRate)
    {
        float seconds = stage switch
        {
            TreeGrowthStage.Stump => this.Stump.Seconds,
            TreeGrowthStage.Sapling => this.Sapling.Seconds,
            TreeGrowthStage.Young => this.Young.Seconds,
            TreeGrowthStage.Mature => 0f,
            _ => 0f,
        };
        return seconds > 0f ? Math.Max(1UL, (ulong)MathF.Round(seconds * Math.Max(1, tickRate))) : 0UL;
    }

    /// <summary>Gets the full-health state of a tree of <paramref name="type"/> at <paramref name="stage"/>.</summary>
    /// <param name="type">The tree type.</param>
    /// <param name="stage">The stage.</param>
    /// <returns>The state: the generated baseline for mature, else the stage's durability; stumps and saplings need no tool tier.</returns>
    public PropDestructibility StateOf(MapPropType type, TreeGrowthStage stage)
    {
        PropDestructibility mature = MapProp.BaselineDestructibility(type);
        return stage switch
        {
            TreeGrowthStage.Stump => PropDestructibility.Create(this.Stump.Durability, 0),
            TreeGrowthStage.Sapling => PropDestructibility.Create(this.Sapling.Durability, 0),
            TreeGrowthStage.Young => PropDestructibility.Create(MathF.Max(1f, MathF.Round(mature.MaxDurability * this.Young.Durability)), mature.HarvestTier),
            TreeGrowthStage.Mature => mature,
            _ => mature,
        };
    }
}
