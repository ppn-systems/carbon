// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Navigation;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Combat.Attacks;
using Carbon.Combat.Projectiles;
using Carbon.Combat.Stats;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Maps.Biomes;
using Carbon.Maps.Navigation;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Testing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Creatures.Tests;

/// <summary>
/// Deterministic tick-stepping scenarios for the data-driven monster tactics (docs/monster-ai.md): kiting, surround,
/// pathing, leash / give-up, flee, pack assist, threat switching, night aggression, telegraphs, allocations and CPU.
/// </summary>
public sealed class MonsterTacticsTests(ITestOutputHelper output)
{
    private const float Tile = 32f;

    [Fact]
    public void Update_PlayerAdvancingOnArcher_ArcherBacksOffAndKeepsShooting()
    {
        TacticsArena arena = new();
        Vector2 start = new(1600f, 1600f);
        Entity archer = arena.SpawnMonster("skeleton_archer", start);
        Entity player = arena.SpawnPlayer(start + new Vector2(200f, 0f), health: 1e6f);
        arena.SetVelocity(player, Vector2.Zero);

        float minDist = float.MaxValue;
        float retreated = 0f;
        int launches = 0;
        int engagedTicks = 0;
        for (int i = 0; i < 320 && arena.Brain(archer).State != AiState.ReturnHome; i++)
        {
            // The player walks straight at the archer at ~40% of its speed.
            Vector2 toArcher = arena.Position(archer) - arena.Position(player);
            arena.SetVelocity(player, Vector2.Normalize(toArcher) * 55f);
            arena.StepTick();
            launches += arena.World.ReadEvents<ProjectileLaunchedEvent>().Length;
            engagedTicks++;
            if (i >= 30)
            {
                minDist = MathF.Min(minDist, arena.Distance(archer, player));
            }

            retreated = MathF.Max(retreated, start.X - arena.Position(archer).X);
        }

        engagedTicks.Should().BeGreaterThan(200, "the archer kites for several seconds before its leash ends the chase");
        minDist.Should().BeGreaterThan(75f, "the archer backs off whenever the player closes inside its preferred band");
        retreated.Should().BeGreaterThan(150f, "the archer gives ground instead of standing still");
        launches.Should().BeGreaterThan(3, "the archer keeps shooting while it kites");
    }

    [Fact]
    public void Update_ArcherAtPreferredRange_StrafesWhileHoldingBand()
    {
        TacticsArena arena = new();
        Vector2 playerPos = new(1600f, 1600f);
        Entity player = arena.SpawnPlayer(playerPos, health: 1e6f);
        Entity archer = arena.SpawnMonster("skeleton_archer", playerPos + new Vector2(170f, 0f));

        Vector2 startPos = arena.Position(archer);
        float pathLength = 0f;
        Vector2 previous = startPos;
        for (int i = 0; i < 300; i++)
        {
            arena.StepTick();
            Vector2 now = arena.Position(archer);
            pathLength += Vector2.Distance(previous, now);
            previous = now;
            arena.Distance(archer, player).Should().BeInRange(105f, 225f, "the archer stays inside its engage band (tick {0})", i);
        }

        pathLength.Should().BeGreaterThan(40f, "between shots the archer circle-strafes around its target");
    }

    [Fact]
    public void Update_FourGolemsApproachFromOneSide_SurroundTargetWithoutStacking()
    {
        TacticsArena arena = new();
        Vector2 playerPos = new(1600f, 1600f);
        Entity player = arena.SpawnPlayer(playerPos, health: 1e7f);
        Entity[] golems =
        [
            arena.SpawnMonster("rock_golem", playerPos + new Vector2(-90f, -12f)),
            arena.SpawnMonster("rock_golem", playerPos + new Vector2(-90f, 12f)),
            arena.SpawnMonster("rock_golem", playerPos + new Vector2(-110f, 0f)),
            arena.SpawnMonster("rock_golem", playerPos + new Vector2(-70f, 0f))
        ];

        for (int i = 0; i < 300; i++)
        {
            arena.StepTick();
        }

        float[] bearings = new float[golems.Length];
        for (int a = 0; a < golems.Length; a++)
        {
            arena.Distance(golems[a], player).Should().BeLessThan(45f, "every golem is engaged at melee reach");
            Vector2 d = arena.Position(golems[a]) - playerPos;
            bearings[a] = MathF.Atan2(d.Y, d.X);
            for (int b = a + 1; b < golems.Length; b++)
            {
                arena.Distance(golems[a], golems[b]).Should().BeGreaterThan(16f, "golems {0} and {1} must not stack on one tile", a, b);
            }
        }

        Array.Sort(bearings);
        float largestGap = bearings[0] + (2f * MathF.PI) - bearings[^1];
        for (int k = 1; k < bearings.Length; k++)
        {
            largestGap = MathF.Max(largestGap, bearings[k] - bearings[k - 1]);
        }

        largestGap.Should().BeLessThan(MathF.PI * 1.25f, "the pack spreads around the target instead of queuing on the approach side");
    }

