// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Random;

namespace Carbon.Maps.Props;

/// <summary>
/// Resolves and populates procedural trees, mineral ore deposits, foliage, and environmental landmarks
/// specific to each Whittaker biome using deterministic spatial hashing with cellular clearance.
/// </summary>
public static class BiomePropResolver
{
    /// <summary>
    /// Evaluates whether a prop spawns at the given world tile coordinate and resolves its attributes.
    /// </summary>
    /// <param name="worldTileX">Tile X coordinate in world space.</param>
    /// <param name="worldTileY">Tile Y coordinate in world space.</param>
    /// <param name="localX">Local X coordinate within the chunk [0..31].</param>
    /// <param name="localY">Local Y coordinate within the chunk [0..31].</param>
    /// <param name="biome">Biome classification of the underlying terrain.</param>
    /// <param name="tileSize">Physical dimension of a tile in world units.</param>
    /// <param name="seed">Deterministic generation seed.</param>
    /// <param name="prop">The populated prop if successfully placed.</param>
    /// <returns>True if a prop is present on this tile; otherwise false.</returns>
    public static bool TryResolveProp(
        int worldTileX,
        int worldTileY,
        int localX,
        int localY,
        BiomeType biome,
        float tileSize,
        ulong seed,
        out MapProp prop)
    {
        prop = default;

        // Water biomes do not spawn standard land props
        if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            return false;
        }

        // Fast deterministic spatial hash per tile
        ulong tileSeed = seed ^ (ulong)(worldTileX * 73856093) ^ (ulong)(worldTileY * 19349663);
        DeterministicRandom rng = new(tileSeed);

        float roll = rng.NextFloat(0f, 1f);
        float offsetX = rng.NextFloat(tileSize * 0.3f, tileSize * 0.7f);
        float offsetY = rng.NextFloat(tileSize * 0.3f, tileSize * 0.7f);
        float worldX = (worldTileX * tileSize) + offsetX;
        float worldY = (worldTileY * tileSize) + offsetY;
        float scaleVar = rng.NextFloat(0.92f, 1.18f);

        // Clearance rule: Heavy mineral deposits, boulders, and shrines require minimum cellular spacing
        // to prevent unnatural clumping and preserve open MMORPG exploration lanes
        bool isResourceCell = ((worldTileX & 3) == 0) && ((worldTileY & 3) == 0);

        switch (biome)
        {
            case BiomeType.DeepOcean:
            case BiomeType.ShallowWater:
                return false;

            case BiomeType.LushPlains:
                // Gentle, airy meadows: solitary oaks/birches, soft shrubs, and rare, precious copper deposits
                if (roll < 0.007f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 42f * scaleVar, isSolid: true, 0x166534FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.012f)
                {
                    prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 36f * scaleVar, isSolid: true, 0x84CC16FF, 0xF8FAFCFF, scaleVar);
                    return true;
                }

