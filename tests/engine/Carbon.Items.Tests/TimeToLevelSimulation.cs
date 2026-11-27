// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Combat.Attacks;
using Carbon.Contracts.Entities;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Progression;

namespace Carbon.Items.Tests;

/// <summary>Attribute split of a simulated build (fractions of the point budget).</summary>
internal sealed record SimulatedBuild(string Name, float Vitality, float Wisdom, float Power, float Agility, float Dexterity);

/// <summary>One simulated level: grinding rate and time to the next level.</summary>
internal readonly record struct SimulatedLevel(
    int Level,
    float KillsPerHour,
    float XpPerMinute,
    float MinutesToNext,
    float DayTimeToKillSeconds,
    float HealthLostPerKill);

/// <summary>
/// Deterministic time-to-level model (docs/progression.md §5). It plays an engaged melee player fighting normal
/// monsters of their own level, using the real tables: <c>progression.json</c> (stats, mitigation, kill XP, curve),
/// <c>monsters.json</c> + <c>spawning.json</c> (pool by level, stat curves, night empowerment), <c>items.json</c>
/// (gear by level) and the server constants (2 swings/s, 250 u/s walk). Per kill cycle:
/// <c>time to kill + walk to the next monster + fixed overhead + rest to heal the damage taken</c>.
/// </summary>
internal static class TimeToLevelSimulation
{
    /// <summary>Server attack cooldown: <c>max(10, tickRate / 2)</c> ticks at 30 Hz (PlayerActionService).</summary>
    public const float BaseAttackIntervalSeconds = 0.5f;

    /// <summary>Server walk speed in world units per second (AuthoritativeWorld.BaseWalkSpeed).</summary>
    public const float WalkSpeed = 250f;

    /// <summary>Monsters per 1280x720 screen in a populated area by day and at night (PR #67 density budget).</summary>
    public const float DayMonstersPerScreen = 3f;

    /// <summary>Night density (PR #67).</summary>
    public const float NightMonstersPerScreen = 5f;

    /// <summary>Screen area in world units.</summary>
    public const float ScreenArea = 1280f * 720f;

    /// <summary>Per-kill overhead: approach and positioning, loot pickup, dodging telegraphs.</summary>
    public const float OverheadSecondsPerKill = 6f;

    /// <summary>Seconds a monster keeps attacking beyond the time to kill (wind-ups already started).</summary>
    public const float EngagementSlackSeconds = 0.8f;

    /// <summary>Fraction of play time spent fighting (the rest: selling, crafting, repairs, inventory, travel).</summary>
    public const float PlayEfficiency = 0.6f;

    public static readonly SimulatedBuild Balanced = new("balanced", 0.25f, 0f, 0.35f, 0.15f, 0.25f);
    public static readonly SimulatedBuild Tank = new("tank", 0.6f, 0f, 0.3f, 0f, 0.1f);
    public static readonly SimulatedBuild GlassCannon = new("glass cannon", 0f, 0f, 0.6f, 0f, 0.4f);
    public static readonly SimulatedBuild Skirmisher = new("skirmisher", 0.2f, 0f, 0.3f, 0.4f, 0.1f);

    private static readonly Lazy<SimulationData> s_data = new(SimulationData.Load);

    public static ProgressionConfig Config => s_data.Value.Progression;

