// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Target domain classification used to evaluate tool/weapon appropriateness, damage scaling, and blade wear.
/// </summary>
public enum ToolTargetKind : byte
{
    /// <summary>Live combat target (monster or PvP adversary).</summary>
    Combat = 0,

    /// <summary>Botanical world prop (trees, bushes, cacti, mushrooms).</summary>
    Flora = 1,

    /// <summary>Mineral or rock formation (boulders, ore veins, crystals).</summary>
    Mineral = 2,
}

/// <summary>
/// Semantic outcome of swinging an active hotbar item against combat or harvest targets.
/// </summary>
public enum ToolStrikeOutcome : byte
{
    /// <summary>Tool or weapon matches the target cleanly.</summary>
    Optimal = 0,

    /// <summary>Using a combat sword to chop wood deals reduced damage and rapidly dulls the edge.</summary>
    SwordMisuseOnWood = 1,

    /// <summary>Striking hard rock with a sword or wood axe deflects cleanly and chips the blade.</summary>
    BladeBluntOnStone = 2,

    // 3 was WornOutTool: retired by ADR-035 (an item at 0 durability is destroyed, so none is ever "worn out").

    /// <summary>Matching tool type lacks the harvest tier required by this resource node.</summary>
    InsufficientTier = 4,
}

/// <summary>
/// Result of evaluating a melee weapon/tool strike against a combat entity.
/// </summary>
/// <param name="Damage">Effective damage before critical strike multiplier.</param>
/// <param name="DurabilityLoss">Wear deducted from the active tool/weapon on hit.</param>
/// <param name="Outcome">Semantic classification for HUD feedback.</param>
public readonly record struct CombatStrikeEvaluation(
    float Damage,
    float DurabilityLoss,
    ToolStrikeOutcome Outcome);

/// <summary>
/// Result of evaluating a harvest strike against a flora or mineral world prop.
/// </summary>
/// <param name="Damage">Effective damage dealt to prop durability when tier permits.</param>
/// <param name="EffectiveTier">Effective harvest tier passed into world prop damage check.</param>
/// <param name="DurabilityLoss">Wear deducted from the striking tool or weapon.</param>
/// <param name="Outcome">Semantic suitability classification for UI warnings and sparks.</param>
public readonly record struct PropStrikeEvaluation(
    float Damage,
    byte EffectiveTier,
    float DurabilityLoss,
    ToolStrikeOutcome Outcome);

/// <summary>
/// Central domain policy governing tool durability wear and wrong-tool misuse penalties. An item never strikes at 0
/// durability: it is destroyed by the wear step that empties it (ADR-035, <see cref="DurabilityWear"/>).
/// </summary>
public static class ToolUsagePolicy
{
    /// <summary>Highest flora tier an empty hand can punch down (oak, birch, willow, palm and acacia trees).</summary>
    public const byte BareHandFloraTier = 1;

    /// <summary>Damage of a bare-hand punch on a tree the hand can fell: slow, so the first axe is the obvious goal.</summary>
    public const float BareHandTreeDamage = 8f;

    /// <summary>
    /// Evaluates melee combat damage and durability wear for <paramref name="slot"/>.
    /// </summary>
    public static CombatStrikeEvaluation EvaluateCombatStrike(ItemRules rules, in ItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(rules);
        float upgradeMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        float baseDamage = rules.GetWeaponBaseDamage(slot.ItemId) * upgradeMult;
        if (slot.IsEmpty || !rules.Items.TryGet(slot.ItemId, out ItemArchetype? def) || def.MaxDurability <= 0f)
        {
            return new CombatStrikeEvaluation(baseDamage, 0f, ToolStrikeOutcome.Optimal);
        }

        float loss = def.Category == ItemCategory.Weapon ? 1.5f : 2.2f;
        return new CombatStrikeEvaluation(baseDamage, loss, ToolStrikeOutcome.Optimal);
    }

    /// <summary>
    /// Evaluates harvest potency, effective tier, blade wear, and misuse warnings for striking a world prop.
    /// </summary>
    public static PropStrikeEvaluation EvaluatePropStrike(
        ItemRules rules,
        in ItemSlot slot,
        ToolTargetKind targetKind,
        byte propRequiredTier)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (targetKind == ToolTargetKind.Combat)
        {
            CombatStrikeEvaluation combat = EvaluateCombatStrike(rules, in slot);
            return new PropStrikeEvaluation(combat.Damage, 0, combat.DurabilityLoss, combat.Outcome);
        }

