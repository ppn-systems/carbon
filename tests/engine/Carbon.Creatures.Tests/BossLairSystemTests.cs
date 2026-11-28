// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Creatures.Systems;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

/// <summary>ADR-032 D15: the abyss boss rises at its lair, respawns on a timer, and never leaves a fight.</summary>
public sealed class BossLairSystemTests
{
    private static readonly Vector2 s_lair = new(40_000f, 3_000f);

    [Fact]
    public void Update_PlayerReachesTheLair_RaisesTheBossOnceAtTheLair()
    {
        LairArena arena = new();
        arena.Player(s_lair + new Vector2(600f, 0f));

        arena.Step(arena.Config.ScanIntervalTicks * 4);

        Entity boss = arena.Boss;
        boss.IsValid.Should().BeTrue("a player within the activation radius raises the boss");
        arena.Catalog.GetDefinition(arena.World.Entities.GetComponent<MonsterComponent>(boss).MonsterIndex).Id.Should().Be("abyss_leviathan");
        arena.World.Entities.GetTransform(boss).Position.Should().Be(s_lair);
        arena.BossCount().Should().Be(1, "one lair holds one boss");
    }

    [Fact]
    public void Update_NoPlayerNearTheLair_RaisesNothing()
    {
        LairArena arena = new();
        arena.Player(s_lair + new Vector2(arena.Lairs.ActivationRadius + 200f, 0f));

        arena.Step(arena.Config.ScanIntervalTicks * 4);

        arena.Boss.IsValid.Should().BeFalse();
        arena.BossCount().Should().Be(0);
    }

    [Fact]
    public void Update_BossKilled_RisesAgainOnlyAfterTheRespawnTimer()
    {
        LairArena arena = new();
        arena.Player(s_lair + new Vector2(600f, 0f));
        arena.Step(arena.Config.ScanIntervalTicks);
        Entity first = arena.Boss;

        arena.Kill(first);
        arena.Step(arena.Config.ScanIntervalTicks * 2);
        arena.Boss.IsValid.Should().BeFalse("the lair is empty right after the kill");

        arena.Step(arena.Lairs.RespawnTicks - (arena.Config.ScanIntervalTicks * 4));
        arena.Boss.IsValid.Should().BeFalse("the respawn timer has not run out");

        arena.Step(arena.Config.ScanIntervalTicks * 4);
        arena.Boss.IsValid.Should().BeTrue("the boss rises again once the timer has run out");
        arena.Boss.Should().NotBe(first);
    }

    [Fact]
    public void Update_LeftAlone_SinksBackAndRisesWholeWhenAPlayerReturns()
    {
        LairArena arena = new();
        Entity player = arena.Player(s_lair + new Vector2(600f, 0f));
        arena.Step(arena.Config.ScanIntervalTicks);
        Entity first = arena.Boss;
        arena.Wound(first, 0.5f);

        arena.Move(player, new Vector2(-50_000f, 0f));
        arena.Step(arena.Lairs.IdleDespawnTicks + (arena.Config.ScanIntervalTicks * 2));
        arena.World.Entities.IsAlive(first).Should().BeFalse("a boss nobody is near sinks back into its lair");

        arena.Move(player, s_lair + new Vector2(600f, 0f));
        arena.Step(arena.Config.ScanIntervalTicks * 2);
        Entity second = arena.Boss;
        second.IsValid.Should().BeTrue("it rises again at once, with no respawn wait");
        HealthComponent hp = arena.World.Entities.GetHealth(second);
        hp.Current.Should().Be(hp.Max, "it rises whole");
    }

