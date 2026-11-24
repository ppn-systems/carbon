// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Maps.Building;

/// <summary>
/// Stateless domain service for detecting enclosed indoor rooms and roof coverage spans.
/// Employs bounded flood-fill to identify rooms and Chebyshev roof support radii (RimWorld 6-tile rule).
/// </summary>
public static class RoomDetector
{
    /// <summary>
    /// Default maximum tile threshold for a sealed indoor room before considering it outdoor open terrain.
    /// </summary>
    public const int DefaultMaxRoomTiles = 300;

    /// <summary>
    /// Default maximum distance a constructed wall or pillar can support overhead roof tiles.
    /// </summary>
    public const int DefaultMaxRoofSpan = 6;

    /// <summary>
    /// Detects whether an interior starting tile is part of an enclosed room bounded by walls and doors.
    /// </summary>
    /// <param name="startTileX">Starting horizontal tile coordinate.</param>
    /// <param name="startTileY">Starting vertical tile coordinate.</param>
    /// <param name="isWallOrDoor">Predicate evaluating whether a tile is a solid barrier / door frame.</param>
    /// <param name="maxRoomTiles">Maximum tile limit before declaring the area open wilderness.</param>
    /// <returns>A populated <see cref="RoomEnclosure"/> instance.</returns>
    public static RoomEnclosure DetectRoom(
        int startTileX,
        int startTileY,
        Func<int, int, bool> isWallOrDoor,
        int maxRoomTiles = DefaultMaxRoomTiles)
    {
        ArgumentNullException.ThrowIfNull(isWallOrDoor);

        if (isWallOrDoor(startTileX, startTileY))
        {
            return RoomEnclosure.Outdoors;
        }

        Queue<(int X, int Y)> queue = new();
        HashSet<(int X, int Y)> visited = new();
        List<(int X, int Y)> roomTiles = new();

        int minX = startTileX;
        int maxX = startTileX;
        int minY = startTileY;
        int maxY = startTileY;

        queue.Enqueue((startTileX, startTileY));
        _ = visited.Add((startTileX, startTileY));

        bool overflowed = false;

        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();

            roomTiles.Add((cx, cy));
            if (cx < minX)
            {
                minX = cx;
            }

            if (cx > maxX)
            {
                maxX = cx;
            }

            if (cy < minY)
            {
                minY = cy;
            }

            if (cy > maxY)
            {
                maxY = cy;
            }

            if (roomTiles.Count > maxRoomTiles)
            {
                overflowed = true;
                break;
            }

            // Expand 4 cardinal neighbors directly with zero allocations
            TryEnqueueNeighbor(cx, cy - 1, queue, visited, isWallOrDoor);
            TryEnqueueNeighbor(cx + 1, cy, queue, visited, isWallOrDoor);
            TryEnqueueNeighbor(cx, cy + 1, queue, visited, isWallOrDoor);
            TryEnqueueNeighbor(cx - 1, cy, queue, visited, isWallOrDoor);
        }

        if (overflowed)
        {
            return new RoomEnclosure(roomTiles, isEnclosed: false, minX, minY, maxX, maxY);
        }

        return new RoomEnclosure(roomTiles, isEnclosed: true, minX, minY, maxX, maxY);
    }

    private static void TryEnqueueNeighbor(
        int nx,
        int ny,
        Queue<(int X, int Y)> queue,
        HashSet<(int X, int Y)> visited,
        Func<int, int, bool> isWallOrDoor)
    {
        if (!visited.Add((nx, ny)))
        {
            return;
        }

        if (!isWallOrDoor(nx, ny))
        {
            queue.Enqueue((nx, ny));
        }
    }

    /// <summary>
    /// Evaluates whether an interior tile is within the roof support radius of at least one supporting wall or pillar.
    /// In RimWorld, roofs can extend up to 6 tiles away from any support pillar or wall.
    /// </summary>
    /// <param name="tileX">Horizontal tile coordinate.</param>
    /// <param name="tileY">Vertical tile coordinate.</param>
    /// <param name="isRoofSupport">Predicate evaluating whether a tile is a roof-supporting wall or pillar.</param>
    /// <param name="maxRoofSpan">Maximum distance in tiles.</param>
    /// <returns>True if supported by a nearby wall/pillar; otherwise false.</returns>
    public static bool IsTileRoofed(
        int tileX,
        int tileY,
        Func<int, int, bool> isRoofSupport,
        int maxRoofSpan = DefaultMaxRoofSpan)
    {
        ArgumentNullException.ThrowIfNull(isRoofSupport);

        for (int dy = -maxRoofSpan; dy <= maxRoofSpan; dy++)
        {
            for (int dx = -maxRoofSpan; dx <= maxRoofSpan; dx++)
            {
                if (isRoofSupport(tileX + dx, tileY + dy))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