    [Fact]
    public void Update_PlayerBehindWall_MeleePathsAroundWallAndHits()
    {
        TacticsArena arena = new(withWalls: true);

        // Vertical wall at tile column 10 from row 4 to row 14; the golem must go around an end.
        for (int y = 4; y <= 14; y++)
        {
            arena.Grid!.SetSolid(10, y, true);
        }

        Entity golem = arena.SpawnMonster("rock_golem", TacticsArena.TileCenter(7, 9));
        Entity player = arena.SpawnPlayer(TacticsArena.TileCenter(13, 9), health: 1e6f);
        arena.Threat(golem).AddThreat(player, 50f);

        int reachedTick = -1;
        for (int i = 0; i < 400 && reachedTick < 0; i++)
        {
            arena.StepTick();
            if (arena.Distance(golem, player) < 40f)
            {
                reachedTick = i;
            }
        }

        reachedTick.Should().BeGreaterThan(0, "the golem paths around the wall to the player");
        arena.Ai.PathSearchesLastTick.Should().BeLessThanOrEqualTo(arena.Ai.MaxPathSearchesPerTick);
    }

    [Fact]
    public void Update_PlayerOutrunsLeash_MonsterReturnsHomeAndRegenerates()
    {
        TacticsArena arena = new();
        Vector2 home = new(1600f, 1600f);
        Entity golem = arena.SpawnMonster("rock_golem", home);
        Entity player = arena.SpawnPlayer(home + new Vector2(120f, 0f), health: 1e6f);
        arena.SetVelocity(player, Vector2.Zero);
        ref HealthComponent hp = ref arena.World.Entities.GetHealth(golem);
        float maxHp = hp.Max;
        hp = new HealthComponent(maxHp * 0.4f, maxHp);

        arena.StepTick();
        arena.Brain(golem).State.Should().Be(AiState.Chase);

        // The player runs away faster than the golem until the golem passes its leash.
        arena.SetVelocity(player, new Vector2(220f, 0f));
        bool leashed = false;
        for (int i = 0; i < 200 && !leashed; i++)
        {
            arena.StepTick();
            leashed = arena.Brain(golem).State == AiState.ReturnHome;
        }

        leashed.Should().BeTrue("passing leashRange sends the golem home");
        float hpAtLeash = arena.World.Entities.GetHealth(golem).Current;

        AiState state = AiState.ReturnHome;
        for (int i = 0; i < 300 && state == AiState.ReturnHome; i++)
        {
            arena.StepTick();
            state = arena.Brain(golem).State;
        }

        state.Should().BeOneOf(AiState.Patrol, AiState.Idle);
        Vector2.Distance(arena.Position(golem), home).Should().BeLessThan(12f, "the golem walked back to its spawn point");
        arena.World.Entities.GetHealth(golem).Current.Should().Be(maxHp, "leashing fully regenerates health");
        hpAtLeash.Should().BeLessThan(maxHp);
    }