    [Fact]
    public void Update_FightInItsChunkBeyondTheActivationRadius_NeverSinksBack()
    {
        LairArena arena = new(chunkSize: 16_384f);
        Entity player = arena.Player(s_lair + new Vector2(600f, 0f));
        arena.Step(arena.Config.ScanIntervalTicks);
        Entity boss = arena.Boss;

        // The player fights from the edge of the boss's chunk, beyond the activation radius: it keeps wounding it.
        arena.Move(player, s_lair + new Vector2(arena.Lairs.ActivationRadius + 800f, 0f));
        for (uint t = 0; t < arena.Lairs.IdleDespawnTicks * 2; t += arena.Config.ScanIntervalTicks)
        {
            arena.Wound(boss, 0.001f);
            arena.Step(arena.Config.ScanIntervalTicks);
        }

        arena.World.Entities.IsAlive(boss).Should().BeTrue("a boss never sinks back while a player in its chunk fights it");

        arena.Step(arena.Lairs.CombatGraceTicks + (arena.Config.ScanIntervalTicks * 2));
        arena.World.Entities.IsAlive(boss).Should().BeFalse("once the fight stops and nobody is near, it sinks back");
    }

    [Fact]
    public void Update_BossLevel_FollowsTheLairsDistanceFromTheOrigin()
    {
        LairArena arena = new();
        arena.Player(s_lair + new Vector2(600f, 0f));

        arena.Step(arena.Config.ScanIntervalTicks);

        MonsterDefinition def = arena.Catalog.GetDefinition(arena.Catalog.GetIndex("abyss_leviathan"));
        int expected = new MonsterScaling(arena.Config).Compute(s_lair.Length(), false, def).Level;
        arena.World.Entities.GetComponent<MonsterComponent>(arena.Boss).Level.Should().Be(expected);
    }

