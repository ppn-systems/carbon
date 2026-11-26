// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Events;
using Carbon.Survival.Food;

namespace Carbon.Survival.Systems;

/// <summary>
/// Server-authoritative timed eating. <see cref="BeginEat"/> validates and starts a meal; each tick the meal
/// counts down and is interrupted if the eater's health drops (any damage source except starvation).
/// On completion it applies nutrition (capped), rolls deterministic food poisoning, adds the food's mood
/// memory and publishes <see cref="MealCompletedEvent"/>; the caller consumes the inventory item on that event.
/// </summary>
public sealed class EatActionSystem : ISimulationSystem
{
    private readonly FoodCatalog _catalog;
    private readonly NeedsConfig _config;
    private readonly ulong _poisonSeed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EatActionSystem"/> class.
    /// </summary>
    /// <param name="catalog">Food catalog from <c>food.json</c>.</param>
    /// <param name="config">Needs tuning from <c>needs.json</c>.</param>
    /// <param name="poisonSeed">World seed for the deterministic poisoning roll.</param>
    public EatActionSystem(FoodCatalog catalog, NeedsConfig config, ulong poisonSeed)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        _catalog = catalog;
        _config = config;
        _poisonSeed = poisonSeed;
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PostUpdate;

    /// <summary>
    /// Gets a value indicating whether <paramref name="entity"/> is currently eating.
    /// </summary>
    /// <param name="world">World simulation.</param>
    /// <param name="entity">Entity to check.</param>
    /// <returns><see langword="true"/> while a meal is in progress.</returns>
    public static bool IsEating(WorldSimulation world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Entities.HasComponent<EatingComponent>(entity);
    }

    /// <summary>
    /// Starts eating <paramref name="foodItemId"/>. The caller must already have verified that the eater owns the item.
    /// </summary>
    /// <param name="world">World simulation (call on the tick thread).</param>
    /// <param name="eater">Eating entity.</param>
    /// <param name="foodItemId">Item id from <c>items.json</c>.</param>
    /// <returns>
    /// <see cref="OpStatus.Ok"/> when the meal started; <see cref="OpStatus.Invalid"/> for an empty id or an entity
    /// without needs; <see cref="OpStatus.NotFound"/> for a dead handle or inedible item;
    /// <see cref="OpStatus.Forbidden"/> when the eater is dead; <see cref="OpStatus.Conflict"/> when already eating
    /// or too full to eat.
    /// </returns>
    public OpStatus BeginEat(WorldSimulation world, Entity eater, string foodItemId)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (string.IsNullOrEmpty(foodItemId))
        {
            return OpStatus.Invalid;
        }

        EntityManager entities = world.Entities;
        if (!entities.IsAlive(eater))
        {
            return OpStatus.NotFound;
        }

        if (!entities.TryGetComponent(eater, out NeedsComponent needs))
        {
            return OpStatus.Invalid;
        }

        bool hasHealth = entities.HasHealth(eater);
        if (hasHealth && entities.GetHealth(eater).IsDead)
        {
            return OpStatus.Forbidden;
        }

        if (!_catalog.TryGetIndex(foodItemId, out int foodIndex))
        {
            return OpStatus.NotFound;
        }

        if (entities.HasComponent<EatingComponent>(eater) || needs.FoodFraction >= _config.FullThreshold)
        {
            return OpStatus.Conflict;
        }

        FoodDefinition food = _catalog.Get(foodIndex);
        int ticks = Math.Max(1, (int)MathF.Ceiling((food.EatSeconds * world.Clock.TickRate) - 0.001f));
        float health = hasHealth ? entities.GetHealth(eater).Current : 0f;
        entities.SetComponent(eater, new EatingComponent(foodIndex, ticks, ticks, health));
        return OpStatus.Ok;
    }

    /// <summary>
    /// Cancels the eater's meal without applying nutrition and publishes <see cref="MealInterruptedEvent"/>.
    /// </summary>
    /// <param name="world">World simulation.</param>
    /// <param name="eater">Eating entity.</param>
    /// <param name="reason">Reason reported in the event (e.g. <see cref="MealInterruptReason.Damaged"/> when the
    /// host applies damage outside the simulation tick).</param>
    /// <returns><see cref="OpStatus.Ok"/> when a meal was cancelled; <see cref="OpStatus.NotFound"/> when not eating.</returns>
    public static OpStatus CancelEat(WorldSimulation world, Entity eater, MealInterruptReason reason = MealInterruptReason.Cancelled)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!world.Entities.TryGetComponent(eater, out EatingComponent meal))
        {
            return OpStatus.NotFound;
        }

        _ = world.Entities.RemoveComponent<EatingComponent>(eater);
        world.PublishEvent(new MealInterruptedEvent(eater, meal.FoodIndex, reason));
        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        ComponentPool<EatingComponent> pool = entities.GetPool<EatingComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        Span<EatingComponent> values = pool.AsSpan();

        // Iterate backwards: swap-back removal only moves already-visited entries.
        for (int i = values.Length - 1; i >= 0; i--)
        {
            int slot = slots[i];
            Entity entity = entities.GetEntity(slot);
            EatingComponent meal = values[i];

            bool hasHealth = entities.HasHealth(entity);
            float health = hasHealth ? entities.GetHealth(entity).Current : 0f;
            if (!entities.HasComponent<NeedsComponent>(entity) || (hasHealth && health <= 0f))
            {
                _ = pool.Remove(slot);
                world.PublishEvent(new MealInterruptedEvent(entity, meal.FoodIndex, MealInterruptReason.Died));
                continue;
            }

            if (hasHealth && health < meal.LastHealth)
            {
                _ = pool.Remove(slot);
                world.PublishEvent(new MealInterruptedEvent(entity, meal.FoodIndex, MealInterruptReason.Damaged));
                continue;
            }

            int remaining = meal.RemainingTicks - 1;
            if (remaining > 0)
            {
                values[i] = meal with { RemainingTicks = remaining, LastHealth = health };
                continue;
            }

            _ = pool.Remove(slot);
            this.Complete(world, entity, meal.FoodIndex, tick.TickIndex);
        }
    }

    private static MoodThought ThoughtFor(FoodQuality quality) => quality switch
    {
        FoodQuality.Raw => MoodThought.AteRawFood,
        FoodQuality.Simple => MoodThought.AteSimpleMeal,
        FoodQuality.Fine => MoodThought.AteFineMeal,
        FoodQuality.Lavish => MoodThought.AteLavishMeal,
        _ => MoodThought.None,
    };

    private void Complete(WorldSimulation world, Entity entity, int foodIndex, ulong tickIndex)
    {
        FoodDefinition food = _catalog.Get(foodIndex);
        ref NeedsComponent needs = ref world.Entities.GetComponent<NeedsComponent>(entity);

        float before = needs.Food;
        needs.Food = MathF.Min(needs.FoodMax, needs.Food + food.Nutrition);
        float gained = needs.Food - before;

        bool poisoned = food.PoisonChance > 0f
            && FoodPoisoningRoll.Sample(_poisonSeed, entity, tickIndex, foodIndex) < food.PoisonChance;
        if (poisoned)
        {
            needs.PoisonRemainingSeconds = MathF.Max(needs.PoisonRemainingSeconds, _config.PoisonDurationSeconds);
        }

        if (food.MoodOffset != 0f && food.MoodDurationSeconds > 0f)
        {
            needs.AddMemory(new MoodMemory(ThoughtFor(food.Quality), food.MoodOffset, food.MoodDurationSeconds));
        }

        world.PublishEvent(new MealCompletedEvent(entity, foodIndex, gained, poisoned));
    }
}