    [Fact]
    public void Update_ManyMonstersNeedPathsSameTick_SearchesAreTimeSlicedByBudget()
    {
        TacticsArena arena = new(withWalls: true);
        for (int y = 2; y <= 30; y++)
        {
            arena.Grid!.SetSolid(20, y, true);
        }

        Entity player = arena.SpawnPlayer(TacticsArena.TileCenter(24, 16), health: 1e9f);
        List<Entity> golems = [];
        for (int k = 0; k < 30; k++)
        {
            Entity golem = arena.SpawnMonster("rock_golem", TacticsArena.TileCenter(14 + (k % 5), 10 + (k / 5 * 2)));
            arena.Threat(golem).AddThreat(player, 10f);
            golems.Add(golem);
        }

        int totalSearches = 0;
        int deferred = 0;
        for (int i = 0; i < 6; i++)
        {
            arena.StepTick();
            arena.Ai.PathSearchesLastTick.Should().BeLessThanOrEqualTo(arena.Ai.MaxPathSearchesPerTick, "tick {0} respects the A* budget", i);
            totalSearches += arena.Ai.PathSearchesLastTick;
            deferred += arena.Ai.PathSearchesDeferredLastTick;
        }

        deferred.Should().BeGreaterThan(0, "30 simultaneous requests exceed the per-tick budget and are postponed");
        totalSearches.Should().BeGreaterThanOrEqualTo(golems.Count, "every postponed request is served within a few ticks");
    }

    [Fact]
    public void Update_TargetUnreachable_MonsterGivesUpAfterGiveUpTicks()
    {
        TacticsArena arena = new(withWalls: true);

        // A closed stone box around the player: no path exists.
        for (int x = 11; x <= 15; x++)
        {
            arena.Grid!.SetSolid(x, 7, true);
            arena.Grid.SetSolid(x, 11, true);
        }

        for (int y = 7; y <= 11; y++)
        {
            arena.Grid!.SetSolid(11, y, true);
            arena.Grid.SetSolid(15, y, true);
        }

        Entity wolf = arena.SpawnMonster("frost_wolf", TacticsArena.TileCenter(8, 9));
        Entity player = arena.SpawnPlayer(TacticsArena.TileCenter(13, 9), health: 1e6f);
        arena.Threat(wolf).AddThreat(player, 50f);
        uint giveUp = arena.Catalog.GetDefinition(arena.Catalog.GetIndex("frost_wolf")).Behavior.GiveUpTicks;

        int gaveUpAt = -1;
        for (int i = 0; i < (int)giveUp + 40 && gaveUpAt < 0; i++)
        {
            arena.StepTick();
            if (arena.Brain(wolf).State == AiState.ReturnHome)
            {
                gaveUpAt = i;
            }
        }

        gaveUpAt.Should().BeInRange((int)giveUp - 5, (int)giveUp + 5, "the wolf evades once it chased for giveUpTicks without reaching the player");
    }

    [Fact]
    public void Update_WolfDropsToLowHealth_FleesThenReengages()
    {
        TacticsArena arena = new();
        Vector2 home = new(1600f, 1600f);
        Entity wolf = arena.SpawnMonster("frost_wolf", home);
        Entity player = arena.SpawnPlayer(home + new Vector2(30f, 0f), health: 1e6f);

        for (int i = 0; i < 20; i++)
        {
            arena.StepTick();
        }

        ref HealthComponent hp = ref arena.World.Entities.GetHealth(wolf);
        hp = new HealthComponent(hp.Max * 0.2f, hp.Max);
        float distBefore = arena.Distance(wolf, player);

        arena.StepTick();
        arena.Brain(wolf).State.Should().Be(AiState.Flee, "the wolf is below its 25% flee threshold");

        for (int i = 0; i < 50; i++)
        {
            arena.StepTick();
            arena.Brain(wolf).State.Should().Be(AiState.Flee);
        }

        arena.Distance(wolf, player).Should().BeGreaterThan(distBefore + 100f, "a fleeing wolf runs away from its attacker");
        arena.World.Entities.GetComponent<AttackStateComponent>(wolf).IsBusy.Should().BeFalse("fleeing cancels attacks");

        for (int i = 0; i < 40; i++)
        {
            arena.StepTick();
        }

        arena.Brain(wolf).State.Should().BeOneOf(AiState.Chase, AiState.Attack);
        arena.Brain(wolf).HasFled.Should().BeTrue("a monster flees only once per engagement");
    }

