// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Serialization;

namespace Carbon.Items.Progression;

/// <summary>
/// Character progression tuning loaded from <c>resources/config/progression.json</c> (ADR-021): the experience curve,
/// kill experience, attribute point budget, per-level and per-attribute stat growth, caps, mitigation curves and the
/// respec price. The server builds it from its config snapshot and clients read the same synced document, so both
/// sides evaluate identical numbers. Evaluation is allocation-free; tables are precomputed by <see cref="LoadFromJson"/>.
/// </summary>
public sealed class ProgressionConfig
{
    /// <summary>Name of the config document (<c>progression.json</c>).</summary>
    public const string DocumentName = "progression";

    /// <summary>Highest supported schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    private uint[] _xpToNext = [];
    private ushort[] _totalStatPoints = [];

    /// <summary>Gets the schema version.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the maximum character level.</summary>
    public int MaxLevel { get; init; } = 99;

    /// <summary>Gets the experience curve and kill formula.</summary>
    public ExperienceConfig Experience { get; init; } = new();

    /// <summary>Gets the attribute point budget.</summary>
    public StatPointsConfig StatPoints { get; init; } = new();

    /// <summary>Gets the level-1 stats of a character with no points and no gear.</summary>
    public StatBlock BaseStats { get; init; } = new();

    /// <summary>Gets the stats gained per level above 1.</summary>
    public StatBlock PerLevel { get; init; } = new();

    /// <summary>Gets the stats gained per point in each attribute.</summary>
    public AttributeEffectsConfig Attributes { get; init; } = new();

    /// <summary>Gets the stat caps.</summary>
    public StatCapsConfig Caps { get; init; } = new();

    /// <summary>Gets the fraction of max health regenerated per second out of combat, on top of the flat regen.</summary>
    public float HealthRegenMaxHealthFraction { get; init; }

    /// <summary>Gets the fraction of equipped armor defense that also counts as magic resist.</summary>
    public float ArmorMagicResistFraction { get; init; } = 0.5f;

    /// <summary>Gets the mitigation and evasion curves.</summary>
    public MitigationConfig Mitigation { get; init; } = new();

    /// <summary>Gets the attribute reset price.</summary>
    public RespecConfig Respec { get; init; } = new();

