// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using Carbon.Survival.Systems;

namespace Carbon.Survival.Tests;

/// <summary>
/// Deterministic tick-stepping harness: a 30 Hz world with the needs and eating systems registered.
/// </summary>
internal sealed class SurvivalTestWorld
{
    public const int TickRate = 30;

    private ulong _tickIndex;
    private double _totalTime;

    public SurvivalTestWorld(NeedsConfig? config = null, FoodCatalog? catalog = null, ulong seed = 42)
    {
        this.Config = config ?? new NeedsConfig();
        this.Catalog = catalog ?? CreateCatalog(this.Config);
        this.World = new WorldSimulation(TickRate);
        this.Eating = new EatActionSystem(this.Catalog, this.Config, seed);
        this.World.RegisterSystem(this.Eating);
        this.World.RegisterSystem(new NeedsSystem(this.Config));
    }

    public NeedsConfig Config { get; }

    public FoodCatalog Catalog { get; }

    public WorldSimulation World { get; }

    public EatActionSystem Eating { get; }

    public static FoodCatalog CreateCatalog(NeedsConfig config, float rawPoisonChance = 0f) => new(
        [
            new FoodDefinition { ItemId = "raw_meat", Nutrition = 25f, Quality = FoodQuality.Raw, PoisonChance = rawPoisonChance, MoodOffset = -7f, MoodDurationSeconds = 600f, EatSeconds = 4f },
            new FoodDefinition { ItemId = "bread", Nutrition = 35f, Quality = FoodQuality.Simple, PoisonChance = 0f, MoodOffset = 0f, MoodDurationSeconds = 0f, EatSeconds = 3f },
            new FoodDefinition { ItemId = "stew", Nutrition = 60f, Quality = FoodQuality.Fine, PoisonChance = 0f, MoodOffset = 5f, MoodDurationSeconds = 900f, EatSeconds = 6f },
        ],
        config.FoodMax);

    public static string FindConfigFile(string fileName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "resources", "config", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"resources/config/{fileName} not found above {AppContext.BaseDirectory}");
    }

    public Entity SpawnPlayer(float food = -1f, float health = 100f)
    {
        Entity entity = this.World.SpawnEntity(Vector2.Zero);
        NeedsComponent needs = NeedsComponent.CreateFull(this.Config);
        if (food >= 0f)
        {
            needs.Food = food;
            needs.Hunger = this.Config.ClassifyHunger(needs.FoodFraction);
        }

        this.World.Entities.SetComponent(entity, needs);
        this.World.Entities.SetHealth(entity, new HealthComponent(health, 100f));
        return entity;
    }

    public ref NeedsComponent Needs(Entity entity) => ref this.World.Entities.GetComponent<NeedsComponent>(entity);

    public ref HealthComponent Health(Entity entity) => ref this.World.Entities.GetHealth(entity);

    /// <summary>Steps whole seconds with 1-second ticks (fast for multi-minute decay timelines).</summary>
    public void StepSeconds(int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            this.Step(1f);
        }
    }

    /// <summary>Steps fixed 30 Hz ticks, as the real server does.</summary>
    public void StepTicks(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            this.Step(1f / TickRate);
        }
    }

    private void Step(float dt)
    {
        _tickIndex++;
        _totalTime += dt;
        this.World.Tick(new FixedTickArgs(_tickIndex, dt, _totalTime));
    }
}