    [Fact]
    public void Update_SlimeAggroes_SamePackAssistsAndOtherPackIgnores()
    {
        TacticsArena arena = new();
        Vector2 playerPos = new(1600f, 1600f);
        Entity player = arena.SpawnPlayer(playerPos, health: 1e6f);
        Entity puller = arena.SpawnMonster("wild_slime", playerPos + new Vector2(-150f, 0f));
        Entity packmate = arena.SpawnMonster("wild_slime", playerPos + new Vector2(-300f, 0f));
        Entity king = arena.SpawnMonster("slime_king", playerPos + new Vector2(-290f, 60f));
        Entity golem = arena.SpawnMonster("rock_golem", playerPos + new Vector2(-300f, -40f));

        arena.Distance(packmate, player).Should().BeGreaterThan(192f, "precondition: the packmate cannot see the player itself");
        arena.Distance(golem, player).Should().BeGreaterThan(192f, "precondition: the golem cannot see the player itself");

        arena.StepTick();

        arena.Brain(puller).State.Should().Be(AiState.Chase);
        arena.Brain(packmate).State.Should().Be(AiState.Chase, "same-pack allies within assistRadius join the pull");
        arena.Brain(packmate).TargetEntity.Should().Be(player);
        arena.Brain(king).State.Should().Be(AiState.Chase, "the slime king shares the slime pack");
        arena.Brain(golem).State.Should().BeOneOf([AiState.Idle, AiState.Patrol], "a golem is not part of the slime pack");
    }

    [Fact]
    public void Update_SecondPlayerOutThreatsFirst_SwitchesOnlyPastMarginAndHonorsTaunt()
    {
        TacticsArena arena = new();
        Vector2 home = new(1600f, 1600f);
        Entity golem = arena.SpawnMonster("rock_golem", home);
        Entity tank = arena.SpawnPlayer(home + new Vector2(25f, 0f), health: 1e6f);
        Entity dps = arena.SpawnPlayer(home + new Vector2(-60f, 0f), health: 1e6f);
        ThreatTable threat = arena.Threat(golem);
        threat.AddThreat(tank, 100f);

        arena.StepTick();
        arena.Brain(golem).TargetEntity.Should().Be(tank, "the tank leads the threat table");

        threat.AddThreat(dps, 120f);
        arena.StepTick();
        arena.Brain(golem).TargetEntity.Should().Be(tank, "120 threat does not beat 100 x 1.25 switch margin");

        threat.AddThreat(dps, 10f);
        arena.StepTick();
        arena.Brain(golem).TargetEntity.Should().Be(dps, "130 threat exceeds the 125 switch threshold");

        threat.Taunt(tank, untilTick: arena.TickIndex + 30UL);
        arena.StepTick();
        arena.Brain(golem).TargetEntity.Should().Be(tank, "a taunt forces the taunter as target");

        threat.AddThreat(dps, 500f);
        arena.StepTick();
        arena.Brain(golem).TargetEntity.Should().Be(tank, "the taunt holds for its duration regardless of threat");

        for (int i = 0; i < 35; i++)
        {
            arena.StepTick();
        }

        arena.Brain(golem).TargetEntity.Should().Be(dps, "after the taunt expires the threat leader wins again");
    }

    [Fact]
    public void Update_AtNight_WolvesAggroFromFurther()
    {
        foreach (bool night in new[] { false, true })
        {
            TacticsArena arena = new();
            Vector2 home = new(1600f, 1600f);
            Entity wolf = arena.SpawnMonster("frost_wolf", home);
            _ = arena.SpawnPlayer(home + new Vector2(270f, 0f), health: 1e6f);
            arena.Ai.IsNight = night;

            arena.StepTick();

            AiState state = arena.Brain(wolf).State;
            if (night)
            {
                state.Should().Be(AiState.Chase, "night aggro range is 220 x 1.4 = 308");
            }
            else
            {
                state.Should().BeOneOf([AiState.Idle, AiState.Patrol], "day aggro range is 220");
            }
        }
    }