    [Fact]
    public void Update_PlayerTravelsFarAcrossCells_KeepsOnlyTheCellsItsScanReaches()
    {
        LairArena arena = new();
        Entity player = arena.Player(new Vector2(0f, 3_000f));

        arena.TravelAlongX(player, toX: 40 * 32_768f);

        // ALLOC-7: a 1 280-unit scan reaches at most 2 x 2 of the 32 768-unit cells; the 40 cells passed are gone.
        arena.System.CellCount.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void Update_PlayerStaysOutsideTheActivationRadius_LooksEachCellUpOnce()
    {
        LairArena arena = new();
        arena.Player(s_lair + new Vector2(arena.Lairs.ActivationRadius + 200f, 0f));
        arena.Step(arena.Config.ScanIntervalTicks);
        int lookups = arena.Source.Lookups;

        arena.Step(arena.Config.ScanIntervalTicks * 20);

        arena.Source.Lookups.Should().Be(lookups, "a cell a scan still reaches stays cached, lair or not");
    }

    [Fact]
    public void Update_BossKilledThenPlayerTravelsFarAndBack_TheRespawnTimerStillRuns()
    {
        LairArena arena = new();
        Entity player = arena.Player(s_lair + new Vector2(600f, 0f));
        arena.Step(arena.Config.ScanIntervalTicks);
        arena.Kill(arena.Boss);
        arena.Step(arena.Config.ScanIntervalTicks);

        arena.TravelAlongX(player, toX: 20 * 32_768f);
        arena.TravelAlongX(player, toX: s_lair.X + 600f);

        arena.Boss.IsValid.Should().BeFalse("the lair kept its respawn timer while nobody's scan reached it");
        arena.Step(arena.Lairs.RespawnTicks);
        arena.Boss.IsValid.Should().BeTrue("the boss rises once the timer has run out");
    }

    [Fact]
    public void Constructor_NonBossMonster_IsRejected()
    {
        SpawningConfig config = CreatureTestData.LoadSpawning();
        config.BossLairs!.Monster = "abyss_eel";

        Action create = () => _ = new BossLairSystem(CreatureTestData.LoadCatalog(), config, new MonsterScaling(config), new FakeLairs(4096f), Vector2.Zero);

        create.Should().Throw<ArgumentException>().WithMessage("*abyss_eel*");
    }

    [Fact]
    public void Constructor_SpawnTableListingTheBoss_IsRejected()
    {
        SpawningConfig config = CreatureTestData.LoadSpawning();
        config.WaterSpawnTables = [new WaterSpawnTable { Tier = Maps.Hydrology.WaterDepthTier.Abyss, Entries = [new SpawnTableEntry { Monster = "abyss_leviathan", Weight = 1f }] }];

        Action create = () => _ = new MonsterSpawnerSystem(CreatureTestData.LoadCatalog(), config, new FixedBiomeSampler(Maps.Biomes.BiomeType.LushPlains), Vector2.Zero, TestGameplayKeys.Entropy(1UL));

        create.Should().Throw<ArgumentException>().WithMessage("*abyss_leviathan*");
    }

    /// <summary>One lair at <see cref="s_lair"/>, in cell (1, 0) of 32 768-unit cells.</summary>
    private sealed class FakeLairs(float chunkSize) : ILairSource
    {
        public float CellSize => 32_768f;

        public float ChunkSize => chunkSize;

        public int Lookups { get; private set; }

        public bool TryGetLair(int cellX, int cellY, out Vector2 anchor)
        {
            this.Lookups++;
            anchor = s_lair;
            return cellX == 1 && cellY == 0;
        }
    }

    private sealed class LairArena
    {
        private const double TickSeconds = 1.0 / 30.0;
        private readonly BossLairSystem _system;
        private ulong _tick = 1;

        public LairArena(float chunkSize = 1_024f)
        {
            this.World = new WorldSimulation(tickRate: 30);
            this.Source = new FakeLairs(chunkSize);
            _system = new BossLairSystem(this.Catalog, this.Config, new MonsterScaling(this.Config), this.Source, Vector2.Zero);
            this.World.RegisterSystem(_system);
        }

        public MonsterCatalog Catalog { get; } = CreatureTestData.LoadCatalog();

        public SpawningConfig Config { get; } = CreatureTestData.LoadSpawning();

        public BossLairConfig Lairs => this.Config.BossLairs!;

        public WorldSimulation World { get; }

        public Entity Boss => _system.GetBoss(1, 0);

        public BossLairSystem System => _system;

        public FakeLairs Source { get; }

        public Entity Player(Vector2 at)
        {
            Entity player = this.World.SpawnEntity(at, radius: 6f);
            this.World.Entities.SetComponent(player, default(SpawnAnchorComponent));
            return player;
        }

        public void Move(Entity entity, Vector2 to) => this.World.Entities.SetTransform(entity, new Transform2D(to));

        public void Kill(Entity boss)
        {
            HealthComponent hp = this.World.Entities.GetHealth(boss);
            this.World.Entities.SetHealth(boss, hp with { Current = 0f });
        }

        public void Wound(Entity boss, float fraction)
        {
            HealthComponent hp = this.World.Entities.GetHealth(boss);
            this.World.Entities.SetHealth(boss, hp with { Current = hp.Current - (hp.Max * fraction) });
        }

        public int BossCount()
        {
            int count = 0;
            ComponentPool<MonsterComponent> pool = this.World.Entities.GetPool<MonsterComponent>();
            for (int i = 0; i < pool.EntitySlots.Length; i++)
            {
                count += this.Catalog.GetDefinition(pool.AsSpan()[i].MonsterIndex).IsBoss ? 1 : 0;
            }

            return count;
        }

        /// <summary>Walks a player along X, half a cell per scan, so every cell on the way is scanned.</summary>
        public void TravelAlongX(Entity player, float toX)
        {
            Vector2 at = this.World.Entities.GetTransform(player).Position;
            float step = 16_384f * MathF.Sign(toX - at.X);
            while (MathF.Abs(toX - at.X) > MathF.Abs(step))
            {
                at += new Vector2(step, 0f);
                this.Move(player, at);
                this.Step(this.Config.ScanIntervalTicks);
            }

            this.Move(player, new Vector2(toX, at.Y));
            this.Step(this.Config.ScanIntervalTicks);
        }

        public void Step(uint ticks)
        {
            for (uint i = 0; i < ticks; i++)
            {
                this.World.ClearAllEvents();
                this.World.Tick(new FixedTickArgs(_tick, (float)TickSeconds, _tick * TickSeconds));
                _tick++;
            }
        }
    }
}
