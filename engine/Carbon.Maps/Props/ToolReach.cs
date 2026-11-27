// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Maps.Props;

/// <summary>
/// How far a tool swing reaches, shared by the server's harvest check and the client's prediction so the client never
/// predicts a strike the server refuses: a tile is in reach when its centre is within <see cref="Px"/> world units of the
/// player's centre.
/// </summary>
public static class ToolReach
{
    /// <summary>Reach of a tool swing in world units, player centre to tile centre.</summary>
    public const float Px = 52f;

    /// <summary>Returns whether the centre of tile (<paramref name="tileX"/>, <paramref name="tileY"/>) is within reach.</summary>
    /// <param name="playerPos">The player's centre in world units.</param>
    /// <param name="tileX">World tile X.</param>
    /// <param name="tileY">World tile Y.</param>
    /// <param name="tileSize">World units per tile.</param>
    public static bool IsWithin(Vector2 playerPos, int tileX, int tileY, float tileSize)
    {
        Vector2 tileCentre = new((tileX + 0.5f) * tileSize, (tileY + 0.5f) * tileSize);
        return Vector2.DistanceSquared(playerPos, tileCentre) <= Px * Px;
    }
}
