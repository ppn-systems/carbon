// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Primitives.Random;
using Carbon.Simulation.ECS;

namespace Carbon.Survival.Food;

/// <summary>
/// Stateless deterministic roll for food poisoning: a keyed PRF (ADR-026) of the server's secret roll key, the eater, the
/// tick and the food, so server replays and tests with the same key are reproducible, no RNG state has to be persisted,
/// and a client that knows the world seed and the tick cannot predict which meal poisons.
/// </summary>
public static class FoodPoisoningRoll
{
    /// <summary>
    /// Returns a uniform sample in [0, 1) for one meal.
    /// </summary>
    /// <param name="key">Secret roll key of the tick's epoch (server-only, never the world seed).</param>
    /// <param name="eater">Eating entity.</param>
    /// <param name="tickIndex">Tick on which the meal completes.</param>
    /// <param name="foodIndex">Food index in <see cref="FoodCatalog"/>.</param>
    /// <returns>A value in [0, 1); the meal poisons when it is below the food's poison chance.</returns>
    public static double Sample(in GameplayKey key, Entity eater, ulong tickIndex, int foodIndex) =>
        key.Sample(GameplayDomain.FoodPoisoning, ((ulong)(uint)eater.Generation << 32) | (uint)eater.Id, tickIndex, (uint)foodIndex);
}
