// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;

namespace Carbon.Maps.Props;

/// <summary>
/// The prop tables of every biome. To add a tree, ore or plant, add a <see cref="PropRule"/> to the
/// biome's list; to add a biome, add a table and register it in <see cref="Get"/>.
/// </summary>
public static class BiomePropTables
{
    private static readonly PropRule s_boulderBase = new(MapPropType.Boulder, 0f, 20f, true, 0, 0);

    private static readonly BiomePropTable[] s_tables = Build();

    /// <summary>Gets the prop table of a biome.</summary>
    /// <param name="biome">The biome.</param>
    /// <returns>The table (empty for water and unknown biomes).</returns>
    public static BiomePropTable Get(BiomeType biome)
    {
        int i = (int)biome;
        return (uint)i < (uint)s_tables.Length ? s_tables[i] : BiomePropTable.Empty;
    }

    private static PropRule Ore(MapPropType type, float roll, float radius, uint a, uint b) => new(type, roll, radius, true, a, b);

    private static PropRule Boulder(float roll, float radius, uint a, uint b) => s_boulderBase with { MaxRoll = roll, Radius = radius, Primary = a, Secondary = b };

    private static PropRule Solid(MapPropType type, float roll, float radius, uint a, uint b) => new(type, roll, radius, true, a, b);

    private static PropRule Soft(MapPropType type, float roll, float radius, uint a, uint b, float minFoliage = 0f) => new(type, roll, radius, false, a, b, minFoliage);

    // Three-band forest: dense grove, light woodland, then an exclusive empty glade.
    private static PropLayer[] Forest(float dense, PropRule[] denseRules, float light, PropRule[] lightRules, PropLayer undergrowth) =>
    [
        new(TreeSlot: true, AwayFromWater: false, dense, Exclusive: true, denseRules),
        new(TreeSlot: true, AwayFromWater: false, light, Exclusive: true, lightRules),
        new(TreeSlot: true, AwayFromWater: false, 0f, Exclusive: true, []),
        undergrowth,
    ];

    private static PropLayer Trees(float minFoliage, params PropRule[] rules) => new(true, false, minFoliage, true, rules);

    private static PropLayer Scrub(float minFoliage, params PropRule[] rules) => new(false, false, minFoliage, true, rules);

    private static PropLayer Undergrowth(float minFoliage, params PropRule[] rules) => new(false, true, minFoliage, true, rules);

