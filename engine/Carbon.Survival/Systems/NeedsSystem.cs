// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Combat.Events;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Events;

namespace Carbon.Survival.Systems;

/// <summary>
/// Drains food and rest every fixed tick, applies RimWorld-style threshold effects (mood thoughts, speed
/// penalties, regen block, malnutrition and starvation damage), and publishes <see cref="NeedStateChangedEvent"/>
/// on threshold crossings. Starvation damage is published as <see cref="DamageEvent"/> /
/// <see cref="EntityKilledEvent"/> with the entity as its own attacker. Zero heap allocations per tick.
/// </summary>
public sealed class NeedsSystem : ISimulationSystem
{
    private const float SecondsPerMinute = 60f;

    private readonly NeedsConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="NeedsSystem"/> class.
    /// </summary>
    /// <param name="config">Validated needs tuning.</param>
    public NeedsSystem(NeedsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PostUpdate;

    /// <summary>
    /// Sets the activity used for the next drain step. Call from the simulation whenever the entity's movement
    /// or combat state changes.
    /// </summary>
    /// <param name="world">World simulation.</param>
    /// <param name="entity">Entity with a <see cref="NeedsComponent"/>.</param>
    /// <param name="activity">Current activity.</param>
    /// <returns><see langword="true"/> when the entity has needs.</returns>
    public static bool SetActivity(WorldSimulation world, Entity entity, NeedsActivity activity)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!world.Entities.HasComponent<NeedsComponent>(entity))
        {
            return false;
        }

