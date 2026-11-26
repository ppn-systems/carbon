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

    /// <summary>Equipped tool has reached zero durability and performs at heavily degraded effectiveness.</summary>
    WornOutTool = 3,

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
/// Central domain policy governing tool durability wear, wrong-tool misuse penalties, and broken-tool degradation.
/// </summary>
public static class ToolUsagePolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="slot"/> holds a breakable tool/weapon whose durability is exhausted.
    /// </summary>
    public static bool IsWornOut(in ItemSlot slot)
    {
        if (slot.IsEmpty || !StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            return false;
        }

        return def.MaxDurability > 0f && slot.Durability <= 0f;
    }

    /// <summary>
    /// Evaluates melee combat damage and durability wear for <paramref name="slot"/>.
    /// </summary>
    public static CombatStrikeEvaluation EvaluateCombatStrike(in ItemSlot slot)
    {
        float upgradeMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        float baseDamage = StarterGameCatalog.GetWeaponBaseDamage(slot.ItemId) * upgradeMult;
        if (slot.IsEmpty || !StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def) || def.MaxDurability <= 0f)
        {
            return new CombatStrikeEvaluation(baseDamage, 0f, ToolStrikeOutcome.Optimal);
        }

        if (slot.Durability <= 0f)
        {
            return new CombatStrikeEvaluation(
                MathF.Max(8f, baseDamage * 0.45f),
                0f,
                ToolStrikeOutcome.WornOutTool);
        }

        float loss = slot.ItemId == StarterGameCatalog.IronSword ? 1.5f : 2.2f;
        return new CombatStrikeEvaluation(baseDamage, loss, ToolStrikeOutcome.Optimal);
    }

    /// <summary>
    /// Evaluates harvest potency, effective tier, blade wear, and misuse warnings for striking a world prop.
    /// </summary>
    public static PropStrikeEvaluation EvaluatePropStrike(
        in ItemSlot slot,
        ToolTargetKind targetKind,
        byte propRequiredTier)
    {
        if (targetKind == ToolTargetKind.Combat)
        {
            CombatStrikeEvaluation combat = EvaluateCombatStrike(in slot);
            return new PropStrikeEvaluation(combat.Damage, 0, combat.DurabilityLoss, combat.Outcome);
        }

        ItemArchetype? def = null;
        bool hasDef = !slot.IsEmpty && StarterGameCatalog.Items.TryGet(slot.ItemId, out def);
        bool isDurabilityTool = hasDef && def is not null && def.MaxDurability > 0f;
        float upgradeMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in slot));

        if (isDurabilityTool && slot.Durability <= 0f)
        {
            // Worn-out tools cannot cleave hard timber or stone veins, but still allow zero-tier bush foraging.
            return new PropStrikeEvaluation(
                targetKind == ToolTargetKind.Mineral ? 3f : 8f,
                0,
                0f,
                ToolStrikeOutcome.WornOutTool);
        }

        if (slot.ItemId == StarterGameCatalog.IronSword)
        {
            if (targetKind == ToolTargetKind.Mineral)
            {
                return new PropStrikeEvaluation(0f, 0, 6.0f, ToolStrikeOutcome.BladeBluntOnStone);
            }

            return new PropStrikeEvaluation(12f * upgradeMult, 1, 4.5f, ToolStrikeOutcome.SwordMisuseOnWood);
        }

        if (slot.ItemId == StarterGameCatalog.WoodAxe && targetKind == ToolTargetKind.Mineral)
        {
            return new PropStrikeEvaluation(0f, 0, 4.0f, ToolStrikeOutcome.BladeBluntOnStone);
        }

        if ((slot.ItemId is StarterGameCatalog.StonePickaxe or StarterGameCatalog.CrystalPickaxe) &&
            targetKind == ToolTargetKind.Flora &&
            def is not null)
        {
            float pickOnWoodDmg = StarterGameCatalog.GetFloraHarvestDamage(slot.ItemId) * 0.72f * upgradeMult;
            if (def.HarvestTier < propRequiredTier)
            {
                return new PropStrikeEvaluation(0f, def.HarvestTier, 1.5f, ToolStrikeOutcome.InsufficientTier);
            }

            return new PropStrikeEvaluation(pickOnWoodDmg, def.HarvestTier, 2.0f, ToolStrikeOutcome.Optimal);
        }

        float dmg = (targetKind == ToolTargetKind.Mineral
            ? StarterGameCatalog.GetMineralHarvestDamage(slot.ItemId)
            : StarterGameCatalog.GetFloraHarvestDamage(slot.ItemId)) * upgradeMult;
        byte tier = def?.HarvestTier ?? 0;

        if (tier < propRequiredTier)
        {
            return new PropStrikeEvaluation(0f, tier, isDurabilityTool ? 1.5f : 0f, ToolStrikeOutcome.InsufficientTier);
        }

        return new PropStrikeEvaluation(dmg, tier, isDurabilityTool ? 1.0f : 0f, ToolStrikeOutcome.Optimal);
    }

    /// <summary>
    /// Formats a concise HUD warning message when a tool strike suffers misuse, dulling, or insufficient tier.
    /// </summary>
    public static string GetMisuseWarning(ToolStrikeOutcome outcome, byte requiredTier) => outcome switch
    {
        ToolStrikeOutcome.SwordMisuseOnWood => "Sword dulled on wood (-dur)! Equip Wood Axe [1]",
        ToolStrikeOutcome.BladeBluntOnStone => "Blade chipped on rock! Equip Pickaxe [2]",
        ToolStrikeOutcome.WornOutTool => "Tool Worn Out! Stand near Village Guard / NPC to mend",
        ToolStrikeOutcome.InsufficientTier => $"Need Tier {requiredTier} Tool!",
        ToolStrikeOutcome.Optimal or _ => string.Empty,
    };
}