    public static SimulatedLevel Simulate(int level, SimulatedBuild build)
    {
        SimulationData data = s_data.Value;
        ProgressionConfig config = data.Progression;
        CharacterProgressionState allocations = Allocate(config.GetTotalStatPoints(level), build);
        InventoryContainer inventory = Gear(level, out byte weaponSlot);
        DerivedCharacterStats stats = CharacterStatCalculator.Compute(data.Rules, config, (ushort)level, inventory, weaponSlot, allocations);
        ItemSlot weapon = inventory.GetSlot(weaponSlot);
        float weaponDamage = data.Rules.GetWeaponBaseDamage(weapon.ItemId) * ItemEnhancementPolicy.GetStatMultiplier(weapon.UpgradeLevel);
        float swingsPerSecond = stats.AttackSpeedMultiplier / BaseAttackIntervalSeconds;
        float critFactor = 1f + (stats.CritChance * (stats.CritDamage - 1f));
        float evasion = config.GetEvasionChance(in stats, level);

        float nightFraction = NightHours(data.Spawning.Night) / 24f;
        Phase day = SimulatePhase(data, config, level, stats, weaponDamage * stats.AttackMultiplier * swingsPerSecond * critFactor, evasion, isNight: false);
        Phase night = SimulatePhase(data, config, level, stats, weaponDamage * stats.AttackMultiplier * swingsPerSecond * critFactor, evasion, isNight: true);

        float killsPerHour = PlayEfficiency * 3600f * (((1f - nightFraction) / day.CycleSeconds) + (nightFraction / night.CycleSeconds));
        float xpPerHour = PlayEfficiency * 3600f * (((1f - nightFraction) * day.Xp / day.CycleSeconds) + (nightFraction * night.Xp / night.CycleSeconds));
        float xpPerMinute = xpPerHour / 60f;
        uint xpToNext = config.GetXpToNextLevel(level);
        return new SimulatedLevel(level, killsPerHour, xpPerMinute, xpToNext / xpPerMinute, day.TimeToKill, day.HealthLost / stats.MaxHealth);
    }

    /// <summary>Total minutes from <paramref name="fromLevel"/> to <paramref name="toLevel"/>.</summary>
    public static float MinutesBetween(int fromLevel, int toLevel, SimulatedBuild build)
    {
        float minutes = 0f;
        for (int lv = fromLevel; lv < toLevel; lv++)
        {
            minutes += Simulate(lv, build).MinutesToNext;
        }

        return minutes;
    }

    private static Phase SimulatePhase(SimulationData data, ProgressionConfig config, int level, DerivedCharacterStats stats, float rawDps, float evasion, bool isNight)
    {
        StatCurvesConfig curves = data.Spawning.StatCurves;
        float empower = isNight ? data.Spawning.Night.StatMultiplier : 1f;
        float density = (isNight ? NightMonstersPerScreen : DayMonstersPerScreen) / ScreenArea;
        float walkSeconds = 0.5f / MathF.Sqrt(density) / (WalkSpeed * stats.MoveSpeedMultiplier);

        float cycle = 0f;
        float xp = 0f;
        float ttkSum = 0f;
        float lostSum = 0f;
        int count = 0;
        foreach (MonsterDefinition def in data.Monsters.Definitions)
        {
            if (def.IsElite || def.IsBoss || !def.IsSpawnableAt(level))
            {
                continue;
            }

            float hp = def.Stats.Hp * curves.Hp.Evaluate(level) * empower;
            float damage = def.Stats.Damage * curves.Damage.Evaluate(level) * empower;
            float armor = def.Stats.Armor * curves.Armor.Evaluate(level) * empower;

            // Player hits on monsters: monster armor mitigates by 100 / (100 + armor) (CombatService.ApplyMonsterHits).
            float ttk = hp / (rawDps * (100f / (100f + armor)));
            float lost = IncomingDps(def, damage, config, stats, level) * (1f - evasion) * (ttk + EngagementSlackSeconds);
            float rest = lost / MathF.Max(0.1f, stats.HealthRegenPerSecond);

            cycle += ttk + walkSeconds + OverheadSecondsPerKill + rest;
            xp += config.ScaleKillExperience(config.ComputeBaseKillExperience(hp, level, isElite: false, isNight), level, level);
            ttkSum += ttk;
            lostSum += lost;
            count++;
        }

        if (count == 0)
        {
            throw new InvalidDataException($"No normal monster spawns at level {level}.");
        }

        return new Phase(cycle / count, xp / count, ttkSum / count, lostSum / count);
    }

