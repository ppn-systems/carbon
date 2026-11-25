// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Props;

/// <summary>
/// One candidate prop in a rule list. Candidates are tried in order: the first whose
/// <see cref="MaxRoll"/> exceeds the tile's roll and whose <see cref="MinFoliage"/> is met wins.
/// </summary>
/// <param name="Type">Prop type.</param>
/// <param name="MaxRoll">The tile roll (0..1) must be below this.</param>
/// <param name="Radius">Base radius in world units, scaled by the per-tile size variation.</param>
/// <param name="IsSolid">Whether the prop blocks movement.</param>
/// <param name="Primary">Primary RGBA colour.</param>
/// <param name="Secondary">Secondary RGBA colour.</param>
/// <param name="MinFoliage">Minimum foliage density for this candidate.</param>
public readonly record struct PropRule(
    MapPropType Type,
    float MaxRoll,
    float Radius,
    bool IsSolid,
    uint Primary,
    uint Secondary,
    float MinFoliage = 0f);

/// <summary>
/// A vegetation layer: when its condition holds, its candidates are tried. An exclusive layer stops the
/// search once its condition holds, even if no candidate matched (an open glade stays empty).
/// </summary>
/// <param name="TreeSlot">Requires a tree lattice slot (which also implies dry land).</param>
/// <param name="AwayFromWater">Requires the tile not to be near water.</param>
/// <param name="MinFoliage">Minimum foliage density for the layer.</param>
/// <param name="Exclusive">Stop the search when the condition holds.</param>
/// <param name="Rules">Candidates, tried in order.</param>
public sealed record PropLayer(bool TreeSlot, bool AwayFromWater, float MinFoliage, bool Exclusive, PropRule[] Rules);

/// <summary>Everything a biome can grow or expose: ore veins and vegetation layers.</summary>
/// <param name="VeinThreshold">Mineral noise above which the ground forms an active vein.</param>
/// <param name="VeinExclusive">Whether a vein node never hosts vegetation, even when no ore matched.</param>
/// <param name="Ores">Vein node candidates, tried in order.</param>
/// <param name="Layers">Vegetation layers, tried in order.</param>
public sealed record BiomePropTable(float VeinThreshold, bool VeinExclusive, PropRule[] Ores, PropLayer[] Layers)
{
    /// <summary>A biome with no props at all (water).</summary>
    public static readonly BiomePropTable Empty = new(1f, true, [], []);

    /// <summary>Returns the index of the first candidate matching the roll and foliage, or -1.</summary>
    internal static int Pick(ReadOnlySpan<PropRule> rules, float roll, float foliage)
    {
        for (int i = 0; i < rules.Length; i++)
        {
            if (roll < rules[i].MaxRoll && foliage >= rules[i].MinFoliage)
            {
                return i;
            }
        }

        return -1;
    }
}
