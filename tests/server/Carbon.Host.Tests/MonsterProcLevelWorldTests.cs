// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Linq;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Combat.Statuses.Config;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-036 D8 through the real world tick: a monster's landed hit (<c>AuthoritativeWorld.ApplyDamageToPlayerLocked</c>)
/// applies its proc at a level set by the monster's own level and the night at the moment of the hit.
/// </summary>
public sealed class MonsterProcLevelWorldTests
{
    private const int MaxTicksToBeHit = 1_200;
    private const int NightBonus = 10;

    // Every monster marks its victim on every landed hit; one effect level per monster level, +10 at night. The effect
    // is persisted, so the level can be read from the character's pending save.
    private static readonly StatusEffectCatalog s_marking = StatusEffectCatalog.LoadFromJson(
        $$"""
        {
          "schemaVersion": 1,
          "procLevelScaling": { "monsterLevelsPerEffectLevel": 1, "nightBonusLevels": {{NightBonus}} },
          "effects": [ { "id": 1, "key": "marked", "icon": "status_poisoned", "durationSeconds": 600, "stacking": "Refresh", "maxLevel": 255, "persist": true } ],
          "monsterProcs": [
            { "monster": "wild_slime", "effect": "marked", "chance": 1 },
            { "monster": "slime_king", "effect": "marked", "chance": 1 },
            { "monster": "rock_golem", "effect": "marked", "chance": 1 },
            { "monster": "frost_wolf", "effect": "marked", "chance": 1 },
            { "monster": "ash_wraith", "effect": "marked", "chance": 1 },
            { "monster": "skeleton_archer", "effect": "marked", "chance": 1 },
            { "monster": "orc_shaman", "effect": "marked", "chance": 1 },
            { "monster": "orc_warlord", "effect": "marked", "chance": 1 },
            { "monster": "crimson_demon", "effect": "marked", "chance": 1 }
          ]
        }
        """);

    [Fact]
    public void MonsterHitsPlayer_ByDay_AppliesTheEffectAtTheMonstersLevel()
    {
        (ushort monsterLevel, byte effectLevel) = FightAt(startHour: 11f);

        monsterLevel.Should().BeGreaterThan(1, "the test needs a monster whose level raises the effect");
        effectLevel.Should().Be((byte)monsterLevel, "1 + (monster level - 1) / 1, no night bonus by day");
    }

    [Fact]
    public void MonsterHitsPlayer_AtNight_AddsTheNightBonusOnTopOfTheMonstersLevel()
    {
        (ushort monsterLevel, byte effectLevel) = FightAt(startHour: 23f);

        monsterLevel.Should().BeGreaterThan(1);
        effectLevel.Should().Be((byte)(monsterLevel + NightBonus), "the hit lands at night");
    }

    private static (ushort MonsterLevel, byte EffectLevel) FightAt(float startHour)
    {
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = new(
            1337UL,
            32f,
            0.0008f,
            30,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            sink,
            new FixedTime(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L)),
            entropy: TestGameplayEntropy.Create(1UL),
            statusEffects: s_marking);
        _ = world.ApplyDebugOverrides(new WorldDebugOverrides { StartHour = startHour });
        EntitySpawnSnapshot mob = FindMonsterAboveLevelOne(world, sink);

        CharacterRecord victim = Character("Victim") with { PositionX = mob.PositionX + 12f, PositionY = mob.PositionY };
        EnterWorldResult entered = world.TryEnterWorld(900UL, victim);
        entered.Status.Should().Be(OpStatus.Ok);

        bool hit = false;
        for (int tick = 0; tick < MaxTicksToBeHit && !hit; tick++)
        {
            world.StepFixedTick();
            hit = sink.CombatHits.Any(h => h.DefenderNetworkId == entered.PlayerNetworkId && h.Damage > 0f);
        }

        hit.Should().BeTrue("a player standing next to a monster is attacked");
        world.StepFixedTick();
        world.TryGetPendingCharacterSnapshot(victim.Id, out CharacterRecord? saved).Should().BeTrue();
        return (mob.Level, LevelOf(saved!.StatusEffects));
    }

    // Saved as "v2;marked:<ms>:<stacks>:<level>" (ADR-036 D5).
    private static byte LevelOf(string persisted)
    {
        string entry = persisted.Split(';').Single(e => e.StartsWith("marked:", StringComparison.Ordinal));
        return byte.Parse(entry.Split(':')[3], CultureInfo.InvariantCulture);
    }

    private static EntitySpawnSnapshot FindMonsterAboveLevelOne(AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink)
    {
        for (int probeX = 1; probeX <= 40; probeX++)
        {
            ulong probeConn = 700UL + (ulong)probeX;
            world.TryEnterWorld(probeConn, Character($"Probe{probeX}") with
            {
                PositionX = ((probeX * 32) + 16) * 32f,
                PositionY = 16f * 32f,
            }).Status.Should().Be(OpStatus.Ok);
            world.StepFixedTick();
            EntitySpawnSnapshot? mob = sink.Spawns
                .Where(x => x.ConnectionId == probeConn && x.Snapshot.Kind == EntityKind.Monster && x.Snapshot.Level > 1)
                .Select(x => (EntitySpawnSnapshot?)x.Snapshot)
                .FirstOrDefault();
            if (mob is not null)
            {
                return mob.Value;
            }
        }

        throw new InvalidOperationException("wilderness chunks away from the origin should populate monsters above level 1");
    }

    private static CharacterRecord Character(string name) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
