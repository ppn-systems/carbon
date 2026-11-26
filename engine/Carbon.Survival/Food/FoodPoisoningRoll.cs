// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Survival.Food;

/// <summary>
/// Stateless deterministic roll for food poisoning. The same seed, eater, tick and food always give the same
/// result, so server replays and tests are reproducible and no RNG state has to be persisted.
/// </summary>
public static class FoodPoisoningRoll
{
    /// <summary>
    /// Returns a uniform sample in [0, 1) for one meal.
    /// </summary>
    /// <param name="seed">World or shard seed.</param>
    /// <param name="eater">Eating entity.</param>
    /// <param name="tickIndex">Tick on which the meal completes.</param>
    /// <param name="foodIndex">Food index in <see cref="FoodCatalog"/>.</param>
    /// <returns>A value in [0, 1); the meal poisons when it is below the food's poison chance.</returns>
    public static double Sample(ulong seed, Entity eater, ulong tickIndex, int foodIndex)
    {
        ulong x = Mix(seed ^ (uint)eater.Id);
        x = Mix(x ^ ((ulong)(uint)eater.Generation << 32));
        x = Mix(x ^ tickIndex);
        x = Mix(x ^ (uint)foodIndex);
        return (x >> 11) * (1.0 / (1UL << 53));
    }

    // SplitMix64 finalizer.
    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