    private static BiomePropTable[] Build()
    {
        BiomePropTable[] t = new BiomePropTable[(int)BiomeType.BlossomWoods + 1];
        Array.Fill(t, BiomePropTable.Empty); // water and any biome without props

        t[(int)BiomeType.BeachSand] = new(0.92f, VeinExclusive: false,
            [Boulder(0.35f, 18f, 0xCBD5E1FF, 0x64748BFF)],
            [Trees(0f, Solid(MapPropType.PalmTree, 0.035f, 34f, 0x10B981FF, 0x92400EFF))]);

        t[(int)BiomeType.LushPlains] = new(0.91f, true,
            [Ore(MapPropType.CopperVein, 0.40f, 21f, 0x78350FFF, 0x2DD4BFFF), Boulder(0.65f, 20f, 0x64748BFF, 0x334155FF)],
            Forest(
                0.72f, [Solid(MapPropType.OakTree, 0.28f, 32f, 0x166534FF, 0x78350FFF), Solid(MapPropType.BirchTree, 0.52f, 28f, 0x84CC16FF, 0xF8FAFCFF)],
                0.48f, [Solid(MapPropType.OakTree, 0.08f, 32f, 0x166534FF, 0x78350FFF), Solid(MapPropType.BirchTree, 0.16f, 28f, 0x84CC16FF, 0xF8FAFCFF)],
                Undergrowth(0.45f, Soft(MapPropType.Bush, 0.015f, 15f, 0x15803DFF, 0x22C55EFF))));

        t[(int)BiomeType.AncientForest] = new(0.86f, true,
            [Ore(MapPropType.CoalDeposit, 0.35f, 21f, 0x18181BFF, 0x3F3F46FF), Ore(MapPropType.IronVein, 0.65f, 21f, 0x334155FF, 0xEA580CFF), Boulder(0.80f, 20f, 0x475569FF, 0x15803DFF)],
            Forest(
                0.65f,
                [
                    Solid(MapPropType.OakTree, 0.28f, 34f, 0x14532DFF, 0x451A03FF),
                    Solid(MapPropType.PineTree, 0.50f, 28f, 0x064E3BFF, 0x713F12FF),
                    Solid(MapPropType.BirchTree, 0.66f, 30f, 0x65A30DFF, 0xF8FAFCFF),
                ],
                0.36f,
                [
                    Solid(MapPropType.OakTree, 0.14f, 34f, 0x14532DFF, 0x451A03FF),
                    Solid(MapPropType.PineTree, 0.25f, 28f, 0x064E3BFF, 0x713F12FF),
                    Solid(MapPropType.BirchTree, 0.34f, 30f, 0x65A30DFF, 0xF8FAFCFF),
                ],
                Undergrowth(
                    0f,
                    Soft(MapPropType.Bush, 0.025f, 17f, 0x166534FF, 0x15803DFF),
                    Soft(MapPropType.MushroomCluster, 0.045f, 13f, 0x78350FFF, 0xFEF3C7FF, minFoliage: 0.50f))));

        t[(int)BiomeType.MistySwamp] = new(0.88f, true,
            [Ore(MapPropType.IronVein, 0.45f, 21f, 0x334155FF, 0xEA580CFF), Boulder(0.70f, 19f, 0x334155FF, 0x1E293BFF)],
            [
                Trees(0.42f, Solid(MapPropType.WillowTree, 0.28f, 40f, 0x4D7C0FFF, 0x3F6212FF), Solid(MapPropType.DeadTree, 0.44f, 24f, 0x1E293BFF, 0x334155FF)),
                Undergrowth(0f, Soft(MapPropType.MushroomCluster, 0.035f, 13f, 0x854D0EFF, 0xFEF3C7FF)),
            ]);

        t[(int)BiomeType.DuneDesert] = new(0.84f, true,
            [Ore(MapPropType.GoldVein, 0.35f, 21f, 0x1E293BFF, 0xFACC15FF), Boulder(0.65f, 20f, 0xB45309FF, 0x78350FFF)],
            [Scrub(0.68f, Solid(MapPropType.Cactus, 0.035f, 21f, 0x15803DFF, 0x166534FF), Solid(MapPropType.DeadTree, 0.055f, 24f, 0x71717AFF, 0x52525BFF))]);

        t[(int)BiomeType.RedMesa] = new(0.74f, true,
            [
                Ore(MapPropType.CopperVein, 0.35f, 21f, 0x78350FFF, 0x2DD4BFFF),
                Ore(MapPropType.IronVein, 0.65f, 21f, 0x334155FF, 0xEA580CFF),
                Ore(MapPropType.GoldVein, 0.85f, 21f, 0x1E293BFF, 0xFACC15FF),
                Boulder(0.95f, 20f, 0x7F1D1DFF, 0x991B1BFF),
            ],
            [Scrub(0.70f, Solid(MapPropType.Cactus, 0.025f, 21f, 0x166534FF, 0x15803DFF), Solid(MapPropType.DeadTree, 0.045f, 22f, 0x52525BFF, 0x3F3F46FF))]);

        t[(int)BiomeType.TropicalJungle] = new(0.88f, true,
            [Ore(MapPropType.CopperVein, 0.45f, 21f, 0x78350FFF, 0x2DD4BFFF), Ore(MapPropType.CrystalCluster, 0.70f, 23f, 0x0D9488FF, 0x2DD4BFFF), Boulder(0.85f, 20f, 0x475569FF, 0x047857FF)],
            Forest(
                0.65f, [Solid(MapPropType.PalmTree, 0.40f, 36f, 0x059669FF, 0x78350FFF), Solid(MapPropType.OakTree, 0.68f, 44f, 0x047857FF, 0x451A03FF)],
                0.40f, [Solid(MapPropType.PalmTree, 0.22f, 36f, 0x059669FF, 0x78350FFF), Solid(MapPropType.OakTree, 0.38f, 44f, 0x047857FF, 0x451A03FF)],
                Undergrowth(0f, Soft(MapPropType.Bush, 0.025f, 18f, 0x059669FF, 0x10B981FF))));

        t[(int)BiomeType.FrozenTundra] = new(0.82f, true,
            [Ore(MapPropType.SilverVein, 0.35f, 21f, 0x475569FF, 0xF1F5F9FF), Ore(MapPropType.CrystalCluster, 0.65f, 22f, 0x0284C7FF, 0x38BDF8FF), Boulder(0.80f, 20f, 0x94A3B8FF, 0x38BDF8FF)],
            [Trees(0.65f, Solid(MapPropType.SnowPine, 0.32f, 32f, 0xF1F5F9FF, 0x0F766EFF))]);

        t[(int)BiomeType.VolcanicAshland] = new(0.72f, true,
            [
                Ore(MapPropType.BasaltPillar, 0.30f, 23f, 0x09090BFF, 0x27272AFF),
                Ore(MapPropType.GoldVein, 0.60f, 21f, 0x18181BFF, 0xFACC15FF),
                Ore(MapPropType.CrystalCluster, 0.80f, 23f, 0x7E22CEFF, 0xC084FCFF),
                Boulder(0.90f, 20f, 0x27272AFF, 0x09090BFF),
            ],
            [Scrub(0.70f, Solid(MapPropType.DeadTree, 0.030f, 24f, 0x18181BFF, 0x3F3F46FF), Solid(MapPropType.BasaltPillar, 0.050f, 23f, 0x09090BFF, 0x27272AFF))]);

        t[(int)BiomeType.AlpinePeaks] = new(0.75f, true,
            [Ore(MapPropType.SilverVein, 0.35f, 21f, 0x475569FF, 0xF1F5F9FF), Ore(MapPropType.GlacialCrystal, 0.70f, 22f, 0x0EA5E9FF, 0xE0F2FEFF), Boulder(0.85f, 20f, 0x94A3B8FF, 0xE2E8F0FF)],
            [Trees(0.65f, Solid(MapPropType.SnowPine, 0.25f, 32f, 0xF8FAFCFF, 0x0284C7FF))]);

        t[(int)BiomeType.Savanna] = new(0.85f, true,
            [Ore(MapPropType.CopperVein, 0.35f, 21f, 0x78350FFF, 0x2DD4BFFF), Ore(MapPropType.IronVein, 0.65f, 21f, 0x334155FF, 0xEA580CFF), Boulder(0.80f, 20f, 0xB45309FF, 0x78350FFF)],
            [
                Trees(0.48f, Solid(MapPropType.AcaciaTree, 0.25f, 38f, 0x65A30DFF, 0x78350FFF)),
                Undergrowth(0f, Soft(MapPropType.Bush, 0.020f, 15f, 0xA16207FF, 0xCA8A04FF)),
            ]);

        t[(int)BiomeType.BlossomWoods] = new(0.91f, true,
            [Ore(MapPropType.CrystalCluster, 0.35f, 23f, 0xC084FCFF, 0xF472B6FF), Ore(MapPropType.CopperVein, 0.65f, 21f, 0x78350FFF, 0x2DD4BFFF), Boulder(0.80f, 20f, 0x475569FF, 0xEC4899FF)],
            Forest(
                0.65f, [Solid(MapPropType.CherryBlossomTree, 0.45f, 36f, 0xF472B6FF, 0x3F2213FF), Solid(MapPropType.OakTree, 0.68f, 34f, 0x059669FF, 0x451A03FF)],
                0.40f, [Solid(MapPropType.CherryBlossomTree, 0.25f, 36f, 0xF472B6FF, 0x3F2213FF), Solid(MapPropType.BirchTree, 0.40f, 30f, 0x84CC16FF, 0xF8FAFCFF)],
                Undergrowth(
                    0f,
                    Soft(MapPropType.Bush, 0.035f, 16f, 0x10B981FF, 0xF472B6FF),
                    Soft(MapPropType.MushroomCluster, 0.055f, 14f, 0xA855F7FF, 0xFAF5FFFF))));
        return t;
    }
}
