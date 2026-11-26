// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Primitives.Hashing;
using Carbon.Primitives.Random;

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
        out MapProp prop) =>
        TryResolveProp(
            worldTileX,
            worldTileY,
            localX,
            localY,
            biome,
            elevation,
            foliageDensity,
            mineralNoise,
            isNearWater,
            tileSize,
            seed,
            keepClear: false,
            zoneTier: 1,
            out prop);

    /// <summary>
    /// Evaluates whether a prop spawns at the given world tile coordinate, honoring road/cliff clearance
    /// and zone-tier mineral density scaling.
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
    /// <param name="keepClear">True when adjacent to a road, bridge, or cliff edge and props must stay clear.</param>
    /// <param name="zoneTier">MMORPG zone danger tier (1..3) scaling ore richness.</param>
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
        bool keepClear,
        byte zoneTier,
        out MapProp prop)
    {
        prop = default;
        if (keepClear)
        {
            return false;
        }

        // Aquatic biomes do not spawn standard land props
        if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
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

        BiomePropTable table = BiomePropTables.Get(biome);

        // 1. Mineral veins: over 96% of the land has no ore; veins concentrate in highlands and higher danger tiers.
        float veinThreshold = table.VeinThreshold;
        if (elevation >= 0.70f)
        {
            veinThreshold -= 0.04f;
        }
        else if (elevation >= 0.54f)
        {
            veinThreshold -= 0.02f;
        }

        if (zoneTier >= 3)
        {
            veinThreshold -= 0.05f;
        }
        else if (zoneTier == 2)
        {
            veinThreshold -= 0.02f;
        }

        // Anti-clump checkerboard keeps mineral outcrops spaced at >= 2 tiles with no colliding adjacent boulders.
        bool isOreSlot = ((worldTileX & 1) == 0) && ((worldTileY & 1) == 0);
        bool isVeinNode = !isNearWater && isOreSlot && mineralNoise >= veinThreshold &&
            (SpatialHash.Hash32(seed, worldTileX, worldTileY, 0x5E1AUL) % 3) == 0;

        // 2. Rare ancient shrines in secluded high-density groves (~1 in 200 tiles).
        if (!isNearWater && mineralNoise >= 0.86f && foliageDensity >= 0.76f &&
            (SpatialHash.Hash32(seed, worldTileX, worldTileY, 0x5A41UL) % 67) == 0)
        {
            prop = MapProp.Create(MapPropType.AncientShrine, localX, localY, worldX, worldY, radius: 34f * scaleVar, isSolid: true, 0x818CF8FF, 0x4338CAFF, 1.3f);
            return true;
        }

        if (isVeinNode)
        {
            int ore = BiomePropTable.Pick(table.Ores, roll, foliageDensity);
            if (ore >= 0)
            {
                prop = Create(table.Ores[ore], localX, localY, worldX, worldY, scaleVar);
                return true;
            }

            if (table.VeinExclusive)
            {
                return false;
            }
        }

        // Occasional non-vein field boulders & rock outcrops on plains, woods, and highlands (spaced on 2x2 ore slots).
        if (!isNearWater && isOreSlot && mineralNoise >= 0.44f &&
            (SpatialHash.Hash32(seed, worldTileX, worldTileY, 0xB01DUL) % 19) == 0)
        {
            prop = MapProp.Create(MapPropType.Boulder, localX, localY, worldX, worldY, radius: 21f * scaleVar, isSolid: true, 0x7A7E84FF, 0x9CA0A6FF, scaleVar);
            return true;
        }

        // 3. Staggered hexagonal lattice for trees (x + 2y ≡ 0 mod 5): at least sqrt(5) ~= 2.24 tiles between trunks,
        // keeping tree crowns distinct and exploration lanes open.
        int lattice = (worldTileX + (worldTileY * 2)) % 5;
        if (lattice < 0)
        {
            lattice += 5;
        }

        bool isTreeSlot = !isNearWater && lattice == 0;

        // 4. Vegetation layers, first matching condition wins.
        PropLayer[] layers = table.Layers;
        for (int i = 0; i < layers.Length; i++)
        {
            PropLayer layer = layers[i];
            if ((layer.TreeSlot && !isTreeSlot) || (layer.AwayFromWater && isNearWater) || foliageDensity < layer.MinFoliage)
            {
                continue;
            }

            int pick = BiomePropTable.Pick(layer.Rules, roll, foliageDensity);
            if (pick >= 0)
            {
                prop = Create(layer.Rules[pick], localX, localY, worldX, worldY, scaleVar);
                return true;
            }

            if (layer.Exclusive)
            {
                return false;
            }
        }

        return false;
    }

    private static MapProp Create(in PropRule rule, int localX, int localY, float worldX, float worldY, float scale) =>
        MapProp.Create(rule.Type, localX, localY, worldX, worldY, radius: rule.Radius * scale, rule.IsSolid, rule.Primary, rule.Secondary, scale);
}
