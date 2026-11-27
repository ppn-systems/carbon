// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Events;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Creatures.Systems;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

/// <summary>ADR-032 D14: water creatures spawn in their depth tiers and never leave them.</summary>
public sealed class WaterCreatureTests
{
    private const float Tile = 32f;
    private const int Row = 8;

    // Level 25+ (outer frontier), so every water creature's level range is open.
    private static readonly Vector2 s_farSea = new(40_000f, 0f);

    [Theory]
    [InlineData(WaterDepthTier.Shore)]
    [InlineData(WaterDepthTier.Shallow)]
    [InlineData(WaterDepthTier.Medium)]
    [InlineData(WaterDepthTier.Deep)]
    [InlineData(WaterDepthTier.Abyss)]
    public void Update_WaterOfATier_SpawnsOnlyCreaturesLivingInThatTier(WaterDepthTier tier)
    {
        List<MonsterSpawnedEvent> spawns = RunSpawner(new FixedWaterSurface(tier), out MonsterCatalog catalog);

        spawns.Should().NotBeEmpty($"{tier} water has creatures");
        foreach (MonsterSpawnedEvent spawn in spawns)
        {
            MonsterDefinition def = catalog.GetDefinition(spawn.MonsterIndex);
            def.Habitat.Should().NotBeNull($"'{def.Id}' spawned in {tier} water, so it must be a water creature");
            def.Habitat!.Value.Contains(tier).Should().BeTrue($"'{def.Id}' does not live in {tier} water");
        }
    }

    [Fact]
    public void Update_Land_NeverSpawnsWaterCreatures()
    {
        List<MonsterSpawnedEvent> spawns = RunSpawner(new FixedBiomeSampler(BiomeType.BeachSand), out MonsterCatalog catalog);

        spawns.Should().NotBeEmpty();
        spawns.Should().OnlyContain(s => catalog.GetDefinition(s.MonsterIndex).Habitat == null, "a land spawn point never yields a water creature");
    }

    [Fact]
    public void Constructor_LandTableListingAWaterCreature_Throws()
    {
        SpawningConfig config = CreatureTestData.LoadSpawning();
        config.SpawnTables = [.. config.SpawnTables, new SpawnTable { Biome = BiomeType.ShallowWater, Entries = [new SpawnTableEntry { Monster = "shore_crab", Weight = 1f }] }];

        Action create = () => _ = new MonsterSpawnerSystem(CreatureTestData.LoadCatalog(), config, new FixedBiomeSampler(BiomeType.LushPlains), Vector2.Zero, TestGameplayKeys.Entropy(1UL));

        create.Should().Throw<ArgumentException>().WithMessage("*shore_crab*");
    }

    [Fact]
    public void Constructor_WaterTableListingACreatureOfOtherWater_Throws()
    {
        SpawningConfig config = CreatureTestData.LoadSpawning();
        config.WaterSpawnTables = [.. config.WaterSpawnTables.Where(t => t.Tier != WaterDepthTier.Abyss), new WaterSpawnTable { Tier = WaterDepthTier.Abyss, Entries = [new SpawnTableEntry { Monster = "shore_crab", Weight = 1f }] }];

        Action create = () => _ = new MonsterSpawnerSystem(CreatureTestData.LoadCatalog(), config, new FixedBiomeSampler(BiomeType.LushPlains), Vector2.Zero, TestGameplayKeys.Entropy(1UL));

        create.Should().Throw<ArgumentException>().WithMessage("*shore_crab*");
    }

    [Fact]
    public void Spawn_WaterCreature_HasAColliderBoundToItsHabitat()
    {
        WorldSimulation world = new(tickRate: 30);
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();
        Entity eel = Spawn(world, catalog, "abyss_eel", Vector2.Zero);
        Entity slime = Spawn(world, catalog, "wild_slime", Vector2.Zero);

        uint eelMask = world.Entities.GetCollider(eel).Mask;
        TerrainMask.IsWaterBound(eelMask).Should().BeTrue();
        TerrainMask.AllowsWaterTier(eelMask, (int)WaterDepthTier.Deep).Should().BeTrue();
        TerrainMask.AllowsWaterTier(eelMask, (int)WaterDepthTier.Abyss).Should().BeTrue();
        TerrainMask.AllowsWaterTier(eelMask, (int)WaterDepthTier.Medium).Should().BeFalse();
        TerrainMask.AllowsWaterTier(eelMask, (int)WaterDepthTier.Dry).Should().BeFalse();
        TerrainMask.IsWaterBound(world.Entities.GetCollider(slime).Mask).Should().BeFalse("land monsters keep walking");
    }

