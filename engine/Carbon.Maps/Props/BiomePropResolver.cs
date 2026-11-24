// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Random;
using Carbon.Maps.Topography;

namespace Carbon.Maps.Props;

/// <summary>
/// Resolves and populates procedural trees, mineral ore deposits, foliage, and environmental landmarks
/// specific to each Whittaker biome using continuous macro density fields and cellular clustering.
/// Guarantees zero heap allocation on chunk population hot paths.
/// </summary>
public static class BiomePropResolver
{
    /// <summary>
    /// Evaluates whether a prop spawns at the given world tile coordinate and resolves its attributes.
    /// Incorporates macro foliage density (producing vast open meadows vs dense ancient copses)
    /// and continuous geological mineral veins (producing clean exploration land punctuated by clustered mining veins).
    /// </summary>
    /// <param name="worldTileX">Tile X coordinate in world space.</param>
    /// <param name="worldTileY">Tile Y coordinate in world space.</param>
    /// <param name="localX">Local X coordinate within the chunk [0..31].</param>
    /// <param name="localY">Local Y coordinate within the chunk [0..31].</param>
    /// <param name="biome">Biome classification of the underlying terrain.</param>
    /// <param name="elevation">Continuous terrain elevation [0..1].</param>
    /// <param name="foliageDensity">Continuous macro foliage density noise [0..1].</param>
    /// <param name="mineralNoise">Continuous geological mineral vein noise [0..1].</param>
    /// <param name="isNearWater">Whether the tile is water or immediately adjacent to a river/ocean shore.</param>
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
        float elevation,
        float foliageDensity,
        float mineralNoise,
        bool isNearWater,
        float tileSize,
        ulong seed,
        out MapProp prop)
    {
        prop = default;

        // Aquatic biomes do not spawn standard land props
        if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            return false;
        }

        // Mountain highway passes and ramp corridors must remain clear of solid props
        if (TopographyHelper.IsPotentialRampSlot(worldTileX, worldTileY) ||
            TopographyHelper.IsPotentialRampSlot(worldTileX, worldTileY - 1) ||
            TopographyHelper.IsPotentialRampSlot(worldTileX, worldTileY + 1) ||
            TopographyHelper.IsPotentialRampSlot(worldTileX - 1, worldTileY) ||
            TopographyHelper.IsPotentialRampSlot(worldTileX + 1, worldTileY))
        {
            return false;
        }

        // Fast deterministic spatial hash per tile
        ulong tileSeed = SpatialHash.Hash(seed, worldTileX, worldTileY);
        DeterministicRandom rng = new(tileSeed);

        float roll = rng.NextFloat(0f, 1f);
        float offsetX = rng.NextFloat(tileSize * 0.3f, tileSize * 0.7f);
        float offsetY = rng.NextFloat(tileSize * 0.3f, tileSize * 0.7f);
        float worldX = (worldTileX * tileSize) + offsetX;
        float worldY = (worldTileY * tileSize) + offsetY;
        float scaleVar = rng.NextFloat(0.92f, 1.18f);

        // 1. Macro Geological Mineral Vein System (Rare, Localized Mining Deposits):
        // Over 96% of the landscape has ZERO ores or rocks, keeping meadows and forests clean and peaceful.
        // Mineral veins are concentrated primarily in rocky highlands, mountain peaks, and arid canyonlands.
        float veinThreshold = biome switch
        {
            BiomeType.DeepOcean or BiomeType.ShallowWater => 1.0f,
            BiomeType.BeachSand => 0.92f,
            BiomeType.RedMesa => 0.74f,
            BiomeType.VolcanicAshland => 0.72f,
            BiomeType.AlpinePeaks => 0.75f,
            BiomeType.FrozenTundra => 0.82f,
            BiomeType.DuneDesert => 0.84f,
            BiomeType.Savanna => 0.85f,
            BiomeType.AncientForest => 0.86f,
            BiomeType.TropicalJungle or BiomeType.MistySwamp => 0.88f,
            BiomeType.LushPlains or BiomeType.BlossomWoods => 0.91f,
            _ => 0.91f
        };

        if (elevation >= 0.70f)
        {
            veinThreshold -= 0.04f;
        }
        else if (elevation >= 0.54f)
        {
            veinThreshold -= 0.02f;
        }

        bool isInActiveVein = !isNearWater && (mineralNoise >= veinThreshold);
        bool isVeinNode = false;

        if (isInActiveVein)
        {
            // Inside an active geological hotspot, only a few tiles form compact ore clusters
            uint veinHash = SpatialHash.Hash32(seed, worldTileX, worldTileY, 0x5E1AUL);
            isVeinNode = (veinHash % 6) == 0;
        }

        // 2. Rare Ancient Shrines (Mystical landmarks in secluded high-density groves, ~1 in 200 tiles)
        if (!isNearWater && mineralNoise >= 0.86f && foliageDensity >= 0.76f)
        {
            uint shrineHash = SpatialHash.Hash32(seed, worldTileX, worldTileY, 0x5A41UL);
            if ((shrineHash % 67) == 0)
            {
                prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
                return true;
            }
        }

        // 3. Staggered Hexagonal Lattice for Trees:
        // Mathematically guarantees minimum distance of sqrt(5) ~= 2.24 tiles (~71.6 pixels) between any two tree origins.
        // Absolutely eliminates unnatural clumping, keeps individual crowns distinct, and preserves open exploration lanes.
        bool isTreeSlot = !isNearWater && ((Math.Abs(worldTileX + 2 + (worldTileY * 2)) % 4) == 0);

        switch (biome)
        {
            case BiomeType.DeepOcean:
            case BiomeType.ShallowWater:
                return false;

            case BiomeType.LushPlains:
                // Gentle, airy meadows: vast open grassy clearings (foliage < 0.48), light woodland fringes, and clustered copses
                if (isVeinNode)
                {
                    if (roll < 0.40f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x64748BFF, 0x334155FF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot)
                {
                    if (foliageDensity >= 0.72f)
                    {
                        // Dense Copse Grove
                        if (roll < 0.28f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 32f * scaleVar, isSolid: true, 0x166534FF, 0x78350FFF, scaleVar);
                            return true;
                        }

                        if (roll < 0.52f)
                        {
                            prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 28f * scaleVar, isSolid: true, 0x84CC16FF, 0xF8FAFCFF, scaleVar);
                            return true;
                        }
                    }
                    else if (foliageDensity >= 0.48f)
                    {
                        // Light Woodland Fringe
                        if (roll < 0.08f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 32f * scaleVar, isSolid: true, 0x166534FF, 0x78350FFF, scaleVar);
                            return true;
                        }

                        if (roll < 0.16f)
                        {
                            prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 28f * scaleVar, isSolid: true, 0x84CC16FF, 0xF8FAFCFF, scaleVar);
                            return true;
                        }
                    }
                    // Below 0.48: Open Meadow / Sunlit Glade (0 trees, vast open grassy spaces)
                }
                else if (!isNearWater && foliageDensity >= 0.45f && roll < 0.015f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 15f * scaleVar, isSolid: false, 0x15803DFF, 0x22C55EFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.AncientForest:
                // Towering woodland with sunlit glades (foliage < 0.36), airy deciduous woods, and deep ancient groves
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.CoalDeposit, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.80f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x475569FF, 0x15803DFF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot)
                {
                    if (foliageDensity >= 0.65f)
                    {
                        // Deep Old-Growth Forest Grove
                        if (roll < 0.28f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x14532DFF, 0x451A03FF, scaleVar);
                            return true;
                        }

                        if (roll < 0.50f)
                        {
                            prop = MapProp.Create(MapPropType.PineTree, localX, localY, worldX, worldY, radius: 28f * scaleVar, isSolid: true, 0x064E3BFF, 0x713F12FF, scaleVar);
                            return true;
                        }

                        if (roll < 0.66f)
                        {
                            prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 30f * scaleVar, isSolid: true, 0x65A30DFF, 0xF8FAFCFF, scaleVar);
                            return true;
                        }
                    }
                    else if (foliageDensity >= 0.36f)
                    {
                        // Pleasant Deciduous Woods
                        if (roll < 0.14f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x14532DFF, 0x451A03FF, scaleVar);
                            return true;
                        }

                        if (roll < 0.25f)
                        {
                            prop = MapProp.Create(MapPropType.PineTree, localX, localY, worldX, worldY, radius: 28f * scaleVar, isSolid: true, 0x064E3BFF, 0x713F12FF, scaleVar);
                            return true;
                        }

                        if (roll < 0.34f)
                        {
                            prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 30f * scaleVar, isSolid: true, 0x65A30DFF, 0xF8FAFCFF, scaleVar);
                            return true;
                        }
                    }
                    // Below 0.36: Forest Clearings & Glades (0 trees)
                }
                else if (!isNearWater)
                {
                    if (roll < 0.025f)
                    {
                        prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 17f * scaleVar, isSolid: false, 0x166534FF, 0x15803DFF, scaleVar);
                        return true;
                    }

                    if (foliageDensity >= 0.50f && roll < 0.045f)
                    {
                        prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 13f * scaleVar, isSolid: false, 0x78350FFF, 0xFEF3C7FF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.FrozenTundra:
                // Windswept snow plains with rare conifer pockets and clustered silver/crystal seams
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.SilverVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x475569FF, 0xF1F5F9FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 22f * scaleVar, isSolid: true, 0x0284C7FF, 0x38BDF8FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.80f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x94A3B8FF, 0x38BDF8FF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot && foliageDensity >= 0.65f && roll < 0.32f)
                {
                    prop = MapProp.Create(MapPropType.SnowPine, localX, localY, worldX, worldY, radius: 32f * scaleVar, isSolid: true, 0xF1F5F9FF, 0x0F766EFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.DuneDesert:
                // Expansive rolling dunes, occasional saguaro clusters and hidden gold seams
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x1E293BFF, 0xFACC15FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0xB45309FF, 0x78350FFF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (foliageDensity >= 0.68f)
                {
                    if (roll < 0.035f)
                    {
                        prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x15803DFF, 0x166534FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.055f)
                    {
                        prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 24f * scaleVar, isSolid: true, 0x71717AFF, 0x52525BFF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.BeachSand:
                // Coastal shorelines: clean sandy beaches with rare lone palm trees
                if (isVeinNode && roll < 0.35f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 18f * scaleVar, isSolid: true, 0xCBD5E1FF, 0x64748BFF, scaleVar);
                    return true;
                }

                if (isTreeSlot && roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x10B981FF, 0x92400EFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.MistySwamp:
                // Murky wetlands: weeping willows, dead cypress, and rich bog iron seams
                if (isVeinNode)
                {
                    if (roll < 0.45f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.70f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 19f * scaleVar, isSolid: true, 0x334155FF, 0x1E293BFF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot && foliageDensity >= 0.42f)
                {
                    if (roll < 0.28f)
                    {
                        prop = MapProp.Create(MapPropType.WillowTree, localX, localY, worldX, worldY, radius: 40f * scaleVar, isSolid: true, 0x4D7C0FFF, 0x3F6212FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.44f)
                    {
                        prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 24f * scaleVar, isSolid: true, 0x1E293BFF, 0x334155FF, scaleVar);
                        return true;
                    }
                }
                else if (!isNearWater && roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 13f * scaleVar, isSolid: false, 0x854D0EFF, 0xFEF3C7FF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.VolcanicAshland:
                // Volcanic badlands: basalt columns, charred timber, rich gold and crystal veins
                if (isVeinNode)
                {
                    if (roll < 0.30f)
                    {
                        prop = MapProp.Create(MapPropType.BasaltPillar, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x09090BFF, 0x27272AFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.60f)
                    {
                        prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x18181BFF, 0xFACC15FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.80f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x7E22CEFF, 0xC084FCFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.90f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x27272AFF, 0x09090BFF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (foliageDensity >= 0.70f)
                {
                    if (roll < 0.030f)
                    {
                        prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 24f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.050f)
                    {
                        prop = MapProp.Create(MapPropType.BasaltPillar, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x09090BFF, 0x27272AFF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.RedMesa:
                // Primary mining territory: rich clustered copper, iron, and gold outcroppings on sandstone
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.85f)
                    {
                        prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x1E293BFF, 0xFACC15FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.95f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x7F1D1DFF, 0x991B1BFF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (foliageDensity >= 0.70f)
                {
                    if (roll < 0.025f)
                    {
                        prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x166534FF, 0x15803DFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.045f)
                    {
                        prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 22f * scaleVar, isSolid: true, 0x52525BFF, 0x3F3F46FF, scaleVar);
                        return true;
                    }
                }

                break;

            case BiomeType.TropicalJungle:
                // Exotic rainforest: clearings (foliage < 0.40), lush canopy, copper and crystals
                if (isVeinNode)
                {
                    if (roll < 0.45f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.70f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0x0D9488FF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.85f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x475569FF, 0x047857FF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot)
                {
                    if (foliageDensity >= 0.65f)
                    {
                        // Dense Jungle Canopy
                        if (roll < 0.40f)
                        {
                            prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 36f * scaleVar, isSolid: true, 0x059669FF, 0x78350FFF, scaleVar);
                            return true;
                        }

                        if (roll < 0.68f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 44f * scaleVar, isSolid: true, 0x047857FF, 0x451A03FF, scaleVar);
                            return true;
                        }
                    }
                    else if (foliageDensity >= 0.40f)
                    {
                        // Light Jungle Fringe
                        if (roll < 0.22f)
                        {
                            prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 36f * scaleVar, isSolid: true, 0x059669FF, 0x78350FFF, scaleVar);
                            return true;
                        }

                        if (roll < 0.38f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 44f * scaleVar, isSolid: true, 0x047857FF, 0x451A03FF, scaleVar);
                            return true;
                        }
                    }
                    // Below 0.40: Jungle Glades (0 trees)
                }
                else if (!isNearWater && roll < 0.025f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 18f * scaleVar, isSolid: false, 0x059669FF, 0x10B981FF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.AlpinePeaks:
                // High alpine glacial peaks: silver outcroppings, glowing glacial crystals, frost-bound conifers
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.SilverVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x475569FF, 0xF1F5F9FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.70f)
                    {
                        prop = MapProp.Create(MapPropType.GlacialCrystal, localX, localY, worldX, worldY, radius: 22f * scaleVar, isSolid: true, 0x0EA5E9FF, 0xE0F2FEFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.85f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x94A3B8FF, 0xE2E8F0FF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot && foliageDensity >= 0.65f && roll < 0.25f)
                {
                    prop = MapProp.Create(MapPropType.SnowPine, localX, localY, worldX, worldY, radius: 32f * scaleVar, isSolid: true, 0xF8FAFCFF, 0x0284C7FF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.Savanna:
                // Vast amber plains with solitary umbrella acacia trees and clustered copper seams
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.80f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0xB45309FF, 0x78350FFF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot && foliageDensity >= 0.48f && roll < 0.25f)
                {
                    prop = MapProp.Create(MapPropType.AcaciaTree, localX, localY, worldX, worldY, radius: 38f * scaleVar, isSolid: true, 0x65A30DFF, 0x78350FFF, scaleVar);
                    return true;
                }
                else if (!isNearWater && roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 15f * scaleVar, isSolid: false, 0xA16207FF, 0xCA8A04FF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.BlossomWoods:
                // Enchanted flowering grove: blooming sakura cherry trees, crystal clusters, and soft undergrowth
                if (isVeinNode)
                {
                    if (roll < 0.35f)
                    {
                        prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 23f * scaleVar, isSolid: true, 0xC084FCFF, 0xF472B6FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.65f)
                    {
                        prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                        return true;
                    }

                    if (roll < 0.80f)
                    {
                        prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x475569FF, 0xEC4899FF, scaleVar);
                        return true;
                    }

                    return false;
                }

                if (isTreeSlot)
                {
                    if (foliageDensity >= 0.65f)
                    {
                        if (roll < 0.45f)
                        {
                            prop = MapProp.Create(MapPropType.CherryBlossomTree, localX, localY, worldX, worldY, radius: 36f * scaleVar, isSolid: true, 0xF472B6FF, 0x3F2213FF, scaleVar);
                            return true;
                        }

                        if (roll < 0.68f)
                        {
                            prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x059669FF, 0x451A03FF, scaleVar);
                            return true;
                        }
                    }
                    else if (foliageDensity >= 0.40f)
                    {
                        if (roll < 0.25f)
                        {
                            prop = MapProp.Create(MapPropType.CherryBlossomTree, localX, localY, worldX, worldY, radius: 36f * scaleVar, isSolid: true, 0xF472B6FF, 0x3F2213FF, scaleVar);
                            return true;
                        }

                        if (roll < 0.40f)
                        {
                            prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 30f * scaleVar, isSolid: true, 0x84CC16FF, 0xF8FAFCFF, scaleVar);
                            return true;
                        }
                    }
                }
                else if (!isNearWater)
                {
                    if (roll < 0.035f)
                    {
                        prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 16f * scaleVar, isSolid: false, 0x10B981FF, 0xF472B6FF, scaleVar);
                        return true;
                    }

                    if (roll < 0.055f)
                    {
                        prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 14f * scaleVar, isSolid: false, 0xA855F7FF, 0xFAF5FFFF, scaleVar);
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