        ItemArchetype? def = null;
        bool hasDef = !slot.IsEmpty && rules.Items.TryGet(slot.ItemId, out def);
        bool isDurabilityTool = hasDef && def is not null && def.MaxDurability > 0f;
        float upgradeMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in slot));

        // Bare hands: anything up to tier 1 flora comes down, trees only slowly (a fresh character starts with nothing).
        if (slot.IsEmpty && targetKind == ToolTargetKind.Flora && propRequiredTier > 0 && propRequiredTier <= BareHandFloraTier)
        {
            return new PropStrikeEvaluation(BareHandTreeDamage, BareHandFloraTier, 0f, ToolStrikeOutcome.Optimal);
        }

        if (def is { Category: ItemCategory.Weapon })
        {
            if (targetKind == ToolTargetKind.Mineral)
            {
                return new PropStrikeEvaluation(0f, 0, 6.0f, ToolStrikeOutcome.BladeBluntOnStone);
            }

            return new PropStrikeEvaluation(12f * upgradeMult, 1, 4.5f, ToolStrikeOutcome.SwordMisuseOnWood);
        }

        if (def is { Category: ItemCategory.Tool } && targetKind == ToolTargetKind.Mineral && !def.Capabilities.HasFlag(ToolCapabilities.MineMineral))
        {
            return new PropStrikeEvaluation(0f, 0, 4.0f, ToolStrikeOutcome.BladeBluntOnStone);
        }

        if (def is { Category: ItemCategory.Tool } && targetKind == ToolTargetKind.Flora && def.Capabilities.HasFlag(ToolCapabilities.MineMineral) && !def.Capabilities.HasFlag(ToolCapabilities.ChopFlora))
        {
            float pickOnWoodDmg = def.FloraHarvestDamage * 0.72f * upgradeMult;
            if (def.HarvestTier < propRequiredTier)
            {
                return new PropStrikeEvaluation(0f, def.HarvestTier, 1.5f, ToolStrikeOutcome.InsufficientTier);
            }

            return new PropStrikeEvaluation(pickOnWoodDmg, def.HarvestTier, 2.0f, ToolStrikeOutcome.Optimal);
        }

        float dmg = (targetKind == ToolTargetKind.Mineral
            ? rules.GetMineralHarvestDamage(slot.ItemId)
            : rules.GetFloraHarvestDamage(slot.ItemId)) * upgradeMult;
        byte tier = def?.HarvestTier ?? 0;

        if (tier < propRequiredTier)
        {
            return new PropStrikeEvaluation(0f, tier, isDurabilityTool ? 1.5f : 0f, ToolStrikeOutcome.InsufficientTier);
        }

        return new PropStrikeEvaluation(dmg, tier, isDurabilityTool ? 1.0f : 0f, ToolStrikeOutcome.Optimal);
    }

    /// <summary>
    /// Evaluates a strike that digs up a tree stump (ADR-040 D1): a pickaxe digs at its full mineral damage, an axe at
    /// <paramref name="axeEfficiency"/> of its wood damage, and bare hands (or anything else) at
    /// <paramref name="bareHandDamage"/>, slowly but never blocked. A stump needs no tool tier. Tools wear as usual and a
    /// blade dulls, as on wood.
    /// </summary>
    /// <param name="rules">Item tables.</param>
    /// <param name="slot">The held item (empty: bare hands).</param>
    /// <param name="bareHandDamage">Damage of a strike without a digging tool (<c>props.json</c> <c>trees.dig</c>).</param>
    /// <param name="axeEfficiency">Share of an axe's wood damage dealt to a stump.</param>
    /// <returns>The evaluation.</returns>
    public static PropStrikeEvaluation EvaluateStumpDig(ItemRules rules, in ItemSlot slot, float bareHandDamage, float axeEfficiency)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (slot.IsEmpty || !rules.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            return new PropStrikeEvaluation(bareHandDamage, 0, 0f, ToolStrikeOutcome.Optimal);
        }

        float upgradeMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        float wear = def.MaxDurability > 0f ? 1.0f : 0f;
        if (def.Category == ItemCategory.Weapon)
        {
            return new PropStrikeEvaluation(bareHandDamage, 0, 4.5f, ToolStrikeOutcome.SwordMisuseOnWood);
        }

        if (def.Capabilities.HasFlag(ToolCapabilities.MineMineral))
        {
            return new PropStrikeEvaluation(MathF.Max(bareHandDamage, rules.GetMineralHarvestDamage(slot.ItemId) * upgradeMult), def.HarvestTier, wear, ToolStrikeOutcome.Optimal);
        }

        if (def.Capabilities.HasFlag(ToolCapabilities.ChopFlora))
        {
            return new PropStrikeEvaluation(MathF.Max(bareHandDamage, rules.GetFloraHarvestDamage(slot.ItemId) * axeEfficiency * upgradeMult), def.HarvestTier, wear, ToolStrikeOutcome.Optimal);
        }

        return new PropStrikeEvaluation(bareHandDamage, 0, wear, ToolStrikeOutcome.Optimal);
    }
}
