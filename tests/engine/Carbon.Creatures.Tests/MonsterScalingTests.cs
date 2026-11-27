// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

public sealed class MonsterScalingTests
{
    private readonly MonsterScaling _scaling = new(CreatureTestData.LoadSpawning());
    private readonly MonsterDefinition _orc = CreatureTestData.LoadCatalog().GetDefinition(CreatureTestData.LoadCatalog().GetIndex("rock_golem"));

    [Fact]
    public void Compute_IncreasingDistance_IsMonotonicInLevelAndStats()
    {
        ScaledMonsterStats previous = _scaling.Compute(0f, isNight: false, _orc);
        float previousLevel = _scaling.EvaluateLevel(0f, out _);

        for (float d = 250f; d <= 250_000f; d += 250f)
        {
            ScaledMonsterStats current = _scaling.Compute(d, isNight: false, _orc);
            float level = _scaling.EvaluateLevel(d, out _);

            level.Should().BeGreaterThanOrEqualTo(previousLevel, $"level must not drop at distance {d}");
            current.Level.Should().BeGreaterThanOrEqualTo(previous.Level);
            current.MaxHealth.Should().BeGreaterThanOrEqualTo(previous.MaxHealth);
            current.Damage.Should().BeGreaterThanOrEqualTo(previous.Damage);
            current.Armor.Should().BeGreaterThanOrEqualTo(previous.Armor);
            previous = current;
            previousLevel = level;
        }

        _scaling.Compute(250_000f, false, _orc).Level.Should().BeGreaterThan(_scaling.Compute(0f, false, _orc).Level);
    }

    [Fact]
    public void Compute_Night_IsStrongerThanDayAtSameDistance()
    {
        foreach (float d in new[] { 0f, 5_000f, 40_000f, 120_000f })
        {
            ScaledMonsterStats day = _scaling.Compute(d, isNight: false, _orc);
            ScaledMonsterStats night = _scaling.Compute(d, isNight: true, _orc);

            night.IsNight.Should().BeTrue();
            night.MaxHealth.Should().BeGreaterThan(day.MaxHealth);
            night.Damage.Should().BeGreaterThan(day.Damage);
            night.Armor.Should().BeGreaterThan(day.Armor);
        }
    }

    [Fact]
    public void EvaluateLevel_BandBoundaries_MatchConfiguredBandLevels()
    {
        SpawningConfig config = _scaling.Config;

        for (int i = 0; i < config.Level.Bands.Count; i++)
        {
            LevelBand band = config.Level.Bands[i];
            _scaling.EvaluateLevel(band.StartDistance, out int startBand).Should().BeApproximately(band.StartLevel, 1e-3f);
            startBand.Should().Be(i);

            float mid = (band.StartDistance + band.EndDistance) * 0.5f;
            float midLevel = _scaling.EvaluateLevel(mid, out int midBand);
            midBand.Should().Be(i, $"{mid} lies inside band '{band.Name}'");
            midLevel.Should().BeApproximately((band.StartLevel + band.EndLevel) * 0.5f, 1e-2f, "smoothstep is centered");
        }

        _scaling.EvaluateLevel(1e9f, out int beyond).Should().Be(config.Level.MaxLevel, "level is capped");
        beyond.Should().Be(config.Level.Bands.Count);
    }

    [Fact]
    public void ShippedConfig_EveryMonsterSpawnableLevelRange_OverlapsItsLootBand()
    {
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();
        Dictionary<string, (int MinLevel, int MaxLevel)> lootBands = CreatureTestData.LoadLootBands();
        int maxReachable = (int)_scaling.EvaluateLevel(float.MaxValue, out _);
        maxReachable.Should().Be(100, "levels span 1..100 so every loot band is reachable");

        foreach (MonsterDefinition def in catalog.Definitions)
        {
            int lo = Math.Max(1, def.MinLevel);
            int hi = Math.Min(maxReachable, def.MaxLevel);
            lo.Should().BeLessThanOrEqualTo(hi, $"'{def.Id}' must be spawnable at some reachable level");
            if (lootBands.TryGetValue(def.LootTableId, out (int MinLevel, int MaxLevel) band))
            {
                (lo <= band.MaxLevel && hi >= band.MinLevel).Should().BeTrue(
                    $"'{def.Id}' spawns at {lo}..{hi} but its loot table '{def.LootTableId}' drops at {band.MinLevel}..{band.MaxLevel}");
            }
        }
    }

    [Theory]
    [InlineData(false, 3.5)]
    [InlineData(true, 5.5)]
    public void ShippedConfig_SpawnDensity_StaysWithinScreenBudget(bool isNight, double maxPerScreen)
    {
        SpawningConfig config = CreatureTestData.LoadSpawning();
        double cap = isNight ? Math.Ceiling(config.BaseCellCap * config.Night.DensityMultiplier) : config.BaseCellCap;
        const double screenArea = 1280d * 720d; // reference desktop viewport, world units at zoom 1

        double perScreen = cap * screenArea / (config.CellSize * config.CellSize);

        perScreen.Should().BeLessThanOrEqualTo(maxPerScreen, "a fully populated area must not crowd the player's screen");
        perScreen.Should().BeGreaterThan(1d, "the wilds should still have monsters to fight");
    }

    [Fact]
    public void ShippedConfig_LevelGates_SlimesNearSpawnDemonsOnlyFarOut()
    {
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();
        MonsterDefinition slime = catalog.GetDefinition(catalog.GetIndex("wild_slime"));
        MonsterDefinition demon = catalog.GetDefinition(catalog.GetIndex("crimson_demon"));

        for (float d = 0f; d <= 6400f; d += 200f)
        {
            int level = (int)_scaling.EvaluateLevel(d, out _);
            slime.IsSpawnableAt(level).Should().BeTrue($"slimes spawn in the heartland (distance {d}, level {level})");
            demon.IsSpawnableAt(level).Should().BeFalse($"demons never spawn near the first spawn point (distance {d})");
        }

        demon.IsSpawnableAt((int)_scaling.EvaluateLevel(40_000f, out _)).Should().BeFalse("the outer wilds start below demon levels");
        demon.IsSpawnableAt((int)_scaling.EvaluateLevel(120_000f, out _)).Should().BeTrue("demons inhabit the endgame band");
        slime.IsSpawnableAt((int)_scaling.EvaluateLevel(120_000f, out _)).Should().BeFalse("slimes do not appear deep in the endgame");
    }

    [Fact]
    public void Compute_InsideSafeZone_IsLevelOneWithBaseStats()
    {
        ScaledMonsterStats stats = _scaling.Compute(0f, isNight: false, _orc);

        stats.Level.Should().Be(1);
        stats.MaxHealth.Should().Be(_orc.Stats.Hp);
        stats.Damage.Should().Be(_orc.Stats.Damage);
    }

    [Theory]
    [InlineData(19f, true)]
    [InlineData(23.5f, true)]
    [InlineData(2f, true)]
    [InlineData(5f, false)]
    [InlineData(12f, false)]
    [InlineData(18.99f, false)]
    public void NightConfig_IsNight_WrapsPastMidnight(float hour, bool expected) =>
        _scaling.Config.Night.IsNight(hour).Should().Be(expected);
}