    [Theory]
    [InlineData("shore_crab", 7, 3)]
    [InlineData("abyss_eel", 11, 8)]
    public void Tick_ChasingAPlayerOutOfItsWater_ApproachesButNeverLeavesIt(string id, int spawnColumn, int playerColumn)
    {
        LakeArena arena = new();
        Entity creature = arena.SpawnMonster(id, Centre(spawnColumn));
        Entity player = arena.SpawnPlayer(Centre(playerColumn));
        WaterHabitat habitat = arena.Catalog.GetDefinition(arena.Catalog.GetIndex(id)).Habitat!.Value;
        float start = arena.Distance(creature, player);
        float closest = start;

        for (int t = 0; t < 360; t++)
        {
            arena.StepTick();
            WaterDepthTier under = arena.Map.GetWaterTierAt(arena.Position(creature));
            habitat.Contains(under).Should().BeTrue($"'{id}' left its water for {under} at tick {t} ({arena.Position(creature) / Tile})");
            closest = MathF.Min(closest, arena.Distance(creature, player));
        }

        closest.Should().BeLessThan(start - Tile, $"'{id}' chases the player up to the edge of its water");
    }

    [Theory]
    [InlineData("abyss_eel", 11, 9)]
    [InlineData("abyss_leviathan", 12, 11)]
    public void Tick_PlayerDivingNearby_IsSeenThroughTheDeepWaterAndAttacked(string id, int spawnColumn, int playerColumn)
    {
        // Deep water is solid to the terrain raycast; a creature of the deep must still see a diver in it.
        LakeArena arena = new();
        Entity creature = arena.SpawnMonster(id, Centre(spawnColumn));
        Entity player = arena.SpawnPlayer(Centre(playerColumn));
        float full = arena.World.Entities.GetHealth(player).Current;
        bool targeted = false;

        for (int t = 0; t < 240 && arena.World.Entities.GetHealth(player).Current >= full; t++)
        {
            arena.StepTick();
            targeted |= arena.World.Entities.GetComponent<AiBrainComponent>(creature).TargetEntity == player;
        }

        targeted.Should().BeTrue($"'{id}' must notice a diver {spawnColumn - playerColumn} tiles away");
        arena.World.Entities.GetHealth(player).Current.Should().BeLessThan(full, $"'{id}' attacks the diver");
    }

    private static List<MonsterSpawnedEvent> RunSpawner(ISpawnSurface surface, out MonsterCatalog catalog)
    {
        catalog = CreatureTestData.LoadCatalog();
        WorldSimulation world = new(tickRate: 30);
        MonsterSpawnerSystem spawner = new(catalog, CreatureTestData.LoadSpawning(), surface, Vector2.Zero, TestGameplayKeys.Entropy(3UL));
        world.RegisterSystem(spawner);
        Entity anchor = world.SpawnEntity(s_farSea, radius: 6f);
        world.Entities.SetComponent(anchor, default(SpawnAnchorComponent));

        List<MonsterSpawnedEvent> spawns = [];
        for (int i = 0; i < 2000; i++)
        {
            _ = world.Update(1.0 / 30.0);
            foreach (MonsterSpawnedEvent e in world.ReadEvents<MonsterSpawnedEvent>())
            {
                spawns.Add(e);
            }
        }

        return spawns;
    }

    private static Entity Spawn(WorldSimulation world, MonsterCatalog catalog, string id, Vector2 position)
    {
        SpawningConfig config = CreatureTestData.LoadSpawning();
        ushort index = catalog.GetIndex(id);
        ScaledMonsterStats stats = new MonsterScaling(config).Compute(0f, isNight: false, catalog.GetDefinition(index));
        return MonsterFactory.Spawn(world, catalog, index, position, in stats, config.MonsterLayer, config.TargetLayerMask);
    }

    private static Vector2 Centre(int tx) => new((tx + 0.5f) * Tile, (Row + 0.5f) * Tile);

