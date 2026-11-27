// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-029 through the real world tick: damage pulses, owner-only replication, and slow on movement.</summary>
public sealed class StatusEffectWorldTests
{
    private const ushort Poisoned = 1;
    private const ushort Slowed = 2;
    private const ushort Bleeding = 3;

    [Fact]
    public void StepFixedTick_SavedBleeding_PulsesOnTheThirtiethTickAsATintedCombatHit()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        EnterWorldResult entered = world.TryEnterWorld(1UL, Character(statusEffects: "bleeding:6000:2"));

        Step(world, 29);
        sink.CombatHits.Should().NotContain(h => h.StatusEffectId == Bleeding, "bleeding pulses once per second");

        Step(world, 1);
        (ulong[] recipients, ulong defender, float damage, float remaining, ushort effect, byte level) = sink.CombatHits.Single(h => h.StatusEffectId == Bleeding);
        (defender, damage, remaining, effect, level).Should().Be((entered.PlayerNetworkId, 1.5f, 98.5f, Bleeding, 1), "two stacks of 0.75; a legacy row loads at level I");
        recipients.Should().Contain(1UL);
    }

    [Fact]
    public void StepFixedTick_SavedBleedingLevelTwo_PulsesLevelTwoDamageAndSendsTheLevel()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        EnterWorldResult entered = world.TryEnterWorld(1UL, Character(statusEffects: "v2;bleeding:6000:2:2"));

        Step(world, 30);

        (_, ulong defender, float damage, _, _, byte level) = sink.CombatHits.Single(h => h.StatusEffectId == Bleeding);
        (defender, damage, level).Should().Be((entered.PlayerNetworkId, 2.5f, 2), "two stacks of bleeding II (0.75 + 0.5)");
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries
            .Should().ContainSingle(e => e.EffectId == Bleeding).Which.Level.Should().Be(2, "the owner's list carries the level");
    }

    [Fact]
    public async Task StepFixedTick_LethalBleedingPulse_KillsThePlayerAndClearsEveryEffect()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        CharacterRecord hero = Character(statusEffects: "bleeding:6000:2;poisoned:8000:1", health: 1f);
        EnterWorldResult entered = world.TryEnterWorld(1UL, hero);

        Step(world, 31);

        sink.CombatHits.Should().Contain(h => h.DefenderNetworkId == entered.PlayerNetworkId && h.StatusEffectId == Bleeding && h.RemainingHealth == 0f);
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries
            .Should().NotContain(e => e.EffectId == Bleeding || e.EffectId == Poisoned, "death clears every effect");
        world.IsPlayerDead(1UL).Should().BeTrue("a lethal pulse uses the normal death path: dead until a respawn request (ADR-031)");
        world.TryGetPendingCharacterSnapshot(hero.Id, out CharacterRecord? dead).Should().BeTrue();
        dead!.CurrentHealth.Should().Be(0f);

        ValueTask<WorldMutationOutcome> respawn = world.SubmitRespawnAsync(1UL);
        Step(world, 1);
        (await respawn.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        world.TryGetPendingCharacterSnapshot(hero.Id, out CharacterRecord? saved).Should().BeTrue();
        saved!.CurrentHealth.Should().Be(saved.MaxHealth, "the respawn heals to full");
    }

    [Fact]
    public void StepFixedTick_TwoPlayers_SendsEachPlayersEffectsOnlyToThatPlayer()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(statusEffects: "poisoned:8000:2"));
        _ = world.TryEnterWorld(2UL, Character(statusEffects: string.Empty, offsetX: 48f));

        Step(world, 2);

        StatusEffectEntry poison = sink.StatusSyncs.Where(s => s.ConnectionId == 1UL).SelectMany(s => s.Entries).First(e => e.EffectId == Poisoned);
        (poison.Stacks, poison.Magnitude).Should().Be((2, 2f));
        sink.StatusSyncs.Where(s => s.ConnectionId != 1UL).SelectMany(s => s.Entries)
            .Should().NotContain(e => e.EffectId == Poisoned, "effects are private to their owner");
        sink.StatusSyncs.Should().Contain(s => s.ConnectionId == 2UL, "the other player gets its own list (well fed)");
        sink.StatusSyncs.Count(s => s.ConnectionId == 1UL).Should().Be(1, "nothing changed after the first send");
    }

    [Fact]
    public void StepFixedTick_Slowed_MovesSixtyPercentAsFarAndExpiryRestoresTheSpeed()
    {
        float normal = WalkDistance(statusEffects: string.Empty, waitTicks: 0);
        float slowed = WalkDistance(statusEffects: "slowed:3000:1", waitTicks: 0);
        float expired = WalkDistance(statusEffects: "slowed:500:1", waitTicks: 30);

        normal.Should().BeGreaterThan(20f);
        (slowed / normal).Should().BeApproximately(0.6f, 0.02f);
        expired.Should().BeApproximately(normal, 0.01f, "the slow and its modifier are gone after 0.5 s");
    }

    [Fact]
    public void StepFixedTick_SlowExpires_SendsAListWithoutIt()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld(new WorldDebugOverrides { StatusEffects = "slowed:500:1" });
        _ = world.TryEnterWorld(1UL, Character(statusEffects: string.Empty));

        Step(world, 1);
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().Contain(e => e.EffectId == Slowed && e.RemainingMs == 466U);

        Step(world, 15);
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().NotContain(e => e.EffectId == Slowed);
    }

    [Fact]
    public void TryEnterWorld_DebugStatusEffects_ApplyNeverSavedEffectsAtTheGivenDuration()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld(new WorldDebugOverrides { StatusEffects = "slowed:60000:1" });
        _ = world.TryEnterWorld(1UL, Character(statusEffects: "slowed:60000:1"));

        Step(world, 1);

        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries
            .Should().ContainSingle(e => e.EffectId == Slowed).Which.RemainingMs.Should().Be(59_966U, "the review knob is not clamped; the saved row is ignored for a never-saved effect");
    }

    // Walks from the world spawn: its clearing is the one place the generator keeps free of trees and rocks, so the
    // walk measures the effect and not a prop in the way.
    private static float WalkDistance(string statusEffects, int waitTicks)
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld(new WorldDebugOverrides { StatusEffects = statusEffects });
        Vector2 spawn = world.DefaultSpawnWorldPosition;
        _ = world.TryEnterWorld(1UL, Character(statusEffects: string.Empty) with { PositionX = spawn.X, PositionY = spawn.Y });
        Step(world, waitTicks + 1);
        Vector2 start = sink.SelfPositions[1UL];
        for (uint i = 0; i < 15; i++)
        {
            world.EnqueueMoveInput(1UL, i + 1U, new Vector2(1f, 0f), sprint: false);
            world.StepFixedTick();
        }

        return sink.SelfPositions[1UL].X - start.X;
    }

    private static (AuthoritativeWorld World, StatusRecordingSink Sink) CreateWorld(WorldDebugOverrides? overrides = null)
    {
        StatusRecordingSink sink = new();
        AuthoritativeWorld world = new(
            1337UL,
            32f,
            0.0008f,
            30,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            sink,
            entropy: TestGameplayEntropy.Create(3UL));
        if (overrides is not null)
        {
            _ = world.ApplyDebugOverrides(overrides);
        }

        return (world, sink);
    }

    private static CharacterRecord Character(string statusEffects, float offsetX = 0f, float health = 100f) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Hero" + offsetX.ToString(System.Globalization.CultureInfo.InvariantCulture), 1, 0, offsetX, 0f, health, 100f, 0, [], DateTimeOffset.UnixEpoch)
    {
        StatusEffects = statusEffects,
    };

    private static void Step(AuthoritativeWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.StepFixedTick();
        }
    }
}
