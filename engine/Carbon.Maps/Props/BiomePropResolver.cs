// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Random;

namespace Carbon.Maps.Props;

/// <summary>
/// Resolves and populates procedural trees, mineral ore deposits, foliage, and environmental landmarks
/// specific to each Whittaker biome using deterministic spatial hashing.
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
        float offsetX = rng.NextFloat(tileSize * 0.25f, tileSize * 0.75f);
        float offsetY = rng.NextFloat(tileSize * 0.25f, tileSize * 0.75f);
        float worldX = (worldTileX * tileSize) + offsetX;
        float worldY = (worldTileY * tileSize) + offsetY;
        float scaleVar = rng.NextFloat(0.85f, 1.25f);

        switch (biome)
        {
            case BiomeType.DeepOcean:
            case BiomeType.ShallowWater:
                return false;

            case BiomeType.LushPlains:
                // Vast open meadows: low tree density (~2%), bushes, boulders, and occasional copper veins
                if (roll < 0.012f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 18f * scaleVar, isSolid: true, 0x166534FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 15f * scaleVar, isSolid: true, 0x84CC16FF, 0xF8FAFCFF, scaleVar);
                    return true;
                }

                if (roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: false, 0x15803DFF, 0x22C55EFF, scaleVar);
                    return true;
                }

                if (roll < 0.041f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x64748BFF, 0x334155FF, scaleVar);
                    return true;
                }

                if (roll < 0.046f)
                {
                    prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                    return true;
                }

                if (roll < 0.0465f)
                {
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 14f, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
                    return true;
                }

                break;

            case BiomeType.AncientForest:
                // Dense canopy with towering oaks, birches, pines, rich undergrowth, and coal/iron veins
                if (roll < 0.080f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 20f * scaleVar, isSolid: true, 0x14532DFF, 0x451A03FF, scaleVar);
                    return true;
                }

                if (roll < 0.130f)
                {
                    prop = MapProp.Create(MapPropType.PineTree, localX, localY, worldX, worldY, radius: 14f * scaleVar, isSolid: true, 0x064E3BFF, 0x713F12FF, scaleVar);
                    return true;
                }

                if (roll < 0.160f)
                {
                    prop = MapProp.Create(MapPropType.BirchTree, localX, localY, worldX, worldY, radius: 16f * scaleVar, isSolid: true, 0x65A30DFF, 0xF8FAFCFF, scaleVar);
                    return true;
                }

                if (roll < 0.190f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: false, 0x166534FF, 0x15803DFF, scaleVar);
                    return true;
                }

                if (roll < 0.210f)
                {
                    prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 5f * scaleVar, isSolid: false, 0x78350FFF, 0xFEF3C7FF, scaleVar);
                    return true;
                }

                if (roll < 0.222f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x475569FF, 0x15803DFF, scaleVar);
                    return true;
                }

                if (roll < 0.230f)
                {
                    prop = MapProp.Create(MapPropType.CoalDeposit, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                    return true;
                }

                if (roll < 0.237f)
                {
                    prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                    return true;
                }

                if (roll < 0.238f)
                {
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 14f, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
                    return true;
                }

                break;

            case BiomeType.FrozenTundra:
                // Desolate snowy waste: snow-dusted pines, frozen boulders, silver, coal, and ice crystals
                if (roll < 0.025f)
                {
                    prop = MapProp.Create(MapPropType.SnowPine, localX, localY, worldX, worldY, radius: 13f * scaleVar, isSolid: true, 0xF1F5F9FF, 0x0F766EFF, scaleVar);
                    return true;
                }

                if (roll < 0.040f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x94A3B8FF, 0x38BDF8FF, scaleVar);
                    return true;
                }

                if (roll < 0.047f)
                {
                    prop = MapProp.Create(MapPropType.SilverVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x475569FF, 0xF1F5F9FF, scaleVar);
                    return true;
                }

                if (roll < 0.053f)
                {
                    prop = MapProp.Create(MapPropType.CoalDeposit, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                    return true;
                }

                if (roll < 0.058f)
                {
                    prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x0284C7FF, 0x38BDF8FF, scaleVar);
                    return true;
                }

                if (roll < 0.0585f)
                {
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 14f, isSolid: true, 0x38BDF8FF, 0x0284C7FF, 1.3f);
                    return true;
                }

                break;

            case BiomeType.DuneDesert:
                // Arid expanse: solitary cacti, dead wood, desert rocks, and rare gold veins
                if (roll < 0.008f)
                {
                    prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x15803DFF, 0x166534FF, scaleVar);
                    return true;
                }

                if (roll < 0.012f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x71717AFF, 0x52525BFF, scaleVar);
                    return true;
                }

                if (roll < 0.017f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0xB45309FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x1E293BFF, 0xFACC15FF, scaleVar);
                    return true;
                }

                if (roll < 0.0205f)
                {
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 15f, isSolid: true, 0xF59E0BFF, 0xB45309FF, 1.4f);
                    return true;
                }

                break;

            case BiomeType.BeachSand:
                // Coastal shore: palm trees and smooth water-washed boulders
                if (roll < 0.012f)
                {
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 14f * scaleVar, isSolid: true, 0x10B981FF, 0x92400EFF, scaleVar);
                    return true;
                }

                if (roll < 0.020f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 6f * scaleVar, isSolid: true, 0xCBD5E1FF, 0x64748BFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.MistySwamp:
                // Murky wetlands: weeping willows, dead trees, mushrooms, and bog iron
                if (roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.WillowTree, localX, localY, worldX, worldY, radius: 17f * scaleVar, isSolid: true, 0x4D7C0FFF, 0x3F6212FF, scaleVar);
                    return true;
                }

                if (roll < 0.060f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x1E293BFF, 0x334155FF, scaleVar);
                    return true;
                }

                if (roll < 0.085f)
                {
                    prop = MapProp.Create(MapPropType.MushroomCluster, localX, localY, worldX, worldY, radius: 5f * scaleVar, isSolid: false, 0x854D0EFF, 0xFEF3C7FF, scaleVar);
                    return true;
                }

                if (roll < 0.098f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x334155FF, 0x1E293BFF, scaleVar);
                    return true;
                }

                if (roll < 0.106f)
                {
                    prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.VolcanicAshland:
                // Scorched volcanic badlands: basalt columns, charred wood, obsidian, gold, and crystals
                if (roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.BasaltPillar, localX, localY, worldX, worldY, radius: 11f * scaleVar, isSolid: true, 0x09090BFF, 0x27272AFF, scaleVar);
                    return true;
                }

                if (roll < 0.050f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x18181BFF, 0x3F3F46FF, scaleVar);
                    return true;
                }

                if (roll < 0.065f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x27272AFF, 0x09090BFF, scaleVar);
                    return true;
                }

                if (roll < 0.073f)
                {
                    prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x18181BFF, 0xFACC15FF, scaleVar);
                    return true;
                }

                if (roll < 0.081f)
                {
                    prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 10f * scaleVar, isSolid: true, 0x7E22CEFF, 0xC084FCFF, scaleVar);
                    return true;
                }

                if (roll < 0.082f)
                {
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 15f, isSolid: true, 0xDC2626FF, 0x7F1D1DFF, 1.4f);
                    return true;
                }

                break;

            case BiomeType.RedMesa:
                // Arid canyon plateaus: red boulders, cacti, copper, iron, and gold veins
                if (roll < 0.025f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x7F1D1DFF, 0x991B1BFF, scaleVar);
                    return true;
                }

                if (roll < 0.035f)
                {
                    prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x166534FF, 0x15803DFF, scaleVar);
                    return true;
                }

                if (roll < 0.047f)
                {
                    prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                    return true;
                }

                if (roll < 0.058f)
                {
                    prop = MapProp.Create(MapPropType.IronVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x334155FF, 0xEA580CFF, scaleVar);
                    return true;
                }

                if (roll < 0.065f)
                {
                    prop = MapProp.Create(MapPropType.GoldVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x1E293BFF, 0xFACC15FF, scaleVar);
                    return true;
                }

                if (roll < 0.072f)
                {
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x52525BFF, 0x3F3F46FF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.TropicalJungle:
                // Thick rainforest: jungle palms, broadleaf trees, lush undergrowth, copper, and crystals
                if (roll < 0.050f)
                {
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 15f * scaleVar, isSolid: true, 0x059669FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.120f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 19f * scaleVar, isSolid: true, 0x047857FF, 0x451A03FF, scaleVar);
                    return true;
                }

                if (roll < 0.160f)
                {
                    prop = MapProp.Create(MapPropType.Bush, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: false, 0x059669FF, 0x10B981FF, scaleVar);
                    return true;
                }

                if (roll < 0.175f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x475569FF, 0x047857FF, scaleVar);
                    return true;
                }

                if (roll < 0.183f)
                {
                    prop = MapProp.Create(MapPropType.CopperVein, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x78350FFF, 0x2DD4BFFF, scaleVar);
                    return true;
                }

                if (roll < 0.188f)
                {
                    prop = MapProp.Create(MapPropType.CrystalCluster, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x0D9488FF, 0x2DD4BFFF, scaleVar);
                    return true;
                }

                break;

            default:
                break;
        }

        return false;
    }
}
