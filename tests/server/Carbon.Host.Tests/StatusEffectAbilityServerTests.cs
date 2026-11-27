// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Application.World.StatusEffects;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Creatures;
using Carbon.Creatures.Definitions;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-036 D7–D9 on the server: proc levels from monster level and night, heavy-hit procs, water and blindness.</summary>
public sealed class StatusEffectAbilityServerTests
{
    private const ushort RockGolem = 102;
    private const ushort SkeletonArcher = 105;
    private const ushort Stunned = 4;
    private const ushort Poisoned = 1;

    [Fact]
    public void OnMonsterHit_SameArcherAtNightOrFarther_AppliesAHigherPoisonLevelOnTheSameRoll()
    {
        ulong tick = FirstProcTick(SkeletonArcher, Poisoned);

        ApplyArcherHit(tick, monsterLevel: 1, night: false).Should().Be(1, "a level-1 archer by day poisons at I");
        ApplyArcherHit(tick, monsterLevel: 30, night: false).Should().Be(2, "a level-30 archer (far from the origin) poisons at II");
        ApplyArcherHit(tick, monsterLevel: 1, night: true).Should().Be(2, "night adds a level");
        ApplyArcherHit(tick, monsterLevel: 30, night: true).Should().Be(3, "a level-30 archer at night poisons at III");
    }

    [Fact]
    public void OnMonsterHit_RockGolem_StunsOnlyOnAHitOfTwelvePercentOfMaxHealth()
    {
        Harness h = new(entropyLabel: 3UL);

        h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, RockGolem, dealt: 11.9f, victimMaxHealth: 100f, tick: 1UL).Should().BeFalse();
        h.Buffer.TryGet(Stunned, out _).Should().BeFalse("below 12 % the golem's stun does not roll");

