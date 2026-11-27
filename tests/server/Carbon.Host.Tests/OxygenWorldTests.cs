// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

// ADR-032 D10–D12: swim state from the depth tier, a server-authoritative oxygen pool, drowning through the
// status-effect system and the normal death path, and an owner-only oxygen sync.
public sealed class OxygenWorldTests
{
    private const ulong Seed = 42UL;
    private const ushort TickRate = 30;
    private const float Tile = 32f;

    // Seed 42: the beach at 954,-6000 faces open sea due east, getting deeper all the way to the abyss.
    private const int CoastX = 954;
    private const int CoastY = -6000;

    private static readonly ProceduralWorldGenerator s_generator = new(Seed);

    [Fact]
    public void StepFixedTick_DivingInTheAbyss_DrainsOxygenAtTheAbyssRate()
    {
        (AuthoritativeWorld world, _) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(WaterDepthTier.Abyss)));

        Step(world, TickRate * 4);

        world.TryGetOxygen(1UL, out float oxygen, out SwimState state).Should().BeTrue();
        state.Should().Be(SwimState.Diving);
        WaterRules rules = world.WaterRules;
        oxygen.Should().BeApproximately(rules.MaxOxygenSeconds - (4f * rules.AbyssDrainPerSecond), 0.05f);
    }

    [Theory]
    [InlineData(WaterDepthTier.Shallow, SwimState.Wading)]
    [InlineData(WaterDepthTier.Medium, SwimState.Swimming)]
    [InlineData(WaterDepthTier.Deep, SwimState.Diving)]
    public void StepFixedTick_EachTier_SetsTheSwimStateAndOnlyDivingUsesOxygen(WaterDepthTier tier, SwimState expected)
    {
        (AuthoritativeWorld world, _) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(tier)));

        Step(world, TickRate * 2);

        world.TryGetOxygen(1UL, out float oxygen, out SwimState state).Should().BeTrue();
        state.Should().Be(expected);
        if (expected == SwimState.Diving)
        {
            oxygen.Should().BeLessThan(world.WaterRules.MaxOxygenSeconds, "diving uses up the breath");
        }
        else
        {
            oxygen.Should().Be(world.WaterRules.MaxOxygenSeconds, "only diving uses oxygen");
        }
    }

    [Fact]
    public void StepFixedTick_OutOfOxygen_DrownsWithLethalPulsesAndDiesOfDrowning()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(WaterDepthTier.Abyss)));
        int breathTicks = (int)MathF.Ceiling(world.WaterRules.MaxOxygenSeconds / world.WaterRules.AbyssDrainPerSecond * TickRate);

        Step(world, breathTicks - TickRate);
        sink.CombatHits.Should().BeEmpty("there is still air left");

        Step(world, TickRate * 60);

        ushort drowning = DrowningId(world);
        sink.CombatHits.Should().Contain(h => h.StatusEffectId == drowning && h.Damage > 0f, "an empty breath pulses drowning damage");
        sink.Deaths.Should().ContainSingle(d => d.ConnectionId == 1UL)
            .Which.Death.Cause.Should().Be(DeathCause.Drowning);
    }

    [Fact]
    public void StepFixedTick_SurfacingFromTheDeep_RefillsOxygenAndEndsDrowning()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        Vector2 deep = TileOf(WaterDepthTier.Deep);
        _ = world.TryEnterWorld(1UL, Character(deep, health: 100000f));
        int breathTicks = (int)MathF.Ceiling(world.WaterRules.MaxOxygenSeconds / world.WaterRules.DeepDrainPerSecond * TickRate);
        Step(world, breathTicks + TickRate);
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().Contain(e => e.EffectId == DrowningId(world));

        // Swim back towards the beach into Medium water: the surface.
        for (uint i = 0; i < TickRate * 6; i++)
        {
            world.TryGetOxygen(1UL, out _, out SwimState now);
            world.EnqueueMoveInput(1UL, i + 1U, now == SwimState.Diving ? new Vector2(-1f, 0f) : Vector2.Zero, sprint: false);
            world.StepFixedTick();
        }

        world.TryGetOxygen(1UL, out float oxygen, out SwimState state);
        state.Should().Be(SwimState.Swimming);
        oxygen.Should().BeGreaterThan(0f, "the breath refills at the surface");
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().NotContain(e => e.EffectId == DrowningId(world));
    }

    [Fact]
    public void StepFixedTick_WalkingIntoDeepWater_TheServerLetsThePlayerDive()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        Vector2 medium = TileOf(WaterDepthTier.Medium);
        _ = world.TryEnterWorld(1UL, Character(medium));
        Step(world, 1);

        for (uint i = 0; i < TickRate * 12; i++)
        {
            world.EnqueueMoveInput(1UL, i + 1U, new Vector2(1f, 0f), sprint: false);
            world.StepFixedTick();
        }

        world.TryGetOxygen(1UL, out _, out SwimState state);
        state.Should().Be(SwimState.Diving, "deep water does not stop a player");
        sink.SelfPositions[1UL].X.Should().BeGreaterThan(TileOf(WaterDepthTier.Deep).X);
    }

    [Fact]
    public void StepFixedTick_OxygenSync_GoesToTheOwnerOnlyAndOnlyWhenItChanges()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(WaterDepthTier.Abyss)));
        _ = world.TryEnterWorld(2UL, Character(new Vector2((CoastX - 3) * Tile, CoastY * Tile)));

        Step(world, 1);
        sink.OxygenSyncs.Should().Contain(o => o.ConnectionId == 1UL, "the diver hears about its oxygen at entry");
        int landSyncs = sink.OxygenSyncs.Count(o => o.ConnectionId == 2UL);
        landSyncs.Should().Be(1, "the walker gets one entry sync");

        Step(world, TickRate * 3);
        int diverSyncs = sink.OxygenSyncs.Count(o => o.ConnectionId == 1UL);
        diverSyncs.Should().BeInRange(10, 20, "1.5 s of a 30 s breath per second is ~5% a second: one sync per whole percent, not one per tick");
        sink.OxygenSyncs.Count(o => o.ConnectionId == 2UL).Should().Be(landSyncs, "nothing changed for the walker on the beach");

        PlayerOxygenSnapshot last = sink.OxygenSyncs.Last(o => o.ConnectionId == 1UL).Oxygen;
        last.SwimState.Should().Be((byte)SwimState.Diving);
        last.DepthTier.Should().Be((byte)WaterDepthTier.Abyss);
        last.MaxOxygenMs.Should().Be(30_000U);
        last.OxygenMs.Should().BeLessThan(last.MaxOxygenMs);
    }

    // ADR-033 D8 seam: swimming strokes cost stamina like sprinting; the same walk on the beach does not.
    [Fact]
    public void StepFixedTick_SwimmingStrokes_DrainStaminaWhereWalkingRegains()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(WaterDepthTier.Medium)));
        _ = world.TryEnterWorld(2UL, Character(new Vector2((CoastX - 40) * Tile, (CoastY - 20) * Tile)));
        Step(world, 1);

        for (uint i = 0; i < TickRate * 3; i++)
        {
            world.EnqueueMoveInput(1UL, i + 1U, new Vector2(0f, 1f), sprint: false);
            world.EnqueueMoveInput(2UL, i + 1U, new Vector2(0f, 1f), sprint: false);
            world.StepFixedTick();
        }

        world.TryGetOxygen(1UL, out _, out SwimState state);
        state.Should().Be(SwimState.Swimming);
        float expected = sink.LastStats[2UL].MaxStamina - (3f * world.StaminaConfig.SwimDrainPerSecond);
        sink.LastStats[1UL].CurrentStamina.Should().BeApproximately(expected, 0.5f, "three seconds of strokes cost swimDrainPerSecond each");
        sink.LastStats[2UL].CurrentStamina.Should().Be(sink.LastStats[2UL].MaxStamina, "walking on land costs nothing");
    }

    [Fact]
    public void TryEnterWorld_AfterLeavingUnderwater_StartsWithAFullBreath()
    {
        (AuthoritativeWorld world, _) = CreateWorld();
        CharacterRecord diver = Character(TileOf(WaterDepthTier.Abyss));
        _ = world.TryEnterWorld(1UL, diver);
        Step(world, TickRate * 5);
        world.TryGetOxygen(1UL, out float before, out _);
        _ = world.LeaveWorld(1UL);

        _ = world.TryEnterWorld(3UL, diver);
        Step(world, 1);

        before.Should().BeLessThan(world.WaterRules.MaxOxygenSeconds);
        world.TryGetOxygen(3UL, out float after, out _);
        after.Should().BeApproximately(world.WaterRules.MaxOxygenSeconds, 0.1f, "oxygen is not persisted: a relog starts with a full breath");
    }

    [Fact]
    public void TryRespawn_AfterDrowning_StartsWithAFullBreathAndNoDrowning()
    {
        (AuthoritativeWorld world, StatusRecordingSink sink) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(WaterDepthTier.Abyss)));
        for (int i = 0; i < TickRate * 120 && !world.IsPlayerDead(1UL); i++)
        {
            world.StepFixedTick();
        }

        world.IsPlayerDead(1UL).Should().BeTrue("the diver drowned");

        world.TryRespawn(1UL).Should().Be(OpStatus.Ok, "respawning at once, before the breath could refill");

        world.TryGetOxygen(1UL, out float oxygen, out _).Should().BeTrue();
        oxygen.Should().Be(world.WaterRules.MaxOxygenSeconds, "a respawn at the sanctuary is a full breath");
        Step(world, TickRate);
        world.TryGetOxygen(1UL, out _, out SwimState state);
        state.Should().NotBe(SwimState.Diving, "the sanctuary is on land");
        sink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().NotContain(e => e.EffectId == DrowningId(world), "drowning ends out of the water");
    }

    [Fact]
    public void StepFixedTick_DrownedAndWaitingToRespawn_StopsUsingOxygenAndRefills()
    {
        (AuthoritativeWorld world, _) = CreateWorld();
        _ = world.TryEnterWorld(1UL, Character(TileOf(WaterDepthTier.Abyss)));
        for (int i = 0; i < TickRate * 120 && !world.IsPlayerDead(1UL); i++)
        {
            world.StepFixedTick();
        }

        world.IsPlayerDead(1UL).Should().BeTrue("the diver drowned");
        world.TryGetOxygen(1UL, out float atDeath, out _);
        atDeath.Should().BeLessThan(1f, "the breath had just run out");

        Step(world, TickRate * 2);

        world.TryGetOxygen(1UL, out float later, out SwimState state);
        state.Should().Be(SwimState.Diving, "the body is still in the abyss");
        later.Should().BeGreaterThan(atDeath, "a dead player no longer uses oxygen, so the breath refills");
    }

    private static ushort DrowningId(AuthoritativeWorld world) =>
        world.StatusEffects.TryGetByKey(world.WaterRules.DrowningEffect, out Carbon.Combat.Statuses.Config.StatusEffectDefinition definition)
            ? definition.Id
            : throw new InvalidOperationException("No drowning effect.");

    // The centre of the first tile of a tier on the shore transect.
    private static Vector2 TileOf(WaterDepthTier tier)
    {
        for (int x = CoastX + 1; x < CoastX + 400; x++)
        {
            if (WaterDepth.TierOf(s_generator.SampleTile(x, CoastY)) == tier)
            {
                return new Vector2((x + 2.5f) * Tile, (CoastY + 0.5f) * Tile);
            }
        }

        throw new InvalidOperationException($"No {tier} water on the transect.");
    }

    private static CharacterRecord Character(Vector2 position, float health = 100f) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Diver", 1, default, position.X, position.Y, health, health, 0, [], DateTimeOffset.UnixEpoch);

    private static void Step(AuthoritativeWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.StepFixedTick();
        }
    }

    private static (AuthoritativeWorld World, StatusRecordingSink Sink) CreateWorld()
    {
        StatusRecordingSink sink = new();
        AuthoritativeWorld world = new(
            Seed,
            Tile,
            ProceduralWorldGenerator.DefaultFrequency,
            TickRate,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            sink,
            entropy: TestGameplayEntropy.Create(7UL));
        return (world, sink);
    }
}
