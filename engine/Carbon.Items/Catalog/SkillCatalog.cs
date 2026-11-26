// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Data.Abstractions;
using Carbon.Data.Registry;

namespace Carbon.Items.Catalog;

/// <summary>Execution behavior kind for an active character skill.</summary>
public enum SkillEffectKind : byte
{
    /// <summary>Spatial arc/circle strike hitting monsters in range and orientation.</summary>
    ArcStrike = 0,

    /// <summary>Self-targeted health &amp; stamina restoration aura.</summary>
    SelfRestore = 1,
}

/// <summary>Stat scaling source determining base ability potency.</summary>
public enum SkillScalingMode : byte
{
    /// <summary>Scales from active melee weapon strike + Power multiplier.</summary>
    PhysicalWeapon = 0,

    /// <summary>Scales from caster Wisdom &amp; spell focus without requiring a heavy melee weapon.</summary>
    ArcaneSpell = 1,

    /// <summary>Scales from caster maximum health &amp; stamina.</summary>
    VitalMaxHealth = 2,
}

/// <summary>Client presentation animation &amp; VFX archetype decoupled from numeric skill IDs.</summary>
public enum SkillAnimArchetype : byte
{
    /// <summary>360-degree spinning slash ring.</summary>
    SpinSlash = 0,

    /// <summary>Directional arcane cone wave.</summary>
    ArcaneWave = 1,

    /// <summary>Self healing/stamina burst aura.</summary>
    SelfAura = 2,
}

/// <summary>
/// Declarative data-driven skill definition supporting open skill registration without hardcoding IDs.
/// </summary>
public sealed record SkillDefinition(
    ushort Id,
    string Code,
    string DisplayName,
    string DefaultKeyLabel,
    string Summary,
    string AccentHex,
    SkillEffectKind EffectKind,
    SkillScalingMode ScalingMode,
    SkillAnimArchetype AnimArchetype,
    byte MaxRank,
    float BaseManaCost,
    float ManaPerRank,
    float BaseStaminaCost,
    float StaminaCostPerRank,
    float BaseCooldownSeconds,
    float CooldownReductionPerRank,
    float MinCooldownSeconds,
    float Radius,
    float HalfAngleRad,
    float BaseDamageMultiplier,
    float DamageBonusRatioPerRank,
    float BaseSelfHealRatio,
    float SelfHealRatioPerRank,
    float BaseStaminaRestore,
    float StaminaRestorePerRank,
    float KnockbackSpeed) : IDataDefinition<ushort>
{
    /// <summary>Evaluates rank-scaled combat &amp; resource costs for <paramref name="rank"/> (<c>1..MaxRank</c>).</summary>
    public SkillSpec EvaluateRank(byte rank)
    {
        byte effRank = Math.Clamp(rank, (byte)1, this.MaxRank);
        int bonusLevels = effRank - 1;
        return new SkillSpec(
            this.Id,
            $"{this.DisplayName} [{this.DefaultKeyLabel}]",
            ManaCost: this.BaseManaCost + (bonusLevels * this.ManaPerRank),
            StaminaCost: this.BaseStaminaCost + (bonusLevels * this.StaminaCostPerRank),
            CooldownSeconds: MathF.Max(this.MinCooldownSeconds, this.BaseCooldownSeconds - (bonusLevels * this.CooldownReductionPerRank)),
            Radius: this.Radius,
            HalfAngleRad: this.HalfAngleRad,
            DamageMultiplier: this.BaseDamageMultiplier > 0f
                ? this.BaseDamageMultiplier * (1f + (bonusLevels * this.DamageBonusRatioPerRank))
                : 0f,
            SelfHealRatio: this.BaseSelfHealRatio > 0f
                ? this.BaseSelfHealRatio + (bonusLevels * this.SelfHealRatioPerRank)
                : 0f,
            StaminaRestore: this.BaseStaminaRestore > 0f
                ? this.BaseStaminaRestore + (bonusLevels * this.StaminaRestorePerRank)
                : 0f);
    }
}

/// <summary>
/// Central AOT registry of active skills indexed by open <see cref="ushort"/> identifier.
/// </summary>
public static class SkillCatalog
{
    /// <summary>360-degree spinning cleave skill identifier.</summary>
    public const ushort WhirlwindId = 1;

    /// <summary>Long-range crystal shockwave cone skill identifier.</summary>
    public const ushort ArcaneNovaId = 2;