    /// <summary>Parses, validates and prepares a <c>progression.json</c> payload.</summary>
    /// <param name="json">JSON object text.</param>
    /// <returns>The ready-to-use configuration.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload is empty, malformed or out of range.</exception>
    public static ProgressionConfig LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ProgressionConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, ItemsJsonContext.Default.ProgressionConfig)
                ?? throw new InvalidDataException("progression.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"progression.json is malformed: {ex.Message}", ex);
        }

        config.Validate();
        config.BuildTables();
        return config;
    }

    /// <summary>Loads the copy of <c>progression.json</c> embedded at build time (tests and config-less hosts).</summary>
    /// <returns>The shipped configuration.</returns>
    public static ProgressionConfig LoadShippedDefaults()
    {
        using Stream stream = typeof(ProgressionConfig).Assembly.GetManifestResourceStream("Carbon.Items.Config.progression.json")
            ?? throw new InvalidOperationException("Embedded default config 'progression.json' is missing.");
        using StreamReader reader = new(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    /// <summary>Gets the experience needed to go from <paramref name="level"/> to the next level (0 at max level).</summary>
    /// <param name="level">Current level.</param>
    /// <returns>Experience to the next level.</returns>
    public uint GetXpToNextLevel(int level)
    {
        int lv = Math.Clamp(level, 1, this.MaxLevel);
        return lv >= this.MaxLevel ? 0U : _xpToNext[lv];
    }

    /// <summary>Gets the attribute points granted on reaching <paramref name="level"/> (0 for level 1).</summary>
    /// <param name="level">Level reached.</param>
    /// <returns>Points granted by that level-up.</returns>
    public int GetStatPointsForLevelUp(int level)
    {
        int lv = Math.Clamp(level, 1, this.MaxLevel);
        return lv <= 1 ? 0 : _totalStatPoints[lv] - _totalStatPoints[lv - 1];
    }

    /// <summary>Gets the total attribute points earned by <paramref name="level"/>.</summary>
    /// <param name="level">Character level.</param>
    /// <returns>Cumulative points.</returns>
    public ushort GetTotalStatPoints(int level) => _totalStatPoints[Math.Clamp(level, 1, this.MaxLevel)];

    /// <summary>Gets the unspent attribute points of <paramref name="state"/> at <paramref name="level"/> (never negative).</summary>
    /// <param name="level">Character level.</param>
    /// <param name="state">Character allocations.</param>
    /// <returns>Unspent points.</returns>
    public ushort GetUnspentStatPoints(int level, CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return (ushort)Math.Max(0, this.GetTotalStatPoints(level) - state.GetTotalSpentAttributePoints());
    }

    /// <summary>Gets whether <paramref name="state"/> spent more points than <paramref name="level"/> grants (e.g. after a config change).</summary>
    /// <param name="level">Character level.</param>
    /// <param name="state">Character allocations.</param>
    /// <returns><see langword="true"/> when the allocation is over budget.</returns>
    public bool IsOverBudget(int level, CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.GetTotalSpentAttributePoints() > this.GetTotalStatPoints(level);
    }

    /// <summary>Gets the kill experience multiplier for a <paramref name="monsterLevel"/> kill by a <paramref name="playerLevel"/> player.</summary>
    /// <param name="monsterLevel">Monster level.</param>
    /// <param name="playerLevel">Player level.</param>
    /// <returns>The multiplier.</returns>
    public float GetLevelDifferenceMultiplier(int monsterLevel, int playerLevel)
    {
        LevelDifferenceConfig diff = this.Experience.Kill.LevelDifference;
        int delta = monsterLevel - playerLevel;
        if (delta >= 0)
        {
            return MathF.Min(diff.MaxMultiplier, 1f + (delta * diff.BonusPerLevel));
        }

        int below = -delta - diff.GraceLevels;
        return below <= 0 ? 1f : MathF.Max(diff.MinMultiplier, 1f - (below * diff.PenaltyPerLevel));
    }

    /// <summary>Computes the experience a monster pays, before the level-difference multiplier.</summary>
    /// <param name="monsterMaxHealth">Scaled monster max health.</param>
    /// <param name="monsterLevel">Monster level.</param>
    /// <param name="isElite">Whether it is an elite.</param>
    /// <param name="isNight">Whether it spawned at night.</param>
    /// <returns>Base kill experience.</returns>
    public uint ComputeBaseKillExperience(float monsterMaxHealth, int monsterLevel, bool isElite, bool isNight)
    {
        KillExperienceConfig kill = this.Experience.Kill;
        float xp = Math.Clamp((MathF.Max(0f, monsterMaxHealth) * kill.MaxHealthFactor) + (Math.Max(1, monsterLevel) * kill.LevelFactor), kill.MinXp, kill.MaxXp);
        xp *= isElite ? kill.EliteMultiplier : 1f;
        xp *= isNight ? kill.NightMultiplier : 1f;
        return (uint)MathF.Round(xp);
    }

    /// <summary>Scales <paramref name="baseExperience"/> by the level difference between monster and player.</summary>
    /// <param name="baseExperience">Experience from <see cref="ComputeBaseKillExperience"/>.</param>
    /// <param name="monsterLevel">Monster level.</param>
    /// <param name="playerLevel">Player level.</param>
    /// <returns>Experience granted (at least 1 for a non-zero base).</returns>
    public uint ScaleKillExperience(uint baseExperience, int monsterLevel, int playerLevel)
    {
        if (baseExperience == 0U)
        {
            return 0U;
        }

        float scaled = baseExperience * this.GetLevelDifferenceMultiplier(monsterLevel, playerLevel);
        return Math.Max(1U, (uint)MathF.Round(scaled));
    }

    /// <summary>
    /// Computes derived stats from level, allocations and summed gear bonuses, then applies the caps.
    /// </summary>
    /// <param name="level">Character level.</param>
    /// <param name="progression">Allocations (<see langword="null"/> = none).</param>
    /// <param name="gear">Summed equipment bonuses.</param>
    /// <returns>The derived stats.</returns>
    public DerivedCharacterStats ComputeStats(int level, CharacterProgressionState? progression, in GearStatBonus gear)
    {
        float levels = Math.Clamp(level, 1, this.MaxLevel) - 1;
        StatAccumulator acc = default;
        acc.Add(this.BaseStats, 1f);
        acc.Add(this.PerLevel, levels);
        if (progression is not null)
        {
            for (int i = 0; i < CharacterProgressionState.MaxAttributeSlots; i++)
            {
                PrimaryAttributeId id = (PrimaryAttributeId)i;
                ushort points = progression.GetAttribute(id);
                StatBlock? block = points == 0 ? null : this.Attributes.Get(id);
                if (block is not null)
                {
                    acc.Add(block, points);
                }
            }
        }

        acc.MaxHealth += gear.MaxHealth;
        acc.MaxMana += gear.MaxMana;
        acc.Defense += gear.Defense;
        acc.MagicResist += gear.Defense * this.ArmorMagicResistFraction;
        acc.CritChance += gear.CritChance;
        acc.AttackSpeed += gear.AttackSpeed;

        return new DerivedCharacterStats(
            acc.MaxHealth,
            acc.MaxMana,
            acc.MaxStamina,
            acc.Defense,
            acc.Attack,
            MathF.Min(this.Caps.MoveSpeed, acc.MoveSpeed),
            acc.SpellPower,
            acc.MagicResist,
            MathF.Min(this.Caps.CritChance, acc.CritChance),
            MathF.Min(this.Caps.CritDamage, acc.CritDamage),
            MathF.Min(this.Caps.AttackSpeed, acc.AttackSpeed),
            acc.Evasion,
            acc.HealthRegen + (acc.MaxHealth * this.HealthRegenMaxHealthFraction),
            acc.ManaRegen);
    }

    /// <summary>
    /// Mitigates a hit of <paramref name="rawDamage"/> by the defender's defense or magic resist against an attacker of
    /// <paramref name="attackerLevel"/>. Any positive hit deals at least 1.
    /// </summary>
    /// <param name="rawDamage">Incoming damage.</param>
    /// <param name="kind">Damage kind.</param>
    /// <param name="stats">Defender stats.</param>
    /// <param name="attackerLevel">Attacker level.</param>
    /// <returns>Damage after mitigation.</returns>
    public float Mitigate(float rawDamage, DamageKind kind, in DerivedCharacterStats stats, int attackerLevel)
    {
        if (!(rawDamage > 0f))
        {
            return 0f;
        }

        float reduction = kind == DamageKind.Magical
            ? this.Mitigation.Magical.Evaluate(stats.MagicResist, attackerLevel)
            : this.Mitigation.Physical.Evaluate(stats.Defense, attackerLevel);
        return MathF.Max(1f, rawDamage * (1f - reduction));
    }

    /// <summary>Gets the defender's dodge chance against an attacker of <paramref name="attackerLevel"/>.</summary>
    /// <param name="stats">Defender stats.</param>
    /// <param name="attackerLevel">Attacker level.</param>
    /// <returns>Dodge chance in <c>[0, evasion cap]</c>.</returns>
    public float GetEvasionChance(in DerivedCharacterStats stats, int attackerLevel) =>
        this.Mitigation.Evasion.Evaluate(stats.Evasion, attackerLevel);

    /// <summary>Gets the gold price of an attribute reset at <paramref name="level"/> (0 while still free).</summary>
    /// <param name="level">Character level.</param>
    /// <returns>Gold price.</returns>
    public long GetRespecCost(int level)
    {
        int lv = Math.Clamp(level, 1, this.MaxLevel);
        return lv <= this.Respec.FreeThroughLevel ? 0L : this.Respec.BaseGold + (this.Respec.GoldPerLevel * lv);
    }

    /// <summary>Checks every value against its allowed range.</summary>
    /// <exception cref="InvalidDataException">Thrown when a value is out of range or tables are inconsistent.</exception>
    public void Validate()
    {
        if (this.SchemaVersion < 1 || this.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"progression.json schemaVersion {this.SchemaVersion} is not supported (max {CurrentSchemaVersion}).");
        }

        if (this.MaxLevel < 2 || this.MaxLevel > 999)
        {
            throw new InvalidDataException("progression.json maxLevel must be in [2, 999].");
        }

        if (this.Experience is null || this.StatPoints is null || this.BaseStats is null || this.PerLevel is null ||
            this.Attributes is null || this.Caps is null || this.Mitigation is null || this.Respec is null ||
            this.Experience.Kill?.LevelDifference is null || this.Experience.XpToNext is null || this.StatPoints.Brackets is null)
        {
            throw new InvalidDataException("progression.json is missing a section.");
        }

        this.ValidateExperience();
        this.ValidateStatPoints();
        this.ValidateStats();
        this.Mitigation.Physical.Validate("physical");
        this.Mitigation.Magical.Validate("magical");
        this.Mitigation.Evasion.Validate("evasion");

        if (this.Respec.FreeThroughLevel < 0 || this.Respec.BaseGold < 0 || this.Respec.GoldPerLevel < 0 ||
            this.Respec.BaseGold > 1_000_000_000L || this.Respec.GoldPerLevel > 10_000_000L)
        {
            throw new InvalidDataException("progression.json respec needs freeThroughLevel, baseGold and goldPerLevel >= 0 (and sane).");
        }
    }

    private void ValidateExperience()
    {
        IReadOnlyList<XpAnchor> anchors = this.Experience.XpToNext;
        if (anchors.Count == 0 || anchors[0] is null || anchors[0].Level != 1)
        {
            throw new InvalidDataException("progression.json experience.xpToNext must start at level 1.");
        }

        for (int i = 0; i < anchors.Count; i++)
        {
            XpAnchor a = anchors[i] ?? throw new InvalidDataException("progression.json experience.xpToNext has a null anchor.");
            if (a.Xp < 1 || a.Xp > uint.MaxValue)
            {
                throw new InvalidDataException($"progression.json xpToNext at level {a.Level} must be in [1, {uint.MaxValue}].");
            }

            if (i > 0 && (a.Level <= anchors[i - 1].Level || a.Xp <= anchors[i - 1].Xp))
            {
                throw new InvalidDataException("progression.json experience.xpToNext must be strictly increasing in level and xp.");
            }
        }

        if (anchors[^1].Level < this.MaxLevel - 1 || anchors[^1].Level >= this.MaxLevel)
        {
            throw new InvalidDataException($"progression.json experience.xpToNext must end exactly at level {this.MaxLevel - 1}, the last level that can level up.");
        }

        KillExperienceConfig kill = this.Experience.Kill;
        LevelDifferenceConfig diff = kill.LevelDifference;
        if (!(kill.MaxHealthFactor >= 0f && kill.MaxHealthFactor <= 100f) ||
            !(kill.LevelFactor >= 0f && kill.LevelFactor <= 10_000f) ||
            !(kill.MinXp >= 0f && kill.MinXp <= kill.MaxXp && kill.MaxXp <= 1_000_000f) ||
            !(kill.EliteMultiplier >= 1f && kill.EliteMultiplier <= 100f) ||
            !(kill.NightMultiplier >= 1f && kill.NightMultiplier <= 10f))
        {
            throw new InvalidDataException("progression.json experience.kill is out of range (factors >= 0, minXp <= maxXp, multipliers >= 1).");
        }

        if (diff.GraceLevels < 0 || diff.GraceLevels > 100 ||
            !(diff.PenaltyPerLevel >= 0f && diff.PenaltyPerLevel <= 1f) ||
            !(diff.MinMultiplier > 0f && diff.MinMultiplier <= 1f) ||
            !(diff.BonusPerLevel >= 0f && diff.BonusPerLevel <= 1f) ||
            !(diff.MaxMultiplier >= 1f && diff.MaxMultiplier <= 10f))
        {
            throw new InvalidDataException("progression.json experience.kill.levelDifference is out of range.");
        }
    }

    private void ValidateStatPoints()
    {
        IReadOnlyList<StatPointBracket> brackets = this.StatPoints.Brackets;
        int expectedFrom = 2;
        for (int i = 0; i < brackets.Count; i++)
        {
            StatPointBracket b = brackets[i] ?? throw new InvalidDataException("progression.json statPoints.brackets has a null entry.");
            if (b.FromLevel != expectedFrom || b.ToLevel < b.FromLevel || b.PointsPerLevel < 0 || b.PointsPerLevel > 100)
            {
                throw new InvalidDataException($"progression.json statPoints.brackets must be contiguous from level 2 with 0..100 points per level (bracket {i}).");
            }

            expectedFrom = b.ToLevel + 1;
        }

        if (expectedFrom != this.MaxLevel + 1)
        {
            throw new InvalidDataException($"progression.json statPoints.brackets must cover levels 2..{this.MaxLevel}.");
        }

        if (this.StatPoints.MilestoneEvery < 0 || this.StatPoints.MilestoneBonus < 0 || this.StatPoints.MilestoneBonus > 1000)
        {
            throw new InvalidDataException("progression.json statPoints milestoneEvery and milestoneBonus must be >= 0.");
        }
    }

    private void ValidateStats()
    {
        StatBlock b = this.BaseStats;
        b.ValidateGrowth("baseStats");
        if (b.MaxHealth < 1f || b.Attack < 1f || b.SpellPower < 1f || b.CritDamage < 1f || b.AttackSpeed < 1f || b.MoveSpeed < 1f)
        {
            throw new InvalidDataException("progression.json baseStats needs maxHealth >= 1 and attack, spellPower, critDamage, attackSpeed, moveSpeed >= 1.");
        }

        this.PerLevel.ValidateGrowth("perLevel");
        this.Attributes.Vitality.ValidateGrowth("attributes.vitality");
        this.Attributes.Wisdom.ValidateGrowth("attributes.wisdom");
        this.Attributes.Power.ValidateGrowth("attributes.power");
        this.Attributes.Agility.ValidateGrowth("attributes.agility");
        this.Attributes.Dexterity.ValidateGrowth("attributes.dexterity");

        StatCapsConfig c = this.Caps;
        if (!(c.CritChance > 0f && c.CritChance <= 1f) ||
            !(c.CritDamage >= b.CritDamage && c.CritDamage <= 10f) ||
            !(c.AttackSpeed >= 1f && c.AttackSpeed <= 3f) ||
            !(c.MoveSpeed >= 1f && c.MoveSpeed <= 2f))
        {
            throw new InvalidDataException("progression.json caps out of range (critChance (0,1], critDamage >= base, attackSpeed [1,3], moveSpeed [1,2]).");
        }

        if (!(this.HealthRegenMaxHealthFraction >= 0f && this.HealthRegenMaxHealthFraction <= 0.2f) ||
            !(this.ArmorMagicResistFraction >= 0f && this.ArmorMagicResistFraction <= 2f))
        {
            throw new InvalidDataException("progression.json healthRegenMaxHealthFraction must be in [0, 0.2] and armorMagicResistFraction in [0, 2].");
        }
    }

    private void BuildTables()
    {
        IReadOnlyList<XpAnchor> anchors = this.Experience.XpToNext;
        uint[] xp = new uint[this.MaxLevel + 1];
        int seg = 0;
        for (int lv = 1; lv < this.MaxLevel; lv++)
        {
            while (seg + 1 < anchors.Count && anchors[seg + 1].Level <= lv)
            {
                seg++;
            }

            XpAnchor a = anchors[seg];
            if (seg + 1 >= anchors.Count || lv == a.Level)
            {
                xp[lv] = (uint)a.Xp;
                continue;
            }

            // Geometric interpolation keeps the growth rate smooth between anchors and the curve strictly monotonic.
            XpAnchor next = anchors[seg + 1];
            double t = (double)(lv - a.Level) / (next.Level - a.Level);
            double value = Math.Exp(Math.Log(a.Xp) + ((Math.Log(next.Xp) - Math.Log(a.Xp)) * t));
            xp[lv] = (uint)Math.Round(value);
        }

        ushort[] total = new ushort[this.MaxLevel + 1];
        int running = 0;
        for (int lv = 2; lv <= this.MaxLevel; lv++)
        {
            running += this.PointsForBracket(lv);
            if (this.StatPoints.MilestoneEvery > 0 && lv % this.StatPoints.MilestoneEvery == 0)
            {
                running += this.StatPoints.MilestoneBonus;
            }

            if (running > ushort.MaxValue)
            {
                throw new InvalidDataException("progression.json grants more than 65535 total attribute points.");
            }

            total[lv] = (ushort)running;
        }

        _xpToNext = xp;
        _totalStatPoints = total;
    }

    private int PointsForBracket(int level)
    {
        IReadOnlyList<StatPointBracket> brackets = this.StatPoints.Brackets;
        for (int i = 0; i < brackets.Count; i++)
        {
            if (level >= brackets[i].FromLevel && level <= brackets[i].ToLevel)
            {
                return brackets[i].PointsPerLevel;
            }
        }

        return 0;
    }

    private struct StatAccumulator
    {
        public float MaxHealth;
        public float MaxMana;
        public float MaxStamina;
        public float Defense;
        public float MagicResist;
        public float Attack;
        public float SpellPower;
        public float CritChance;
        public float CritDamage;
        public float AttackSpeed;
        public float MoveSpeed;
        public float Evasion;
        public float HealthRegen;
        public float ManaRegen;

        public void Add(StatBlock block, float scale)
        {
            this.MaxHealth += block.MaxHealth * scale;
            this.MaxMana += block.MaxMana * scale;
            this.MaxStamina += block.MaxStamina * scale;
            this.Defense += block.Defense * scale;
            this.MagicResist += block.MagicResist * scale;
            this.Attack += block.Attack * scale;
            this.SpellPower += block.SpellPower * scale;
            this.CritChance += block.CritChance * scale;
            this.CritDamage += block.CritDamage * scale;
            this.AttackSpeed += block.AttackSpeed * scale;
            this.MoveSpeed += block.MoveSpeed * scale;
            this.Evasion += block.Evasion * scale;
            this.HealthRegen += block.HealthRegen * scale;
            this.ManaRegen += block.ManaRegen * scale;
        }
    }
}