        world.Entities.GetComponent<NeedsComponent>(entity).Activity = activity;
        return true;
    }

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        ComponentPool<NeedsComponent> pool = entities.GetPool<NeedsComponent>();
        ComponentPool<EatingComponent> meals = entities.GetPool<EatingComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        Span<NeedsComponent> values = pool.AsSpan();
        float dt = tick.DeltaTime;
        float minutes = dt / SecondsPerMinute;

        for (int i = 0; i < values.Length; i++)
        {
            Entity entity = entities.GetEntity(slots[i]);
            bool hasHealth = entities.HasHealth(entity);
            if (!entity.IsValid || (hasHealth && entities.GetHealth(entity).IsDead))
            {
                continue;
            }

            ref NeedsComponent needs = ref values[i];
            HungerState previousHunger = needs.Hunger;
            RestState previousRest = needs.Fatigue;
            MoodState previousMood = needs.MoodBand;

            this.Drain(ref needs, minutes);
            needs.Hunger = _config.ClassifyHunger(needs.FoodFraction);
            needs.Fatigue = _config.ClassifyRest(needs.RestFraction);

            if (hasHealth)
            {
                this.ApplyMalnutrition(world, entity, ref needs, dt, minutes, meals);
            }

            needs.PoisonRemainingSeconds = MathF.Max(0f, needs.PoisonRemainingSeconds - dt);
            needs.Mood = this.ComputeMood(ref needs, dt);
            needs.MoodBand = _config.ClassifyMood(needs.Mood);
            this.ApplyEffects(ref needs);

            if (needs.Hunger != previousHunger)
            {
                world.PublishEvent(new NeedStateChangedEvent(entity, NeedKind.Food, (byte)previousHunger, (byte)needs.Hunger, needs.FoodFraction));
            }

            if (needs.Fatigue != previousRest)
            {
                world.PublishEvent(new NeedStateChangedEvent(entity, NeedKind.Rest, (byte)previousRest, (byte)needs.Fatigue, needs.RestFraction));
            }

            if (needs.MoodBand != previousMood)
            {
                world.PublishEvent(new NeedStateChangedEvent(entity, NeedKind.Mood, (byte)previousMood, (byte)needs.MoodBand, needs.Mood));
            }
        }
    }

    private static bool DealStarvationDamage(
        WorldSimulation world,
        Entity entity,
        float amount,
        ComponentPool<EatingComponent> meals)
    {
        ref HealthComponent health = ref world.Entities.GetHealth(entity);
        float dealt = MathF.Min(health.Current, amount);
        if (dealt <= 0f)
        {
            return health.IsDead;
        }

        health = new HealthComponent(health.Current - dealt, health.Max);
        bool dead = health.IsDead;
        float current = health.Current;
        world.MarkDirty(entity, EntityDirtyMask.Health);

        // Starvation must not interrupt the meal that would cure it: advance the eater's damage baseline.
        if (meals.Has(entity.Id))
        {
            ref EatingComponent meal = ref meals.Get(entity.Id);
            meal = meal with { LastHealth = current };
        }

        world.PublishEvent(new DamageEvent(entity, entity, dealt, IsCritical: false));
        if (dead)
        {
            world.PublishEvent(new EntityKilledEvent(entity, entity));
        }

        return dead;
    }

    private void Drain(ref NeedsComponent needs, float minutes)
    {
        float foodDrain = _config.FoodDecayPerMinute * _config.FoodActivity.For(needs.Activity) * minutes;
        needs.Food = Math.Clamp(needs.Food - foodDrain, 0f, needs.FoodMax);

        if (!_config.RestEnabled)
        {
            needs.Rest = needs.RestMax;
            return;
        }

        float restDelta = needs.Activity == NeedsActivity.Resting
            ? _config.RestRecoveryPerMinute * minutes
            : -_config.RestDecayPerMinute * _config.RestActivity.For(needs.Activity) * minutes;
        needs.Rest = Math.Clamp(needs.Rest + restDelta, 0f, needs.RestMax);
    }

    private void ApplyMalnutrition(
        WorldSimulation world,
        Entity entity,
        ref NeedsComponent needs,
        float dt,
        float minutes,
        ComponentPool<EatingComponent> meals)
    {
        if (needs.Hunger != HungerState.Starving)
        {
            needs.Malnutrition = MathF.Max(0f, needs.Malnutrition - (_config.MalnutritionRecoveryPerMinute * minutes));
            needs.StarvationTimer = 0f;
            return;
        }

        needs.Malnutrition = MathF.Min(1f, needs.Malnutrition + (_config.MalnutritionGainPerMinute * minutes));
        if (needs.Malnutrition >= 1f)
        {
            // Maximum malnutrition is lethal regardless of remaining health.
            _ = DealStarvationDamage(world, entity, world.Entities.GetHealth(entity).Current, meals);
            return;
        }

        needs.StarvationTimer += dt;
        while (needs.StarvationTimer >= _config.StarvationDamageIntervalSeconds)
        {
            needs.StarvationTimer -= _config.StarvationDamageIntervalSeconds;
            float damage = _config.StarvationDamageBase + (_config.StarvationDamagePerSeverity * needs.Malnutrition);
            if (DealStarvationDamage(world, entity, damage, meals))
            {
                return;
            }
        }
    }

    private float ComputeMood(ref NeedsComponent needs, float dt)
    {
        float mood = _config.MoodBaseline;
        mood += needs.Hunger switch
        {
            HungerState.Hungry => _config.HungryMood,
            HungerState.UrgentlyHungry => _config.UrgentlyHungryMood,
            HungerState.Starving => _config.StarvingMood,
            HungerState.Fed => 0f,
            _ => 0f,
        };
        mood += needs.Fatigue switch
        {
            RestState.Tired => _config.TiredMood,
            RestState.VeryTired => _config.VeryTiredMood,
            RestState.Exhausted => _config.ExhaustedMood,
            RestState.Rested => 0f,
            _ => 0f,
        };

        if (needs.IsPoisoned)
        {
            mood += _config.PoisonMood;
        }

        for (int m = 0; m < NeedsComponent.MoodMemoryCapacity; m++)
        {
            MoodMemory memory = needs.Memories[m];
            if (!memory.IsActive)
            {
                continue;
            }

            float remaining = memory.RemainingSeconds - dt;
            if (remaining <= 0f)
            {
                needs.Memories[m] = default;
                continue;
            }

            needs.Memories[m] = memory with { RemainingSeconds = remaining };
            mood += memory.Offset;
        }

        return Math.Clamp(mood, 0f, 100f);
    }

    private void ApplyEffects(ref NeedsComponent needs)
    {
        float scale = needs.Hunger switch
        {
            HungerState.UrgentlyHungry => _config.UrgentlyHungrySpeedScale,
            HungerState.Starving => _config.StarvingSpeedScale,
            HungerState.Fed => 1f,
            HungerState.Hungry => 1f,
            _ => 1f,
        };

        if (needs.Fatigue == RestState.Exhausted)
        {
            scale *= _config.ExhaustedSpeedScale;
        }

        if (needs.IsPoisoned)
        {
            scale *= _config.PoisonSpeedScale;
        }

        needs.MoveSpeedScale = scale;
        needs.AttackSpeedScale = scale;
        needs.HealthRegenBlocked = needs.Hunger >= HungerState.UrgentlyHungry || needs.IsPoisoned;
    }
}