                if (roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 17f * scaleVar, isSolid: false, 0x15803DFF, 0x22C55EFF, scaleVar);
                    return true;
                }

                // Spaced heavy formations (at most 1 per ~16 tiles)
                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.05f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.10f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x64748BFF, 0x334155FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.105f)
                    {
                        prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
                        return true;
                    }
                }

                break;

            case BiomeType.AncientForest:
                // Towering woodland canopy with shaded undergrowth and deep-earth coal and iron
                if (roll < 0.045f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 46f * scaleVar, isSolid: true, 0x14532DFF, 0x451A03FF, scaleVar);
                    return true;
                }

                if (roll < 0.075f)
                {
                    prop = MapProp.Create(MapPropType.PineTree, localX, localY, worldX, worldY, radius: 32f * scaleVar, isSolid: true, 0x064E3BFF, 0x713F12FF, scaleVar);
                    return true;
                }

                if (roll < 0.095f)
                {
                    prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 38f * scaleVar, isSolid: true, 0x65A30DFF, 0xF8FAFCFF, scaleVar);
                    return true;
                }

                if (roll < 0.115f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 17f * scaleVar, isSolid: false, 0x166534FF, 0x15803DFF, scaleVar);
                    return true;
                }

                if (roll < 0.130f)
                {
                    prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 13f * scaleVar, isSolid: false, 0x78350FFF, 0xFEF3C7FF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.06f)
                    {
                        prop = MapProp.Create(MapPropType.CoalDeposit, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.11f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.16f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x475569FF, 0x15803DFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.166f)
                    {
                        prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
                        return true;
                    }
                }

                break;

            case BiomeType.FrozenTundra:
                // Desolate snow plains with frosted conifers and rare mineral outcroppings
                if (roll < 0.018f)
                {
                    prop = MapProp.Create(MapPropType.SnowPine, localX, localY, worldX, worldY, radius: 32f * scaleVar, isSolid: true, 0xF1F5F9FF, 0x0F766EFF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.06f)
                    {
                        prop = MapProp.Create(MapPropType.SilverVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x475569FF, 0xF1F5F9FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.10f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 22f * scaleVar, isSolid: true, 0x0284C7FF, 0x38BDF8FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.15f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x94A3B8FF, 0x38BDF8FF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.DuneDesert:
                // Vast arid dunes with solitary saguaros, bleached wood, and hidden gold
                if (roll < 0.006f)
                {
                    prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x15803DFF, 0x166534FF, scaleVar);
                    return true;
                }

                if (roll < 0.010f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 24f * scaleVar, isSolid: true, 0x71717AFF, 0x52525BFF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.04f)
                    {
                        prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x1E293BFF, 0xFACC15FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.08f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0xB45309FF, 0x78350FFF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.BeachSand:
                // Coastal shore: palm trees and smooth water-washed boulders
                if (roll < 0.009f)
                {
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x10B981FF, 0x92400EFF, scaleVar);
                    return true;
                }

                if (isResourceCell && roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 18f * scaleVar, isSolid: true, 0xCBD5E1FF, 0x64748BFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.MistySwamp:
                // Murky wetlands: weeping willows, dead cypress trees, and bog iron
                if (roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.WillowTree, localX, localY, worldX, worldY, radius: 40f * scaleVar, isSolid: true, 0x4D7C0FFF, 0x3F6212FF, scaleVar);
                    return true;
                }

                if (roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 24f * scaleVar, isSolid: true, 0x1E293BFF, 0x334155FF, scaleVar);
                    return true;
                }

                if (roll < 0.050f)
                {
                    prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 13f * scaleVar, isSolid: false, 0x854D0EFF, 0xFEF3C7FF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.07f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.12f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 19f * scaleVar, isSolid: true, 0x334155FF, 0x1E293BFF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.VolcanicAshland:
                // Volcanic badlands: basalt columns, charred wood, gold, and crystals
                if (roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.BasaltPillar, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x09090BFF, 0x27272AFF, scaleVar);
                    return true;
                }

                if (roll < 0.030f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 24f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.06f)
                    {
                        prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x18181BFF, 0xFACC15FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.12f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x7E22CEFF, 0xC084FCFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.17f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x27272AFF, 0x09090BFF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.RedMesa:
                // Primary mining territory: rich copper, iron, and gold outcroppings on sandstone
                if (roll < 0.008f)
                {
                    prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x166534FF, 0x15803DFF, scaleVar);
                    return true;
                }

                if (roll < 0.014f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 22f * scaleVar, isSolid: true, 0x52525BFF, 0x3F3F46FF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.08f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.14f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.18f)
                    {
                        prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x1E293BFF, 0xFACC15FF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.23f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x7F1D1DFF, 0x991B1BFF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.TropicalJungle:
                // Exotic rainforest: dense palms, jungle broadleaves, lush bushes, copper, and crystals
                if (roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 36f * scaleVar, isSolid: true, 0x059669FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.075f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 44f * scaleVar, isSolid: true, 0x047857FF, 0x451A03FF, scaleVar);
                    return true;
                }

                if (roll < 0.100f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 18f * scaleVar, isSolid: false, 0x059669FF, 0x10B981FF, scaleVar);
                    return true;
                }

                if (isResourceCell)
                {
                    float resRoll = rng.NextFloat(0f, 1f);
                    if (resRoll < 0.06f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.11f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x0D9488FF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (resRoll < 0.16f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x475569FF, 0x047857FF, scaleVar);
                        return true;
                    }
                }

                break;

            default:
                break;
        }

        return false;
    }
}