    /// <summary>Open water of one tier everywhere.</summary>
    private sealed class FixedWaterSurface(WaterDepthTier tier) : ISpawnSurface
    {
        public bool TrySampleSpawnBiome(Vector2 worldPosition, out BiomeType biome)
        {
            biome = WaterDepth.BiomeFor(tier);
            return false;
        }

        public bool TrySampleSpawnWater(Vector2 worldPosition, out WaterDepthTier sampled)
        {
            sampled = tier;
            return true;
        }
    }

    /// <summary>
    /// A painted lake in chunk (0, 0), rows 4..12: land at x &lt;= 5, then Shore (6), Shallow (7), Medium (8), Deep
    /// (9, 10) and Abyss (11, 12), then land. Creatures run the real AI, brain and movement against it.
    /// </summary>
    private sealed class LakeArena
    {
        private const double TickSeconds = 1.0 / 30.0;
        private readonly SpawningConfig _config = CreatureTestData.LoadSpawning();
        private readonly CreatureSystems _systems;
        private ulong _tick;

        public LakeArena()
        {
            this.Map = CreateLake();
            this.World = new WorldSimulation(tickRate: 30) { WorldPhysics = this.Map };
            _systems = CreatureSystems.Register(
                this.World,
                this.Catalog,
                _config,
                new FixedBiomeSampler(BiomeType.LushPlains),
                new Vector2(-1_000_000f, 0f),
                entropy: TestGameplayKeys.Entropy(5UL),
                ai: new AiBehaviorSystem(this.Map.NavGrid, Tile));
        }

        public MonsterCatalog Catalog { get; } = CreatureTestData.LoadCatalog();

        public ChunkWorld2D Map { get; }

        public WorldSimulation World { get; }

        public Entity SpawnPlayer(Vector2 position)
        {
            Entity player = this.World.SpawnEntity(position, radius: 6f);
            this.World.Entities.SetCollider(player, Collider2D.CreateCircle(6f) with { Layer = _config.TargetLayerMask });
            this.World.Entities.SetHealth(player, new HealthComponent(1_000_000f, 1_000_000f));
            return player;
        }

        public Entity SpawnMonster(string id, Vector2 position)
        {
            ushort index = this.Catalog.GetIndex(id);
            ScaledMonsterStats stats = _systems.Spawner.Scaling.Compute(0f, isNight: false, this.Catalog.GetDefinition(index));
            return MonsterFactory.Spawn(this.World, this.Catalog, index, position, in stats, _config.MonsterLayer, _config.TargetLayerMask);
        }

        public void StepTick()
        {
            this.World.ClearAllEvents();
            this.World.Tick(new FixedTickArgs(_tick, (float)TickSeconds, _tick * TickSeconds));
            _tick++;
        }

        public Vector2 Position(Entity entity) => this.World.Entities.GetTransform(entity).Position;

        public float Distance(Entity a, Entity b) => Vector2.Distance(this.Position(a), this.Position(b));

        private static ChunkWorld2D CreateLake()
        {
            (int X, float Depth)[] columns = [(6, 0.010f), (7, 0.030f), (8, 0.070f), (9, 0.150f), (10, 0.150f), (11, 0.250f), (12, 0.250f)];
            ChunkWorld2D world = new(new ProceduralWorldGenerator(99UL), tileSize: Tile);
            world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
            Chunk2D chunk = world.GetOrCreateChunk(0, 0);
            chunk.ClearProps();
            for (int y = 0; y < Chunk2D.Size; y++)
            {
                for (int x = 0; x < Chunk2D.Size; x++)
                {
                    chunk.SetTile(x, y, new MapTile(BiomeType.LushPlains, 0.5f, 0.5f, 0.5f, IsSolid: false, Variant: 0));
                }
            }

            for (int y = 4; y <= 12; y++)
            {
                foreach ((int x, float depth) in columns)
                {
                    BiomeType biome = WaterDepth.BiomeFor(WaterDepth.TierOf(depth));
                    chunk.SetTile(x, y, new MapTile(biome, WaterDepth.ToElevation(depth), 0.5f, 0.85f, BiomeProperties.Get(biome).IsSolid, Variant: 0));
                }
            }

            return world;
        }
    }
}