    /// <summary>Self restoration + stamina surge burst skill identifier.</summary>
    public const ushort VitalSurgeId = 3;

    /// <summary>Registered active skills ordered cleanly for UI &amp; server lookup.</summary>
    public static DefinitionRegistry<ushort, SkillDefinition> Skills { get; } = new(
    [
        new SkillDefinition(
            Id: WhirlwindId,
            Code: "whirlwind",
            DisplayName: "Whirlwind",
            DefaultKeyLabel: "Q",
            Summary: "360 Cleave Strike around hero",
            AccentHex: "#F97316",
            EffectKind: SkillEffectKind.ArcStrike,
            ScalingMode: SkillScalingMode.PhysicalWeapon,
            AnimArchetype: SkillAnimArchetype.SpinSlash,
            MaxRank: 5,
            BaseManaCost: 18f,
            ManaPerRank: 3f,
            BaseStaminaCost: 22f,
            StaminaCostPerRank: 0f,
            BaseCooldownSeconds: 3.5f,
            CooldownReductionPerRank: 0.35f,
            MinCooldownSeconds: 2.0f,
            Radius: 96f,
            HalfAngleRad: MathF.PI,
            BaseDamageMultiplier: 1.55f,
            DamageBonusRatioPerRank: 0.16f,
            BaseSelfHealRatio: 0f,
            SelfHealRatioPerRank: 0f,
            BaseStaminaRestore: 0f,
            StaminaRestorePerRank: 0f,
            KnockbackSpeed: 120f),
        new SkillDefinition(
            Id: ArcaneNovaId,
            Code: "arcane_nova",
            DisplayName: "Arcane Nova",
            DefaultKeyLabel: "R",
            Summary: "Long Arcane Shockwave Cone",
            AccentHex: "#38BDF8",
            EffectKind: SkillEffectKind.ArcStrike,
            ScalingMode: SkillScalingMode.ArcaneSpell,
            AnimArchetype: SkillAnimArchetype.ArcaneWave,
            MaxRank: 5,
            BaseManaCost: 30f,
            ManaPerRank: 3f,
            BaseStaminaCost: 10f,
            StaminaCostPerRank: 0f,
            BaseCooldownSeconds: 5.0f,
            CooldownReductionPerRank: 0.40f,
            MinCooldownSeconds: 2.4f,
            Radius: 175f,
            HalfAngleRad: 0.72f,
            BaseDamageMultiplier: 2.10f,
            DamageBonusRatioPerRank: 0.16f,
            BaseSelfHealRatio: 0f,
            SelfHealRatioPerRank: 0f,
            BaseStaminaRestore: 0f,
            StaminaRestorePerRank: 0f,
            KnockbackSpeed: 105f),
        new SkillDefinition(
            Id: VitalSurgeId,
            Code: "vital_surge",
            DisplayName: "Vital Surge",
            DefaultKeyLabel: "V",
            Summary: "Restore HP & Stamina Burst",
            AccentHex: "#34D399",
            EffectKind: SkillEffectKind.SelfRestore,
            ScalingMode: SkillScalingMode.VitalMaxHealth,
            AnimArchetype: SkillAnimArchetype.SelfAura,
            MaxRank: 5,
            BaseManaCost: 35f,
            ManaPerRank: 3f,
            BaseStaminaCost: 0f,
            StaminaCostPerRank: 0f,
            BaseCooldownSeconds: 8.0f,
            CooldownReductionPerRank: 0.55f,
            MinCooldownSeconds: 3.5f,
            Radius: 0f,
            HalfAngleRad: 0f,
            BaseDamageMultiplier: 0f,
            DamageBonusRatioPerRank: 0f,
            BaseSelfHealRatio: 0.40f,
            SelfHealRatioPerRank: 0.05f,
            BaseStaminaRestore: 45f,
            StaminaRestorePerRank: 7f,
            KnockbackSpeed: 0f),
    ]);

    /// <summary>Total learnable mastery ranks summed across all registered skills.</summary>
    public static int TotalLearnableRanks { get; } = ComputeTotalLearnableRanks();

    private static int ComputeTotalLearnableRanks()
    {
        int sum = 0;
        ReadOnlySpan<SkillDefinition> all = Skills.All;
        for (int i = 0; i < all.Length; i++)
        {
            sum += all[i].MaxRank;
        }

        return sum;
    }
}
