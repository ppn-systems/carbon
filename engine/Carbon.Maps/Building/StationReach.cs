// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Maps.Building;

/// <summary>
/// How close a player must be to use a crafting station, shared by the server's checks and the client's predictions so
/// they agree (audit DUP-3): a built station counts within <see cref="StructureTiles"/> tiles of the player's tile, an
/// NPC artisan within <see cref="NpcPx"/> world units.
/// </summary>
public static class StationReach
{
    /// <summary>Tiles from the player's tile, on either axis, at which a built station (anvil, workbench, stove) counts.</summary>
    public const int StructureTiles = 5;

    /// <summary>World units from the player at which a station NPC (forge artisan, shrine keeper) counts.</summary>
    public const float NpcPx = 175f;

    /// <summary>
    /// Returns whether an intact structure of type <paramref name="first"/> or <paramref name="second"/> stands within
    /// <paramref name="reachTiles"/> tiles of the tile under <paramref name="worldPos"/>.
    /// </summary>
    /// <param name="grid">The building grid to search.</param>
    /// <param name="worldPos">The player's position in world units.</param>
    /// <param name="tileSize">World units per tile.</param>
    /// <param name="first">A station type that counts.</param>
    /// <param name="second">Another station type that counts (pass <paramref name="first"/> again for one type).</param>
    /// <param name="reachTiles">Tiles on either axis; <see cref="StructureTiles"/> for crafting.</param>
    public static bool IsNearStructure(IReadOnlyBuildingGrid grid, Vector2 worldPos, float tileSize, StructureType first, StructureType second, int reachTiles)
    {
        ArgumentNullException.ThrowIfNull(grid);
        int ptx = (int)MathF.Floor(worldPos.X / tileSize);
        int pty = (int)MathF.Floor(worldPos.Y / tileSize);
        for (int dy = -reachTiles; dy <= reachTiles; dy++)
        {
            for (int dx = -reachTiles; dx <= reachTiles; dx++)
            {
                if (grid.TryGetStructure(ptx + dx, pty + dy, out StructureTile st) &&
                    (st.Type == first || st.Type == second) &&
                    !st.IsDestroyed)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
