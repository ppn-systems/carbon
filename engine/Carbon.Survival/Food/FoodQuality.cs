// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Food;

/// <summary>
/// Preparation quality of a food, mirroring RimWorld's raw / simple / fine / lavish meals.
/// </summary>
public enum FoodQuality : byte
{
    /// <summary>Uncooked food; may cause food poisoning and an "ate raw food" thought.</summary>
    Raw = 0,

    /// <summary>A simple cooked meal.</summary>
    Simple = 1,

    /// <summary>A fine meal.</summary>
    Fine = 2,

    /// <summary>A lavish meal.</summary>
    Lavish = 3,
}
