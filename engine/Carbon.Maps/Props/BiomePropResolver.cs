// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Random;

namespace Carbon.Maps.Props;

/// <summary>
/// Resolves and populates procedural foliage, trees, rocks, and environmental landmarks
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
                if (roll < 0.05f)
                {
                    // Oak tree: Green canopy, brown trunk
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x166534FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.08f)
                {
                    // Rock boulder
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x64748BFF, 0x334155FF, scaleVar);
                    return true;
                }

                if (roll < 0.16f)
                {
                    // Wildflower patch (walkable)
                    prop = MapProp.Create(MapPropType.WildflowerPatch, localX, localY, worldX, worldY, radius: 0f, isSolid: false, 0xF43F5EFF, 0xFBBF24FF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.AncientForest:
                if (roll < 0.18f)
                {
                    // Dense ancient oak tree
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 11f * scaleVar, isSolid: true, 0x14532DFF, 0x451A03FF, scaleVar);
                    return true;
                }

                if (roll < 0.28f)
                {
                    // Coniferous pine tree
                    prop = MapProp.Create(MapPropType.PineTree, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x064E3BFF, 0x713F12FF, scaleVar);
                    return true;
                }

                if (roll < 0.33f)
                {
                    // Mossy boulder
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x475569FF, 0x15803DFF, scaleVar);
                    return true;
                }

                if (roll < 0.335f)
                {
                    // Ancient stone shrine hidden in forest
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 13f, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
                    return true;
                }

                break;

            case BiomeType.FrozenTundra:
                if (roll < 0.12f)
                {
                    // Snow-dusted pine
                    prop = MapProp.Create(MapPropType.SnowPine, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0xE2E8F0FF, 0x0F766EFF, scaleVar);
                    return true;
                }

                if (roll < 0.18f)
                {
                    // Glacial boulder
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x94A3B8FF, 0x38BDF8FF, scaleVar);
                    return true;
                }

                if (roll < 0.184f)
                {
                    // Frozen obelisk shrine
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 13f, isSolid: true, 0x38BDF8FF, 0x0284C7FF, 1.3f);
                    return true;
                }

                break;

            case BiomeType.DuneDesert:
                if (roll < 0.08f)
                {
                    // Saguaro desert cactus
                    prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 6f * scaleVar, isSolid: true, 0x15803DFF, 0x166534FF, scaleVar);
                    return true;
                }

                if (roll < 0.12f)
                {
                    // Desert rock formation
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0xB45309FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.123f)
                {
                    // Sunken pyramid shrine
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 13f, isSolid: true, 0xF59E0BFF, 0xB45309FF, 1.4f);
                    return true;
                }

                break;

            case BiomeType.BeachSand:
                if (roll < 0.04f)
                {
                    // Coastal palm tree
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x10B981FF, 0x92400EFF, scaleVar);
                    return true;
                }

                if (roll < 0.06f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 5f * scaleVar, isSolid: true, 0xCBD5E1FF, 0x64748BFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.MistySwamp:
                if (roll < 0.14f)
                {
                    // Gnarled dead swamp tree
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x1E293BFF, 0x334155FF, scaleVar);
                    return true;
                }

                if (roll < 0.20f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 6f * scaleVar, isSolid: true, 0x334155FF, 0x1E293BFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.VolcanicAshland:
                if (roll < 0.12f)
                {
                    // Hexagonal basalt obsidian column
                    prop = MapProp.Create(MapPropType.BasaltPillar, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x09090BFF, 0x27272AFF, scaleVar);
                    return true;
                }

                if (roll < 0.17f)
                {
                    // Charred dead tree
                    prop = MapProp.Create(MapPropType.DeadTree, localX, localY, worldX, worldY, radius: 7f * scaleVar, isSolid: true, 0x18181BFF, 0xEF4444FF, scaleVar);
                    return true;
                }

                if (roll < 0.173f)
                {
                    // Volcano altar shrine
                    prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 14f, isSolid: true, 0xEF4444FF, 0x7F1D1DFF, 1.4f);
                    return true;
                }

                break;

            case BiomeType.RedMesa:
                if (roll < 0.10f)
                {
                    prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 9f * scaleVar, isSolid: true, 0x7F1D1DFF, 0x991B1BFF, scaleVar);
                    return true;
                }

                if (roll < 0.13f)
                {
                    prop = MapProp.Create(MapPropType.Cactus, localX, localY, worldX, worldY, radius: 6f * scaleVar, isSolid: true, 0x166534FF, 0x15803DFF, scaleVar);
                    return true;
                }

                break;

            case BiomeType.TropicalJungle:
                if (roll < 0.16f)
                {
                    prop = MapProp.Create(MapPropType.PalmTree, localX, localY, worldX, worldY, radius: 8f * scaleVar, isSolid: true, 0x059669FF, 0x78350FFF, scaleVar);
                    return true;
                }

                if (roll < 0.28f)
                {
                    prop = MapProp.Create(MapPropType.OakTree, localX, localY, worldX, worldY, radius: 10f * scaleVar, isSolid: true, 0x047857FF, 0x451A03FF, scaleVar);
                    return true;
                }

                if (roll < 0.38f)
                {
                    prop = MapProp.Create(MapPropType.WildflowerPatch, localX, localY, worldX, worldY, radius: 0f, isSolid: false, 0xEC4899FF, 0x10B981FF, scaleVar);
                    return true;
                }

                break;

            default:
                break;
        }

        return false;
    }
}
