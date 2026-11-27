// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Simulation.ECS;
using FluentAssertions;
using Xunit;

namespace Carbon.Combat.Tests;

/// <summary>ADR-036 D7–D9: new effects' values, water putting out burning, and the proc level formula.</summary>
public sealed class StatusEffectAbilityTests
{
    private const ushort TickRate = 30;
    private static readonly StatusEffectCatalog s_shipped = StatusEffectCatalog.LoadShippedDefaults();

    [Fact]
    public void CreateActive_WeaknessLevels_CutDamageDealtByTheConfiguredAmount()
    {
        StatusEffectBuffer buffer = new();
        _ = buffer.ApplyOrRefresh(Create("weakness", level: 1));
        buffer.DamageDealtMultiplier.Should().BeApproximately(0.75f, 1e-6f, "weakness I deals 75 % damage");

        _ = buffer.ApplyOrRefresh(Create("weakness", level: 3));
        buffer.DamageDealtMultiplier.Should().BeApproximately(0.55f, 1e-6f, "weakness III is 0.75 - 2 x 0.1");
    }

    [Fact]
    public void CreateActive_BlindnessLevels_ShrinkTheViewButNeverBelowAQuarter()
    {
        Create("blindness", level: 1).ViewRadiusMultiplier.Should().BeApproximately(0.5f, 1e-6f);
        Create("blindness", level: 3).ViewRadiusMultiplier.Should().BeApproximately(0.3f, 1e-6f);

        StatusEffectCatalog steep = StatusEffectCatalog.LoadFromJson(
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "blind", "icon": "i", "durationSeconds": 5, "stacking": "Strongest", "viewRadiusMultiplier": 0.5, "maxLevel": 9, "viewRadiusMultiplierPerLevel": -0.2 } ] }""");
        steep.TryGet(1, out StatusEffectDefinition def).Should().BeTrue();
        StatusEffectBuffer buffer = new();
        _ = buffer.ApplyOrRefresh(StatusEffectCatalog.CreateActive(def, TickRate, Entity.Null, StatusSource.Monster, 104, level: 9));

        buffer.ViewRadiusMultiplier.Should().Be(StatusEffectDefinition.MinViewRadiusMultiplier, "a blind player still sees nearby enemies");
        def.ViewRadiusMultiplierAt(9).Should().Be(StatusEffectDefinition.MinViewRadiusMultiplier, "0.5 - 8 x 0.2 is clamped per level too");
    }

    [Fact]
    public void ViewRadiusMultiplier_TwoBlindingEffects_MultiplyButStopAtAQuarter()
    {
        StatusEffectCatalog two = StatusEffectCatalog.LoadFromJson(
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "ash", "icon": "i", "durationSeconds": 5, "viewRadiusMultiplier": 0.5 }, { "id": 2, "key": "smoke", "icon": "i", "durationSeconds": 5, "viewRadiusMultiplier": 0.4 } ] }""");
        StatusEffectBuffer buffer = new();
        two.TryGet(1, out StatusEffectDefinition ash).Should().BeTrue();
        two.TryGet(2, out StatusEffectDefinition smoke).Should().BeTrue();
        _ = buffer.ApplyOrRefresh(StatusEffectCatalog.CreateActive(ash, TickRate, Entity.Null, StatusSource.Monster, 104));
        _ = buffer.ApplyOrRefresh(StatusEffectCatalog.CreateActive(smoke, TickRate, Entity.Null, StatusSource.Monster, 104));

        buffer.ViewRadiusMultiplier.Should().Be(StatusEffectDefinition.MinViewRadiusMultiplier, "0.5 x 0.4 = 0.2 is below the floor");
    }

    [Fact]
    public void RemoveExtinguishedByWater_BurningAndPoison_RemovesOnlyTheBurning()
    {
        StatusEffectBuffer buffer = new();
        _ = buffer.ApplyOrRefresh(Create("burning", level: 2));
        _ = buffer.ApplyOrRefresh(Create("poisoned"));
        uint version = buffer.Version;

        buffer.RemoveExtinguishedByWater().Should().BeTrue();

        buffer.TryGet(Id("burning"), out _).Should().BeFalse("water puts out burning");
        buffer.TryGet(Id("poisoned"), out _).Should().BeTrue("water does not wash out poison");
        buffer.Version.Should().NotBe(version, "the owner must see the flame go out");
        buffer.RemoveExtinguishedByWater().Should().BeFalse();
    }

    [Fact]
    public void CreateActive_BurningAndFrostbite_DealTheirLevelDamageAndFrostbiteSlowsStamina()
    {
        Create("burning", level: 2).MagnitudePerStack.Should().Be(2.5f, "burning II is 1.5 + 1");
        ActiveStatusEffect frost = Create("frostbite", level: 2);
        (frost.MagnitudePerStack, frost.Lethal).Should().Be((1f, false));
        frost.StaminaRegenMultiplier.Should().BeApproximately(0.5f, 1e-6f, "frostbite II is 0.6 - 0.1");
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 2)]
    [InlineData(15, false, 1)]
    [InlineData(16, false, 2)]
    [InlineData(30, false, 2)]
    [InlineData(30, true, 3)]
    [InlineData(200, true, 5)]
    public void ResolveProcLevel_MonsterLevelAndNight_RaiseTheLevelUpToMax(int monsterLevel, bool night, int expected)
    {
        MonsterProcEntry poison = s_shipped.MonsterProcs.First(p => p.Monster == "skeleton_archer");

        s_shipped.ResolveProcLevel(poison, monsterLevel, night).Should().Be((byte)expected, "1 + floor((level - 1) / 15) + night, clamped to poison V");
    }

    [Fact]
    public void ResolveProcLevel_BaseLevelTwoSlimeKing_StartsAtTwo()
    {
        MonsterProcEntry slow = s_shipped.MonsterProcs.First(p => p.Monster == "slime_king");

        s_shipped.ResolveProcLevel(slow, 1, isNight: false).Should().Be(2, "high-tier slimes slow II");
        s_shipped.ResolveProcLevel(slow, 31, isNight: true).Should().Be(5, "2 + 2 + 1");
    }

    [Fact]
    public void LoadShippedDefaults_NewEffectsAndHeavyHitProcs_AreConfigured()
    {
        string[] keys = ["weakness", "blindness", "burning", "frostbite"];
        foreach (string key in keys)
        {
            s_shipped.TryGetByKey(key, out StatusEffectDefinition def).Should().BeTrue(key);
            def.Icon.Should().Be($"status_{key}");
        }

        s_shipped.TryGetByKey("burning", out StatusEffectDefinition burning).Should().BeTrue();
        burning.ExtinguishedByWater.Should().BeTrue();
        MonsterProcEntry golem = s_shipped.MonsterProcs.Single(p => p.Monster == "rock_golem");
        (golem.Effect.Key, golem.Chance, golem.MinDamageFraction).Should().Be(("stunned", 1f, 0.12f));
    }

    [Fact]
    public void LoadFromJson_InvalidProcAndScaling_AreRejected()
    {
        const string Effect = """{ "id": 1, "key": "a", "icon": "i", "durationSeconds": 1 }""";
        string[] invalid =
        [
            $$"""{ "schemaVersion": 1, "effects": [ {{Effect}} ], "monsterProcs": [ { "monster": "m", "effect": "a", "chance": 0.5, "level": 0 } ] }""",
            $$"""{ "schemaVersion": 1, "effects": [ {{Effect}} ], "monsterProcs": [ { "monster": "m", "effect": "a", "chance": 0.5, "minDamageFraction": 1.5 } ] }""",
            $$"""{ "schemaVersion": 1, "effects": [ {{Effect}} ], "procLevelScaling": { "monsterLevelsPerEffectLevel": -1, "nightBonusLevels": 0 } }""",
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "a", "icon": "i", "durationSeconds": 1, "viewRadiusMultiplier": 0.1 } ] }""",
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "a", "icon": "i", "durationSeconds": 1, "damageDealtMultiplierPerLevel": -0.1 } ] }""",
        ];

        foreach (string json in invalid)
        {
            Action load = () => StatusEffectCatalog.LoadFromJson(json);
            load.Should().Throw<System.IO.InvalidDataException>(json);
        }
    }

    private static ushort Id(string key) => s_shipped.TryGetByKey(key, out StatusEffectDefinition def) ? def.Id : (ushort)0;

    private static ActiveStatusEffect Create(string key, byte level = 1)
    {
        s_shipped.TryGetByKey(key, out StatusEffectDefinition def).Should().BeTrue();
        return StatusEffectCatalog.CreateActive(def, TickRate, Entity.Null, StatusSource.Monster, 104, level: level);
    }
}
