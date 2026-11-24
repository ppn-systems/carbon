// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Props;

namespace Carbon.Maps.Structures;

/// <summary>
/// Deterministically generates the starter village settlement, cobblestone roads,
/// fences, and central plaza around the world spawn point.
/// </summary>
public static class StarterVillageGenerator
{
    /// <summary>
    /// Radius in tiles defining the village settlement boundary.
    /// </summary>
    public const int VillageRadius = 15;

    /// <summary>
    /// Checks whether a world tile coordinate falls within the settlement footprint.
    /// </summary>
    public static bool IsWithinVillage(int worldTileX, int worldTileY, int spawnTileX, int spawnTileY)
    {
        int dx = Math.Abs(worldTileX - spawnTileX);
        int dy = Math.Abs(worldTileY - spawnTileY);
        return Math.Max(dx, dy) <= VillageRadius;
    }

    /// <summary>
    /// Evaluates if a settlement structure or road occupies the specified world coordinate.
    /// </summary>
    /// <param name="worldTileX">Tile X in world coordinates.</param>
    /// <param name="worldTileY">Tile Y in world coordinates.</param>
    /// <param name="localX">Local chunk X coordinate [0..31].</param>
    /// <param name="localY">Local chunk Y coordinate [0..31].</param>
    /// <param name="spawnTileX">World spawn tile X.</param>
    /// <param name="spawnTileY">World spawn tile Y.</param>
    /// <param name="tileSize">Physical dimension of each tile.</param>
    /// <param name="prop">The generated village structure prop.</param>
    /// <returns>True if a village structure exists at this coordinate; otherwise false.</returns>
    public static bool TryResolveVillageStructure(
        int worldTileX,
        int worldTileY,
        int localX,
        int localY,
        int spawnTileX,
        int spawnTileY,
        float tileSize,
        out MapProp prop)
    {
        prop = default;
        int dx = worldTileX - spawnTileX;
        int dy = worldTileY - spawnTileY;
        int absDx = Math.Abs(dx);
        int absDy = Math.Abs(dy);

        float centerX = (worldTileX * tileSize) + (tileSize * 0.5f);
        float centerY = (worldTileY * tileSize) + (tileSize * 0.5f);

        // 1. Center Town Well at (0, 0)
        if (dx == 0 && dy == 0)
        {
            prop = MapProp.Create(MapPropType.TownWell, localX, localY, centerX, centerY, radius: 10f, isSolid: true, 0x64748BFF, 0xB45309FF, scale: 1.2f);
            return true;
        }

        // 2. Village Campfire near plaza
        if (dx == 3 && dy == 3)
        {
            prop = MapProp.Create(MapPropType.Campfire, localX, localY, centerX, centerY, radius: 6f, isSolid: true, 0xF97316FF, 0x451A03FF, scale: 1.0f);
            return true;
        }

        // 3. Cardinal Highways & Cobblestone Roads
        bool isMainCrossRoad = (absDx == 0 && absDy <= VillageRadius + 12) || (absDy == 0 && absDx <= VillageRadius + 12);
        bool isPlazaCircle = Math.Max(absDx, absDy) == 5;

        if (isMainCrossRoad || isPlazaCircle)
        {
            prop = MapProp.Create(MapPropType.CobblestoneRoad, localX, localY, centerX, centerY, radius: 0f, isSolid: false, 0x94A3B8FF, 0x64748BFF, scale: 1.0f);
            return true;
        }

        // 4. Perimeter Wooden Palisade Fence (with road gates at cardinal axes)
        bool isPerimeterEdge = Math.Max(absDx, absDy) == VillageRadius;
        bool isGateOpening = absDx <= 1 || absDy <= 1;

        if (isPerimeterEdge && !isGateOpening)
        {
            prop = MapProp.Create(MapPropType.WoodenFence, localX, localY, centerX, centerY, radius: 6f, isSolid: true, 0x78350FFF, 0x92400EFF, scale: 1.0f);
            return true;
        }

        // 5. Village floral decoration near plaza
        if (absDx is >= 2 and <= 4 && absDy is >= 1 and <= 4 && (worldTileX ^ worldTileY) % 3 == 0)
        {
            prop = MapProp.Create(MapPropType.WildflowerPatch, localX, localY, centerX, centerY, radius: 0f, isSolid: false, 0x38BDF8FF, 0xFBBF24FF, scale: 0.9f);
            return true;
        }

        return false;
    }
}