        h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, RockGolem, dealt: 12f, victimMaxHealth: 100f, tick: 2UL).Should().BeTrue();
        h.Buffer.TryGet(Stunned, out ActiveStatusEffect stun).Should().BeTrue();
        (stun.Source, stun.SourceId).Should().Be((StatusSource.Monster, RockGolem), "the golem's own proc, below the 25 % global heavy hit");
    }

    [Fact]
    public void OnMonsterHit_ProcDurationOverride_LastsTheConfiguredSeconds()
    {
        StatusEffectCatalog catalog = StatusEffectCatalog.LoadFromJson(
            """{ "schemaVersion": 1, "effects": [ { "id": 2, "key": "slowed", "icon": "i", "durationSeconds": 3, "stacking": "Strongest", "moveSpeedMultiplier": 0.6 } ], "monsterProcs": [ { "monster": "wild_slime", "effect": "slowed", "chance": 1, "durationSeconds": 9 } ] }""");
        Harness h = new(entropyLabel: 1UL, catalog);

        h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, 101, dealt: 1f, victimMaxHealth: 100f, tick: 1UL).Should().BeTrue();

        h.Buffer.TryGet(2, out ActiveStatusEffect slow).Should().BeTrue();
        slow.RemainingTicks.Should().Be(270U, "9 s at 30 Hz, not the effect's 3 s");
    }

    [Fact]
    public void ExtinguishInWater_Burning_GoesOutFromShallowWaterButNotOnTheShore()
    {
        Harness h = new(entropyLabel: 1UL);
        h.Service.Apply(h.Player, "burning", StatusSource.Monster, 104).Should().BeTrue();

        h.Service.ExtinguishInWater(h.Player, WaterDepthTier.Shore).Should().BeFalse("wet sand does not put out a fire");
        h.Service.ExtinguishInWater(h.Player, WaterDepthTier.Shallow).Should().BeTrue();

        h.Service.TryGetBuffer(h.Player, out StatusEffectBuffer buffer).Should().BeTrue();
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void ShippedProcs_EveryMonsterAppliesAtLeastOneEffect()
    {
        StatusEffectCatalog catalog = StatusEffectCatalog.LoadShippedDefaults();
        ReadOnlySpan<MonsterDefinition> monsters = CreatureContent.LoadDefault().Catalog.Definitions;
        List<string> without = [];
        for (int i = 0; i < monsters.Length; i++)
        {
            string id = monsters[i].Id;
            if (!catalog.MonsterProcs.Any(p => p.Monster == id))
            {
                without.Add(id);
            }
        }

        without.Should().BeEmpty("every monster type applies an effect (ADR-036 D7)");
    }

    [Fact]
    public void StepFixedTick_BurningPlayerInShallowWater_IsPutOutAndOnDryLandKeepsBurning()
    {
        (AuthoritativeWorld wet, StatusRecordingSink wetSink) = WaterWorld("burning:60000:1");
        _ = wet.TryEnterWorld(1UL, WaterCharacter(ShallowTile()));
        (AuthoritativeWorld dry, StatusRecordingSink drySink) = WaterWorld("burning:60000:1");
        _ = dry.TryEnterWorld(1UL, WaterCharacter(new Vector2(DryX * WaterTileSize, CoastY * WaterTileSize)));

        for (int i = 0; i < 3; i++)
        {
            wet.StepFixedTick();
            dry.StepFixedTick();
        }

        ushort burning = Id("burning");
        wetSink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().NotContain(e => e.EffectId == burning, "the water put the fire out");
        drySink.StatusSyncs.Last(s => s.ConnectionId == 1UL).Entries.Should().Contain(e => e.EffectId == burning);
    }

    [Fact]
    public void ComputeVisibility_HalfRadiusScale_ShrinksBothTheEnterAndTheExitRadius()
    {
        WorldSimulation simulation = new();
        _ = simulation.SpawnNetworkEntity(1UL, Vector2.Zero, 7f);
        _ = simulation.SpawnNetworkEntity(2UL, new Vector2(300f, 0f), 7f);
        _ = simulation.SpawnNetworkEntity(3UL, new Vector2(500f, 0f), 7f);
        SpatialAoiTracker tracker = new(768f);
        tracker.TrackConnection(5UL);
        List<ulong> entered = [];
        List<ulong> visible = [];
        List<ulong> exited = [];

        tracker.ComputeVisibility(5UL, 1UL, Vector2.Zero, simulation, entered, visible, exited, radiusScale: 0.5f);

        entered.Should().Equal([2UL], "only the entity inside 384 enters");
        tracker.ComputeVisibility(5UL, 1UL, Vector2.Zero, simulation, entered, visible, exited, radiusScale: 1f);
        entered.Should().Equal([3UL]);
        tracker.ComputeVisibility(5UL, 1UL, Vector2.Zero, simulation, entered, visible, exited, radiusScale: 0.5f);
        exited.Should().Equal([3UL], "500 is beyond the 416 exit radius at half scale");
    }

    // Seed 42 (OxygenWorldTests): the beach at 954,-6000 faces open sea due east.
    private const ulong WaterSeed = 42UL;
    private const float WaterTileSize = 32f;
    private const int CoastX = 954;
    private const int CoastY = -6000;
    private const int DryX = 940;

    private static ushort Id(string key) =>
        StatusEffectCatalog.LoadShippedDefaults().TryGetByKey(key, out StatusEffectDefinition def) ? def.Id : (ushort)0;

    private static ulong FirstProcTick(ushort archetype, ushort effectId)
    {
        for (ulong tick = 1UL; tick < 500UL; tick++)
        {
            Harness h = new(entropyLabel: 7UL);
            if (h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, archetype, dealt: 5f, victimMaxHealth: 100f, tick) &&
                h.Buffer.TryGet(effectId, out _))
            {
                return tick;
            }
        }

        throw new InvalidOperationException("No proc in 500 ticks.");
    }

    private static byte ApplyArcherHit(ulong tick, int monsterLevel, bool night)
    {
        Harness h = new(entropyLabel: 7UL);
        _ = h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, SkeletonArcher, dealt: 5f, victimMaxHealth: 100f, tick, monsterLevel, night);
        return h.Buffer.TryGet(Poisoned, out ActiveStatusEffect poison) ? poison.Level : (byte)0;
    }

    private static Vector2 ShallowTile()
    {
        ProceduralWorldGenerator generator = new(WaterSeed);
        for (int x = CoastX + 1; x < CoastX + 400; x++)
        {
            if (WaterDepth.TierOf(generator.SampleTile(x, CoastY)) == WaterDepthTier.Shallow)
            {
                return new Vector2((x + 2.5f) * WaterTileSize, (CoastY + 0.5f) * WaterTileSize);
            }
        }

        throw new InvalidOperationException("No shallow water on the transect.");
    }

    private static CharacterRecord WaterCharacter(Vector2 position) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Burner", 1, 0, position.X, position.Y, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);

    private static (AuthoritativeWorld World, StatusRecordingSink Sink) WaterWorld(string statusEffects)
    {
        StatusRecordingSink sink = new();
        AuthoritativeWorld world = new(
            WaterSeed,
            WaterTileSize,
            ProceduralWorldGenerator.DefaultFrequency,
            30,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            sink,
            entropy: TestGameplayEntropy.Create(7UL));
        _ = world.ApplyDebugOverrides(new WorldDebugOverrides { StatusEffects = statusEffects });
        return (world, sink);
    }

    private sealed class Harness
    {
        public Harness(ulong entropyLabel, StatusEffectCatalog? catalog = null)
        {
            this.Service = new StatusEffectService(this.Simulation, 30, catalog, CreatureContent.LoadDefault().Catalog, TestGameplayEntropy.Create(entropyLabel));
            this.Player = this.Simulation.SpawnEntity(Vector2.Zero, radius: 8f);
            _ = this.Service.Attach(this.Player);
        }

        public WorldSimulation Simulation { get; } = new(tickRate: 30);

        public StatusEffectService Service { get; }

        public Entity Player { get; }

        public StatusEffectBuffer Buffer => this.Service.TryGetBuffer(this.Player, out StatusEffectBuffer buffer) ? buffer : throw new InvalidOperationException("no buffer");
    }
}
