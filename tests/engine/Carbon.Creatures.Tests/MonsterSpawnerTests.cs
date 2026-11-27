// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Events;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Systems;
using Carbon.Maps.Biomes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Spatial;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

public sealed class MonsterSpawnerTests
{
    private static readonly Vector2 s_frontier = new(6000f, 0f);

    [Fact]
    public void Update_AnchorAtWorldSpawn_NeverSpawnsInsideSafeZone()
    {
        SpawnRun run = new(seed: 11UL, anchor: new Vector2(900f, 0f));

        run.Step(3000);

        float safe = run.Config.SafeZoneRadius;
        run.Spawns.Should().OnlyContain(s => s.Position.Length() >= safe, "no monster may appear inside the spawn safe zone");
        run.Spawns.Should().NotBeEmpty("cells partially outside the safe zone still populate");
    }

    [Fact]
    public void Update_FirstScanWithAnchorAtWorldSpawn_PopulatesHeartlandImmediately()
    {
        SpawnRun run = new(seed: 11UL, anchor: Vector2.Zero);

        run.Spawner.RequestScan();
        run.Step(1);

        run.Spawns.Should().HaveCountGreaterThanOrEqualTo(3, "rejected candidates retry within the scan instead of waiting a spawn interval");
        run.Spawns.Should().OnlyContain(s => s.Position.Length() >= run.Config.SafeZoneRadius);
    }

    [Fact]
    public void Update_SameSeed_ProducesIdenticalSpawns()
    {
        SpawnRun a = new(seed: 42UL, anchor: s_frontier);
        SpawnRun b = new(seed: 42UL, anchor: s_frontier);
        SpawnRun c = new(seed: 43UL, anchor: s_frontier);

        a.Step(1200);
        b.Step(1200);
        c.Step(1200);

        a.Spawns.Should().NotBeEmpty();
        a.Spawns.Should().Equal(b.Spawns, "spawning is keyed by (key, cell, attempt, tick)");
        c.Spawns.Should().NotEqual(a.Spawns, "a different key yields different spawns");
    }

    [Fact]
    public void Update_LongRun_RespectsPerCellCapAndScalesLevelWithDistance()
    {
        SpawnRun run = new(seed: 5UL, anchor: s_frontier);

        run.Step(4000);

        int cap = run.Spawner.GetCellCap();
        Dictionary<CellCoord, int> perCell = run.CountLiveMonstersPerCell();
        perCell.Values.Should().OnlyContain(n => n <= cap, $"density cap is {cap} per cell");
        foreach (MonsterSpawnedEvent spawn in run.Spawns)
        {
            int expected = run.Spawner.Scaling.Compute(spawn.Position.Length(), false, run.Catalog.GetDefinition(spawn.MonsterIndex)).Level;
            spawn.Level.Should().Be(expected, "level follows distance from the world origin");
            run.Catalog.GetDefinition(spawn.MonsterIndex).IsSpawnableAt(spawn.Level).Should().BeTrue("monsters only spawn inside their level range");
        }
    }

    [Fact]
    public void Update_Night_SpawnsDenserStrongerAndMoreElites()
    {
        SpawnRun day = new(seed: 9UL, anchor: s_frontier, biome: BiomeType.BeachSand);
        SpawnRun night = new(seed: 9UL, anchor: s_frontier, biome: BiomeType.BeachSand, hour: 23f);

        day.Step(6000);
        night.Step(6000);

        night.Spawner.GetCellCap().Should().BeGreaterThan(day.Spawner.GetCellCap());
        night.Spawner.LiveMonsterCount.Should().BeGreaterThan(day.Spawner.LiveMonsterCount, "night raises density caps");
        night.Spawns.Should().OnlyContain(s => s.IsNight);
        night.Spawns.FindAll(s => s.IsElite).Count.Should().BeGreaterThan(day.Spawns.FindAll(s => s.IsElite).Count, "night raises elite chance");
    }