    [Fact]
    public void Update_CasterSpell_TelegraphsTargetAreaBeforeImpact()
    {
        TacticsArena arena = new();
        Vector2 playerPos = new(1600f, 1600f);
        Entity player = arena.SpawnPlayer(playerPos, health: 1e6f);
        Entity wraith = arena.SpawnMonster("ash_wraith", playerPos + new Vector2(150f, 0f));

        int windupTick = -1;
        int activeTick = -1;
        Vector2 telegraphed = default;
        for (int i = 0; i < 200 && activeTick < 0; i++)
        {
            arena.StepTick();
            foreach (AttackPhaseChangedEvent e in arena.World.ReadEvents<AttackPhaseChangedEvent>())
            {
                if (e.Attacker != wraith || e.Kind != AttackKind.Spell)
                {
                    continue;
                }

                if (e.Phase == AttackPhase.Windup && windupTick < 0)
                {
                    windupTick = i;
                    telegraphed = e.TargetPosition;
                    e.AreaRadius.Should().BeGreaterThan(0f, "the telegraph carries the blast radius");
                }
                else if (e.Phase == AttackPhase.Active && windupTick >= 0)
                {
                    activeTick = i;
                }
            }
        }

        windupTick.Should().BeGreaterThanOrEqualTo(0, "the caster telegraphs its spell");
        activeTick.Should().BeGreaterThan(windupTick + 10, "players get the windup time to step out");
        Vector2.Distance(telegraphed, playerPos).Should().BeLessThan(8f, "the telegraph marks the player's position");
        arena.Distance(wraith, player).Should().BeGreaterThan(80f, "casters keep their distance");
    }

    [Fact]
    public void Update_SameScenarioTwice_ProducesIdenticalPositions()
    {
        Vector2[] first = RunMixedScenario(ticks: 240);
        Vector2[] second = RunMixedScenario(ticks: 240);
        second.Should().Equal(first, "the AI is fully deterministic");
    }

    [Fact]
    public void Update_SteadyStateTacticsTicks_AllocateNothing()
    {
        TacticsArena arena = BuildMixedArena(out _);

        AllocationResult window = AllocationProbe.Measure(300, warmupIterations: 400, i => arena.StepTick());

        window.Bytes.Should().Be(0, $"kiting, surround, pack, flee, threat, and budgeted pathing ticks are zero-allocation ({window})");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Update_FiveHundredMonstersWithWalls_StaysWithinTickAndPathBudget()
    {
        TacticsArena arena = new(withWalls: true, gridTiles: 160);
        string[] kinds = ["rock_golem", "frost_wolf", "skeleton_archer", "orc_shaman", "wild_slime"];

        // Wall segments every 8 tiles force pathing.
        for (int gx = 4; gx < 156; gx += 8)
        {
            for (int gy = 4; gy < 156; gy += 8)
            {
                for (int k = 0; k < 4; k++)
                {
                    arena.Grid!.SetSolid(gx, gy + k, true);
                }
            }
        }

        List<Entity> monsters = [];
        for (int p = 0; p < 50; p++)
        {
            int px = 10 + (p % 10 * 14);
            int py = 10 + (p / 10 * 28);
            _ = arena.SpawnPlayer(TacticsArena.TileCenter(px, py), health: 1e9f);
            for (int m = 0; m < 10; m++)
            {
                Vector2 offset = new(((m % 5) - 2) * 40f, ((m / 5 * 2) - 1) * 110f);
                monsters.Add(arena.SpawnMonster(kinds[(p + m) % kinds.Length], TacticsArena.TileCenter(px, py) + offset));
            }
        }

        for (int i = 0; i < 60; i++)
        {
            arena.StepTick();
        }

        const int Measured = 200;
        int maxSearches = 0;
        int deferred = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < Measured; i++)
        {
            arena.StepTick();
            maxSearches = Math.Max(maxSearches, arena.Ai.PathSearchesLastTick);
            deferred += arena.Ai.PathSearchesDeferredLastTick;
        }

        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        double msPerTick = sw.Elapsed.TotalMilliseconds / Measured;
        output.WriteLine($"{monsters.Count} monsters / 50 players: {msPerTick:F3} ms per tick, max {maxSearches} A* per tick, {deferred} deferred, {allocated} B allocated");

        maxSearches.Should().BeLessThanOrEqualTo(arena.Ai.MaxPathSearchesPerTick, "A* searches are capped per tick");
        msPerTick.Should().BeLessThan(33.3, "500 monsters must fit comfortably inside a 30 Hz tick even on a slow CI runner");
    }

    private static Vector2[] RunMixedScenario(int ticks)
    {
        TacticsArena arena = BuildMixedArena(out List<Entity> tracked);
        for (int i = 0; i < ticks; i++)
        {
            arena.StepTick();
        }

        Vector2[] positions = new Vector2[tracked.Count];
        for (int i = 0; i < tracked.Count; i++)
        {
            positions[i] = arena.Position(tracked[i]);
        }

        return positions;
    }

