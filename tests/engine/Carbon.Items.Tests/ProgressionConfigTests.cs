// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Progression;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class ProgressionConfigTests
{
    private static readonly ProgressionConfig s_config = ProgressionConfig.LoadShippedDefaults();

    private static readonly PrimaryAttributeId[] s_attributes =
    [
        PrimaryAttributeId.Vitality,
        PrimaryAttributeId.Wisdom,
        PrimaryAttributeId.Power,
        PrimaryAttributeId.Agility,
        PrimaryAttributeId.Dexterity,
    ];

    [Fact]
    public void LoadShippedDefaults_ShippedFile_MatchesResourcesConfig()
    {
        string shipped = File.ReadAllText(Path.Combine(FindConfigDirectory(), "progression.json"));
        ProgressionConfig fromDisk = ProgressionConfig.LoadFromJson(shipped);

        fromDisk.MaxLevel.Should().Be(s_config.MaxLevel);
        for (int lv = 1; lv <= s_config.MaxLevel; lv++)
        {
            fromDisk.GetXpToNextLevel(lv).Should().Be(s_config.GetXpToNextLevel(lv), "the embedded copy is built from resources/config");
        }
    }

    [Fact]
    public void GetXpToNextLevel_EveryLevel_IsStrictlyIncreasingAndZeroAtCap()
    {
        uint previous = 0U;
        for (int lv = 1; lv < s_config.MaxLevel; lv++)
        {
            uint xp = s_config.GetXpToNextLevel(lv);
            xp.Should().BeGreaterThan(previous, $"level {lv} must need more experience than level {lv - 1}");
            previous = xp;
        }

        s_config.GetXpToNextLevel(s_config.MaxLevel).Should().Be(0U, "the max level cannot level up");
    }

    [Fact]
    public void GetXpToNextLevel_AtAnchors_ReturnsAnchorValues()
    {
        foreach (XpAnchor anchor in s_config.Experience.XpToNext)
        {
            s_config.GetXpToNextLevel(anchor.Level).Should().Be((uint)anchor.Xp);
        }
    }

    [Fact]
    public void GetTotalStatPoints_EveryLevel_IsMonotonicAndNeverBelowLegacyFourPerLevel()
    {
        s_config.GetTotalStatPoints(1).Should().Be(0);
        for (int lv = 2; lv <= s_config.MaxLevel; lv++)
        {
            s_config.GetTotalStatPoints(lv).Should().BeGreaterThan(s_config.GetTotalStatPoints(lv - 1), $"every level-up grants points (level {lv})");
            s_config.GetTotalStatPoints(lv).Should().BeGreaterThanOrEqualTo(
                (ushort)((lv - 1) * 4),
                "characters allocated under the old 4-per-level rule must never end up over budget");
        }
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(10, 9)]
    [InlineData(21, 5)]
    [InlineData(50, 10)]
    [InlineData(51, 6)]
    [InlineData(81, 7)]
    [InlineData(99, 7)]
    public void GetStatPointsForLevelUp_BracketsAndMilestones_GrantConfiguredPoints(int level, int expected) =>
        s_config.GetStatPointsForLevelUp(level).Should().Be(expected);

    [Fact]
    public void ComputeStats_HigherLevel_NeverLowersAnyStat()
    {
        GearStatBonus noGear = default;
        DerivedCharacterStats previous = s_config.ComputeStats(1, null, in noGear);
        for (int lv = 2; lv <= s_config.MaxLevel; lv++)
        {
            DerivedCharacterStats next = s_config.ComputeStats(lv, null, in noGear);
            AssertNoStatLower(previous, next, $"level {lv}");
            next.MaxHealth.Should().BeGreaterThan(previous.MaxHealth);
            previous = next;
        }
    }

    [Fact]
    public void ComputeStats_OneMorePointInAnyAttribute_NeverLowersAnyStatAndRaisesSomething()
    {
        GearStatBonus noGear = default;
        foreach (PrimaryAttributeId attr in s_attributes)
        {
            CharacterProgressionState state = new();
            DerivedCharacterStats previous = s_config.ComputeStats(50, state, in noGear);
            for (int points = 1; points <= 60; points++)
            {
                state.SetAttribute(attr, (ushort)points);
                DerivedCharacterStats next = s_config.ComputeStats(50, state, in noGear);
                AssertNoStatLower(previous, next, $"{attr} = {points}");
                next.Should().NotBe(previous, $"{attr} must affect at least one stat");
                previous = next;
            }
        }
    }

    [Fact]
    public void ComputeStats_AbsurdAllocation_RespectsEveryCap()
    {
        CharacterProgressionState state = new();
        foreach (PrimaryAttributeId attr in s_attributes)
        {
            state.SetAttribute(attr, 60_000);
        }

        GearStatBonus gear = new(0f, 0f, 0f, CritChance: 0.9f);
        DerivedCharacterStats stats = s_config.ComputeStats(s_config.MaxLevel, state, in gear);

        stats.CritChance.Should().Be(s_config.Caps.CritChance);
        stats.CritDamage.Should().Be(s_config.Caps.CritDamage);
        stats.AttackSpeedMultiplier.Should().Be(s_config.Caps.AttackSpeed);
        stats.MoveSpeedMultiplier.Should().Be(s_config.Caps.MoveSpeed);
        s_config.GetEvasionChance(in stats, attackerLevel: 1).Should().Be(s_config.Mitigation.Evasion.Cap);
        s_config.Mitigate(1000f, DamageKind.Physical, in stats, attackerLevel: 1).Should().Be(1000f * (1f - s_config.Mitigation.Physical.Cap));
        s_config.Mitigate(1000f, DamageKind.Magical, in stats, attackerLevel: 1).Should().Be(1000f * (1f - s_config.Mitigation.Magical.Cap));
    }

    [Fact]
    public void ComputeStats_LevelOneNoPointsNoGear_EqualsBaseStats()
    {
        GearStatBonus noGear = default;
        DerivedCharacterStats stats = s_config.ComputeStats(1, null, in noGear);
        StatBlock b = s_config.BaseStats;

        stats.MaxHealth.Should().Be(b.MaxHealth);
        stats.Defense.Should().Be(b.Defense);
        stats.AttackMultiplier.Should().Be(b.Attack);
        stats.CritDamage.Should().Be(b.CritDamage);
        stats.HealthRegenPerSecond.Should().BeApproximately(b.HealthRegen + (b.MaxHealth * s_config.HealthRegenMaxHealthFraction), 1e-4f);
    }

    [Fact]
    public void ComputeStats_Gear_AddsArmorDefenseAndPartToMagicResist()
    {
        GearStatBonus gear = new(MaxHealth: 40f, MaxMana: 10f, Defense: 20f, CritChance: 0.2f);
        GearStatBonus noGear = default;
        DerivedCharacterStats bare = s_config.ComputeStats(10, null, in noGear);
        DerivedCharacterStats geared = s_config.ComputeStats(10, null, in gear);

        (geared.MaxHealth - bare.MaxHealth).Should().BeApproximately(40f, 1e-3f);
        (geared.Defense - bare.Defense).Should().BeApproximately(20f, 1e-3f);
        (geared.MagicResist - bare.MagicResist).Should().BeApproximately(20f * s_config.ArmorMagicResistFraction, 1e-3f);
        (geared.CritChance - bare.CritChance).Should().BeApproximately(0.2f, 1e-4f);
    }

    [Fact]
    public void Mitigate_DefenseCurve_MatchesFormulaAndHasDiminishingReturns()
    {
        MitigationCurve curve = s_config.Mitigation.Physical;
        static float Reduction(float def, int level) => 1f - (s_config.Mitigate(1000f, DamageKind.Physical, Stats(defense: def), level) / 1000f);

        float expected = 100f / (100f + curve.K + (curve.PerAttackerLevel * 10));
        Reduction(100f, 10).Should().BeApproximately(expected, 1e-4f, "reduction = def / (def + K + perLevel * attackerLevel)");

        float first = Reduction(100f, 30) - Reduction(0f, 30);
        float second = Reduction(200f, 30) - Reduction(100f, 30);
        second.Should().BeLessThan(first, "each extra 100 defense must be worth less than the previous 100");

        Reduction(100f, 60).Should().BeLessThan(Reduction(100f, 10), "the same defense mitigates less against a higher-level attacker");
        Reduction(0f, 10).Should().Be(0f);
    }

    [Fact]
    public void Mitigate_MagicalHit_UsesMagicResistNotDefense()
    {
        DerivedCharacterStats tank = Stats(defense: 500f, magicResist: 0f);
        s_config.Mitigate(100f, DamageKind.Magical, in tank, 10).Should().Be(100f, "defense does not stop spells");
        s_config.Mitigate(100f, DamageKind.Physical, in tank, 10).Should().BeLessThan(100f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    public void Mitigate_NonPositiveDamage_ReturnsZero(float raw) =>
        s_config.Mitigate(raw, DamageKind.Physical, Stats(defense: 10f), 5).Should().Be(0f);

    [Fact]
    public void Mitigate_TinyHitAgainstHugeDefense_DealsAtLeastOne() =>
        s_config.Mitigate(1.2f, DamageKind.Physical, Stats(defense: 1e6f), 1).Should().Be(1f);

    [Fact]
    public void CombatRoll_ManyNonces_SucceedsAtRequestedRateAndIsDeterministic()
    {
        int crits = 0;
        int dodges = 0;
        const int Samples = 200_000;
        for (uint n = 1; n <= Samples; n++)
        {
            crits += CombatRoll.Roll(0.25f, n) ? 1 : 0;
            dodges += CombatRoll.Roll(0.25f, n, CombatRoll.EvasionSalt) ? 1 : 0;
        }

        (crits / (float)Samples).Should().BeApproximately(0.25f, 0.01f);
        (dodges / (float)Samples).Should().BeApproximately(0.25f, 0.01f);
        CombatRoll.Roll(0.5f, 42U).Should().Be(CombatRoll.Roll(0.5f, 42U), "rolls are a pure function of the nonce");
        CombatRoll.Roll(0f, 42U).Should().BeFalse();
        CombatRoll.Roll(1f, 42U).Should().BeTrue();
    }

    [Fact]
    public void ApplyCritical_Critical_MultipliesByCritDamage()
    {
        CombatRoll.ApplyCritical(100f, isCritical: true, critDamage: 1.8f).Should().BeApproximately(180f, 1e-3f);
        CombatRoll.ApplyCritical(100f, isCritical: false, critDamage: 1.8f).Should().Be(100f);
        CombatRoll.ApplyCritical(100f, isCritical: true, critDamage: 0.5f).Should().Be(100f, "a crit never lowers damage");
    }

    [Fact]
    public void ComputeBaseKillExperience_Formula_ClampsAndAppliesEliteAndNight()
    {
        KillExperienceConfig kill = s_config.Experience.Kill;
        s_config.ComputeBaseKillExperience(75f, 1, false, false).Should().Be((uint)MathF.Round((75f * kill.MaxHealthFactor) + kill.LevelFactor));
        s_config.ComputeBaseKillExperience(1f, 1, false, false).Should().Be((uint)kill.MinXp);
        s_config.ComputeBaseKillExperience(1e9f, 99, false, false).Should().Be((uint)kill.MaxXp);

        uint plain = s_config.ComputeBaseKillExperience(500f, 20, false, false);
        s_config.ComputeBaseKillExperience(500f, 20, true, false).Should().Be((uint)MathF.Round(plain * kill.EliteMultiplier));
        s_config.ComputeBaseKillExperience(500f, 20, false, true).Should().Be((uint)MathF.Round(plain * kill.NightMultiplier));
    }

    [Fact]
    public void GetLevelDifferenceMultiplier_GreyAndRedMonsters_PenalisedAndRewardedWithinBounds()
    {
        LevelDifferenceConfig diff = s_config.Experience.Kill.LevelDifference;
        s_config.GetLevelDifferenceMultiplier(30, 30).Should().Be(1f);
        s_config.GetLevelDifferenceMultiplier(30 - diff.GraceLevels, 30).Should().Be(1f, "inside the grace window");
        s_config.GetLevelDifferenceMultiplier(30 - diff.GraceLevels - 2, 30).Should().BeApproximately(1f - (2f * diff.PenaltyPerLevel), 1e-4f);
        s_config.GetLevelDifferenceMultiplier(1, 99).Should().Be(diff.MinMultiplier, "farming level-1 monsters at the cap pays the floor");
        s_config.GetLevelDifferenceMultiplier(32, 30).Should().BeApproximately(1f + (2f * diff.BonusPerLevel), 1e-4f);
        s_config.GetLevelDifferenceMultiplier(99, 1).Should().Be(diff.MaxMultiplier);
        s_config.ScaleKillExperience(10U, 1, 99).Should().BeGreaterThanOrEqualTo(1U);
    }

    [Fact]
    public void GetRespecCost_ByLevel_FreeForNewPlayersThenGrows()
    {
        RespecConfig respec = s_config.Respec;
        s_config.GetRespecCost(respec.FreeThroughLevel).Should().Be(0L);
        s_config.GetRespecCost(respec.FreeThroughLevel + 1).Should().Be(respec.BaseGold + (respec.GoldPerLevel * (respec.FreeThroughLevel + 1)));
        s_config.GetRespecCost(80).Should().BeGreaterThan(s_config.GetRespecCost(40));
    }

    [Fact]
    public void IsOverBudget_SpentMoreThanEarned_IsDetected()
    {
        CharacterProgressionState state = new();
        state.SetAttribute(PrimaryAttributeId.Power, s_config.GetTotalStatPoints(10));
        s_config.IsOverBudget(10, state).Should().BeFalse();
        s_config.GetUnspentStatPoints(10, state).Should().Be(0);

        state.AddAttribute(PrimaryAttributeId.Vitality, 1);
        s_config.IsOverBudget(10, state).Should().BeTrue();
        s_config.GetUnspentStatPoints(10, state).Should().Be(0, "unspent points never go negative");
    }

    [Fact]
    public void ComputeStats_ThroughPolicyWithEquippedArmor_IncludesGear()
    {
        ItemRules rules = ItemRules.LoadShippedDefaults();
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(PlayerInventoryLayout.HeadSlot, new ItemSlot(StarterGameCatalog.IronHelmet, 1, 0UL, 220f));

        DerivedCharacterStats bare = CharacterProgressionPolicy.ComputeStats(rules, s_config, 5, new InventoryContainer(PlayerInventoryLayout.TotalCapacity), byte.MaxValue, null);
        DerivedCharacterStats geared = CharacterProgressionPolicy.ComputeStats(rules, s_config, 5, inv, byte.MaxValue, null);

        (geared.Defense - bare.Defense).Should().BeApproximately(rules.GetArmorDefense(StarterGameCatalog.IronHelmet), 1e-3f);
    }

    [Fact]
    public void SumGear_AccessorySlots_CountTowardStats()
    {
        ItemRules rules = ItemRules.LoadShippedDefaults();
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(PlayerInventoryLayout.Accessory1Slot, new ItemSlot(StarterGameCatalog.IronHelmet, 1, 0UL, 220f));

        GearStatBonus gear = CharacterProgressionPolicy.SumGear(rules, inv, byte.MaxValue);

        gear.Defense.Should().BeApproximately(rules.GetArmorDefense(StarterGameCatalog.IronHelmet), 1e-3f, "slots 40..41 (accessories) add their item stats");
    }

    [Theory]
    [InlineData("\"maxLevel\": 99", "\"maxLevel\": 1")]
    [InlineData("{ \"level\": 5, \"xp\": 1700 }", "{ \"level\": 5, \"xp\": 300 }")]
    [InlineData("{ \"level\": 98, \"xp\": 870000 }", "{ \"level\": 97, \"xp\": 870000 }")]
    [InlineData("{ \"fromLevel\": 21, \"toLevel\": 50", "{ \"fromLevel\": 22, \"toLevel\": 50")]
    [InlineData("\"critChance\": 0.5,", "\"critChance\": 1.5,")]
    [InlineData("\"vitality\": { \"maxHealth\": 14,", "\"vitality\": { \"maxHealth\": -14,")]
    [InlineData("\"cap\": 0.75 },\n    \"magical\"", "\"cap\": 1.0 },\n    \"magical\"")]
    [InlineData("\"baseGold\": 50,", "\"baseGold\": -1,")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"unknownField\": 3,")]
    [InlineData("\"attack\": 1,", "\"attack\": 0.5,")]
    public void LoadFromJson_InvalidValue_Throws(string find, string replace)
    {
        string json = File.ReadAllText(Path.Combine(FindConfigDirectory(), "progression.json")).Replace("\r\n", "\n", StringComparison.Ordinal);
        json.Should().Contain(find, "the test edits a known line of the shipped file");
        string broken = json.Replace(find, replace, StringComparison.Ordinal);

        Action load = () => ProgressionConfig.LoadFromJson(broken);
        load.Should().Throw<InvalidDataException>();
    }

    private static DerivedCharacterStats Stats(float defense = 0f, float magicResist = 0f) =>
        new(100f, 50f, 100f, defense, 1f, 1f, 1f, magicResist);

    private static void AssertNoStatLower(DerivedCharacterStats before, DerivedCharacterStats after, string because)
    {
        after.MaxHealth.Should().BeGreaterThanOrEqualTo(before.MaxHealth, because);
        after.MaxMana.Should().BeGreaterThanOrEqualTo(before.MaxMana, because);
        after.MaxStamina.Should().BeGreaterThanOrEqualTo(before.MaxStamina, because);
        after.Defense.Should().BeGreaterThanOrEqualTo(before.Defense, because);
        after.MagicResist.Should().BeGreaterThanOrEqualTo(before.MagicResist, because);
        after.AttackMultiplier.Should().BeGreaterThanOrEqualTo(before.AttackMultiplier, because);
        after.SpellPowerMultiplier.Should().BeGreaterThanOrEqualTo(before.SpellPowerMultiplier, because);
        after.CritChance.Should().BeGreaterThanOrEqualTo(before.CritChance, because);
        after.CritDamage.Should().BeGreaterThanOrEqualTo(before.CritDamage, because);
        after.AttackSpeedMultiplier.Should().BeGreaterThanOrEqualTo(before.AttackSpeedMultiplier, because);
        after.MoveSpeedMultiplier.Should().BeGreaterThanOrEqualTo(before.MoveSpeedMultiplier, because);
        after.Evasion.Should().BeGreaterThanOrEqualTo(before.Evasion, because);
        after.HealthRegenPerSecond.Should().BeGreaterThanOrEqualTo(before.HealthRegenPerSecond, because);
        after.ManaRegenPerSecond.Should().BeGreaterThanOrEqualTo(before.ManaRegenPerSecond, because);
    }

    internal static string FindConfigDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "resources", "config");
            if (File.Exists(Path.Combine(candidate, "progression.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("resources/config/progression.json not found above the test output directory.");
    }
}