    [Fact]
    public void Update_MonstersKilled_CellWaitsForRespawnDelay()
    {
        SpawnRun run = new(seed: 3UL, anchor: s_frontier);
        run.Step(2000);
        run.Spawns.Should().NotBeEmpty();

        HashSet<CellCoord> emptied = [];
        ComponentPool<MonsterComponent> pool = run.World.Entities.GetPool<MonsterComponent>();
        for (int i = 0; i < pool.EntitySlots.Length; i++)
        {
            Entity monster = run.World.Entities.GetEntity(pool.EntitySlots[i]);
            run.World.Entities.SetHealth(monster, new HealthComponent(0f, 1f));
            emptied.Add(pool.AsSpan()[i].SpawnCell);
        }

        run.Spawns.Clear();
        run.Step((int)run.Config.RespawnDelayTicks - (int)run.Config.ScanIntervalTicks);
        run.Spawns.Should().NotContain(s => emptied.Contains(run.Spawner.GetCell(s.Position)), "cells wait the respawn delay after a death");

        run.Step(600);
        run.Spawns.Should().Contain(s => emptied.Contains(run.Spawner.GetCell(s.Position)), "cells refill after the respawn delay");
    }

    [Fact]
    public void Update_DawnLowersTheCellCap_NeverDespawnsOrRecyclesLiveMonstersAroundThePlayer()
    {
        SpawnRun run = new(seed: 5UL, anchor: s_frontier, hour: 23f);
        run.Step(6000);
        List<Entity> nightMonsters = run.LiveMonsters();
        Dictionary<CellCoord, int> nightCounts = run.CountLiveMonstersPerCell();
        nightCounts.Values.Should().Contain(c => c > run.Config.BaseCellCap, "night fills cells above the day cap");

        // Dawn: the cap drops back to the base cap while the player is still standing among the night monsters.
        run.Spawner.TimeOfDayHours = 5.5f;
        run.Spawner.IsNight.Should().BeFalse();
        run.Spawns.Clear();
        run.Step(3000);

        nightMonsters.Should().OnlyContain(m => run.World.Entities.IsAlive(m), "dawn must not despawn or recycle live monsters");
        run.Spawner.LiveMonsterCount.Should().BeGreaterThanOrEqualTo(nightMonsters.Count);
        Dictionary<CellCoord, int> dayCounts = run.CountLiveMonstersPerCell();
        foreach ((CellCoord cell, int count) in nightCounts)
        {
            dayCounts.GetValueOrDefault(cell).Should().BeGreaterThanOrEqualTo(count, $"cell {cell} keeps its night monsters");
        }
    }

    private sealed class SpawnRun
    {
        public SpawnRun(ulong seed, Vector2 anchor, BiomeType biome = BiomeType.LushPlains, float hour = 12f)
        {
            this.World = new WorldSimulation(tickRate: 30);
            this.Spawner = new MonsterSpawnerSystem(this.Catalog, this.Config, new FixedBiomeSampler(biome), Vector2.Zero, TestGameplayKeys.Entropy(seed))
            {
                TimeOfDayHours = hour
            };
            this.World.RegisterSystem(this.Spawner);

            // Anchor is not on the monsters' target layer, so spawned monsters stay put (pure spawner test).
            Entity player = this.World.SpawnEntity(anchor, radius: 6f);
            this.World.Entities.SetComponent(player, default(SpawnAnchorComponent));
        }

        public MonsterCatalog Catalog { get; } = CreatureTestData.LoadCatalog();

        public SpawningConfig Config { get; } = CreatureTestData.LoadSpawning();

        public WorldSimulation World { get; }

        public MonsterSpawnerSystem Spawner { get; }

        public List<MonsterSpawnedEvent> Spawns { get; } = [];

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                _ = this.World.Update(1.0 / 30.0);
                foreach (MonsterSpawnedEvent e in this.World.ReadEvents<MonsterSpawnedEvent>())
                {
                    this.Spawns.Add(e with { Monster = Entity.Null });
                }
            }
        }

        public List<Entity> LiveMonsters()
        {
            List<Entity> monsters = [];
            ComponentPool<MonsterComponent> pool = this.World.Entities.GetPool<MonsterComponent>();
            for (int i = 0; i < pool.EntitySlots.Length; i++)
            {
                monsters.Add(this.World.Entities.GetEntity(pool.EntitySlots[i]));
            }

            return monsters;
        }

        public Dictionary<CellCoord, int> CountLiveMonstersPerCell()
        {
            Dictionary<CellCoord, int> counts = [];
            ComponentPool<MonsterComponent> pool = this.World.Entities.GetPool<MonsterComponent>();
            for (int i = 0; i < pool.EntitySlots.Length; i++)
            {
                CellCoord cell = pool.AsSpan()[i].SpawnCell;
                counts[cell] = counts.GetValueOrDefault(cell) + 1;
            }

            return counts;
        }
    }
}