    /// <summary>
    /// Mitigated damage per second of <paramref name="def"/>: each attack profile fires once per
    /// <c>max(cooldown, duration)</c>; casters' spells and bolts are magical (docs/progression.md §4.3).
    /// </summary>
    private static float IncomingDps(MonsterDefinition def, float damage, ProgressionConfig config, in DerivedCharacterStats stats, int level)
    {
        bool isCaster = false;
        foreach (AttackProfileDefinition a in def.Attacks)
        {
            isCaster |= a.Kind == AttackKind.Spell;
        }

        float sum = 0f;
        foreach (AttackProfileDefinition a in def.Attacks)
        {
            uint duration = a.WindupTicks + a.TravelTicks + a.ActiveTicks + a.RecoveryTicks;
            float period = Math.Max(a.CooldownTicks, duration) / 30f;
            DamageKind kind = isCaster && a.Kind is AttackKind.Spell or AttackKind.Projectile ? DamageKind.Magical : DamageKind.Physical;
            sum += config.Mitigate(damage * a.DamageMultiplier, kind, in stats, level) / period;
        }

        // The monster cycles through its profiles rather than firing all of them at once.
        return sum / Math.Max(1, def.Attacks.Count);
    }

    private static CharacterProgressionState Allocate(int points, SimulatedBuild build)
    {
        CharacterProgressionState state = new();
        state.SetAttribute(PrimaryAttributeId.Vitality, (ushort)(points * build.Vitality));
        state.SetAttribute(PrimaryAttributeId.Wisdom, (ushort)(points * build.Wisdom));
        state.SetAttribute(PrimaryAttributeId.Power, (ushort)(points * build.Power));
        state.SetAttribute(PrimaryAttributeId.Agility, (ushort)(points * build.Agility));
        state.SetAttribute(PrimaryAttributeId.Dexterity, (ushort)(points * build.Dexterity));
        return state;
    }

    /// <summary>
    /// Gear an engaged player owns at <paramref name="level"/>: copper sword + leather (1–9), iron sword + iron armor
    /// (10–24), demon cleaver (25–44), ember blade (45+), enhanced +1 every 8 levels up to +10.
    /// </summary>
    private static InventoryContainer Gear(int level, out byte weaponSlot)
    {
        const uint CopperSword = 180;
        const uint EmberBlade = 183;
        byte enhancement = (byte)Math.Min(ItemEnhancementPolicy.MaxUpgradeLevel, level / 8);
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        uint weapon = level < 10 ? CopperSword : level < 25 ? StarterGameCatalog.IronSword : level < 45 ? StarterGameCatalog.DemonCleaver : EmberBlade;
        uint[] armor = level < 12
            ? [StarterGameCatalog.LeatherCap, StarterGameCatalog.LeatherTunic, StarterGameCatalog.LeatherGreaves, StarterGameCatalog.LeatherBoots]
            : [StarterGameCatalog.IronHelmet, StarterGameCatalog.IronChestplate, StarterGameCatalog.IronLeggings, StarterGameCatalog.IronBoots];

        weaponSlot = 0;
        inv.SetSlot(weaponSlot, ItemEnhancementPolicy.WithUpgradeLevel(new ItemSlot(weapon, 1, 0UL, 10_000f), enhancement));
        for (int i = 0; i < armor.Length; i++)
        {
            inv.SetSlot(PlayerInventoryLayout.HeadSlot + i, ItemEnhancementPolicy.WithUpgradeLevel(new ItemSlot(armor[i], 1, 0UL, 10_000f), enhancement));
        }

        return inv;
    }

    private static float NightHours(NightConfig night) =>
        night.StartHour > night.EndHour ? 24f - night.StartHour + night.EndHour : night.EndHour - night.StartHour;

    private readonly record struct Phase(float CycleSeconds, float Xp, float TimeToKill, float HealthLost);

    private sealed record SimulationData(ProgressionConfig Progression, ItemRules Rules, MonsterCatalog Monsters, SpawningConfig Spawning)
    {
        public static SimulationData Load()
        {
            string dir = ProgressionConfigTests.FindConfigDirectory();
            return new SimulationData(
                ProgressionConfig.LoadFromJson(File.ReadAllText(Path.Combine(dir, "progression.json"))),
                ItemRules.LoadShippedDefaults(),
                MonsterCatalog.FromJson(File.ReadAllText(Path.Combine(dir, "monsters.json"))),
                SpawningConfig.FromJson(File.ReadAllText(Path.Combine(dir, "spawning.json"))));
        }
    }
}
