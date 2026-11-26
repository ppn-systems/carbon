// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Data;

namespace Carbon.Maps.Building;

/// <summary>
/// Port giving <see cref="BuildingService"/> read access to terrain, natural props and live entities for placement rules.
/// </summary>
public interface IConstructionEnvironment
{
    /// <summary>Reads the terrain tile (biome, cliff flag, height level) without generating chunks.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns>The terrain tile.</returns>
    MapTile GetTerrain(int worldTileX, int worldTileY);

    /// <summary>Evaluates whether an intact solid natural prop (tree trunk, ore rock) occupies the tile.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns><c>true</c> when blocked.</returns>
    bool HasSolidProp(int worldTileX, int worldTileY);

    /// <summary>Evaluates whether a live entity (player, monster, NPC) stands on the tile.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns><c>true</c> when occupied.</returns>
    bool IsOccupiedByEntity(int worldTileX, int worldTileY);
}