    private static TacticsArena BuildMixedArena(out List<Entity> tracked)
    {
        TacticsArena arena = new(withWalls: true);
        for (int y = 6; y <= 12; y++)
        {
            arena.Grid!.SetSolid(9, y, true);
        }

        Entity player = arena.SpawnPlayer(TacticsArena.TileCenter(12, 9), health: 1e9f);
        Entity tank = arena.SpawnPlayer(TacticsArena.TileCenter(13, 11), health: 1e9f);
        tracked =
        [
            arena.SpawnMonster("rock_golem", TacticsArena.TileCenter(6, 8)),
            arena.SpawnMonster("rock_golem", TacticsArena.TileCenter(6, 10)),
            arena.SpawnMonster("frost_wolf", TacticsArena.TileCenter(15, 6)),
            arena.SpawnMonster("frost_wolf", TacticsArena.TileCenter(16, 7)),
            arena.SpawnMonster("skeleton_archer", TacticsArena.TileCenter(17, 12)),
            arena.SpawnMonster("orc_shaman", TacticsArena.TileCenter(12, 15)),
            arena.SpawnMonster("wild_slime", TacticsArena.TileCenter(14, 9)),
            arena.SpawnMonster("crimson_demon", TacticsArena.TileCenter(10, 4)),
            player,
            tank
        ];

        arena.Threat(tracked[0]).AddThreat(player, 40f);
        arena.Threat(tracked[2]).AddThreat(tank, 40f);
        ref HealthComponent hp = ref arena.World.Entities.GetHealth(tracked[3]);
        hp = new HealthComponent(hp.Max * 0.2f, hp.Max);
        return arena;
    }

    /// <summary>Navigation adapter exposing a <see cref="TileObstacleGrid"/> to the A* pathfinder.</summary>
    private sealed class ObstacleWalkability(TileObstacleGrid grid) : ITileWalkabilityGrid
    {
        public TileWalkability GetWalkability(int worldTileX, int worldTileY) =>
            this.IsWalkable(worldTileX, worldTileY) ? TileWalkability.WalkableNormal : TileWalkability.Impassable;

        public float GetMoveCost(int worldTileX, int worldTileY) => 1f;

        public bool IsWalkable(int worldTileX, int worldTileY) =>
            grid.IsInBounds(worldTileX, worldTileY) && !grid.IsSolid(worldTileX, worldTileY);

        public byte GetHeightLevel(int worldTileX, int worldTileY) => 0;

        public bool CanTraverse(int fromX, int fromY, int toX, int toY) => this.IsWalkable(toX, toY);
    }

    [Fact]
    public void Respawn_PathingGolemIntoTheSameSlot_AllocatesItsAttachmentsAgain()
    {
        // Characterisation (ADR-044): each life of a monster gets new attachments, even in the slot of the previous one.
        TacticsArena arena = new(withWalls: true);
        for (int y = 4; y <= 14; y++)
        {
            arena.Grid!.SetSolid(10, y, true);
        }

        Entity player = arena.SpawnPlayer(TacticsArena.TileCenter(13, 9), health: 1e6f);
        List<(Entity Golem, StatSheet Sheet, ThreatTable Threat, PathWaypointBuffer Path)> lives = [];
        for (int life = 0; life < 4; life++)
        {
            lives.Add(LivePathingLife(arena, player));
        }

        lives.Should().OnlyContain(l => l.Golem.Id == lives[0].Golem.Id, "every life respawns into the freed slot");
        for (int i = 1; i < lives.Count; i++)
        {
            lives[i].Sheet.Should().NotBeSameAs(lives[i - 1].Sheet);
            lives[i].Threat.Should().NotBeSameAs(lives[i - 1].Threat);
            lives[i].Path.Should().NotBeSameAs(lives[i - 1].Path);
        }

        AllocationResult window = AllocationProbe.Measure(1, warmupIterations: 2, i => _ = LivePathingLife(arena, player));
        window.Bytes.Should().BeGreaterThan(0, $"a respawn allocates its StatSheet, ThreatTable and PathWaypointBuffer again ({window})");
    }

    /// <summary>Spawns a golem behind the wall, lets it path towards the player, and despawns it (one monster life).</summary>
    private static (Entity Golem, StatSheet Sheet, ThreatTable Threat, PathWaypointBuffer Path) LivePathingLife(TacticsArena arena, Entity player)
    {
        Entity golem = arena.SpawnMonster("rock_golem", TacticsArena.TileCenter(7, 9));
        ThreatTable threat = arena.Threat(golem);
        threat.AddThreat(player, 50f);
        for (int t = 0; t < 30; t++)
        {
            arena.StepTick();
        }

        EntityManager entities = arena.World.Entities;
        if (!entities.GetAttachmentStore<StatSheet>().TryGet(golem, out StatSheet? sheet) ||
            !entities.GetAttachmentStore<PathWaypointBuffer>().TryGet(golem, out PathWaypointBuffer? path))
        {
            throw new InvalidOperationException("the golem should have a stat sheet and a path around the wall");
        }

        arena.World.DespawnEntity(golem);
        arena.StepTick();
        return (golem, sheet, threat, path);
    }

    private sealed class TacticsArena
    {
        private const double TickSeconds = 1.0 / 30.0;

        private readonly SpawningConfig _config = CreatureTestData.LoadSpawning();

        public TacticsArena(bool withWalls = false, int gridTiles = 100)
        {
            this.World = new WorldSimulation(tickRate: 30);
            ITileWalkabilityGrid? nav = null;
            if (withWalls)
            {
                this.Grid = new TileObstacleGrid(gridTiles, gridTiles, Tile);
                this.World.WorldPhysics = this.Grid;
                nav = new ObstacleWalkability(this.Grid);
            }

            this.Ai = new AiBehaviorSystem(nav, Tile);
            this.Systems = CreatureSystems.Register(
                this.World,
                this.Catalog,
                _config,
                new FixedBiomeSampler(BiomeType.LushPlains),
                Vector2.Zero,
                entropy: TestGameplayKeys.Entropy(7UL),
                ai: this.Ai);
            this.World.RegisterSystem(new ProjectileSystem());
        }

        public MonsterCatalog Catalog { get; } = CreatureTestData.LoadCatalog();

        public WorldSimulation World { get; }

        public CreatureSystems Systems { get; }

        public AiBehaviorSystem Ai { get; }

        public TileObstacleGrid? Grid { get; }

        public ulong TickIndex { get; private set; }

        public static Vector2 TileCenter(int x, int y) => new((x + 0.5f) * Tile, (y + 0.5f) * Tile);

        public Entity SpawnPlayer(Vector2 position, float health)
        {
            Entity player = this.World.SpawnEntity(position, radius: 6f);
            this.World.Entities.SetCollider(player, Collider2D.CreateCircle(6f) with { Layer = _config.TargetLayerMask });
            this.World.Entities.SetHealth(player, new HealthComponent(health, health));
            return player;
        }

        public Entity SpawnMonster(string id, Vector2 position)
        {
            ushort index = this.Catalog.GetIndex(id);
            ScaledMonsterStats stats = this.Systems.Spawner.Scaling.Compute(0f, isNight: false, this.Catalog.GetDefinition(index));
            Entity monster = MonsterFactory.Spawn(this.World, this.Catalog, index, position, in stats, _config.MonsterLayer, _config.TargetLayerMask);
            _ = this.World.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(monster, static () => new ThreatTable());
            return monster;
        }

        public ThreatTable Threat(Entity monster) =>
            this.World.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(monster, static () => new ThreatTable());

        public AiBrainComponent Brain(Entity monster) => this.World.Entities.GetComponent<AiBrainComponent>(monster);

        public void SetVelocity(Entity entity, Vector2 velocity) => this.World.Entities.SetVelocity(entity, new Velocity2D(velocity));

        public void StepTick()
        {
            this.World.ClearAllEvents();
            this.World.Tick(new FixedTickArgs(this.TickIndex, (float)TickSeconds, this.TickIndex * TickSeconds));
            this.TickIndex++;
        }

        public Vector2 Position(Entity entity) => this.World.Entities.GetTransform(entity).Position;

        public float Distance(Entity a, Entity b) => Vector2.Distance(this.Position(a), this.Position(b));
    }
}
