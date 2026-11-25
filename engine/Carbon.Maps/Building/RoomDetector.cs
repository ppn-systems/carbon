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

    [ThreadStatic]
    private static EnclosureScratch? s_enclosureScratch;

    /// <summary>
    /// Zero-allocation check determining whether an interior starting tile is part of a bounded enclosed room.
    /// Uses reusable thread-local BFS queue and stamped open-addressing visited table.
    /// </summary>
    /// <param name="startTileX">Starting horizontal tile coordinate.</param>
    /// <param name="startTileY">Starting vertical tile coordinate.</param>
    /// <param name="isWallOrDoor">Predicate evaluating whether a tile is a room boundary wall or door.</param>
    /// <param name="maxRoomTiles">Maximum interior tile limit before declaring the region outdoors.</param>
    /// <returns><c>true</c> if the interior tile is enclosed within <paramref name="maxRoomTiles"/>; otherwise <c>false</c>.</returns>
    public static bool IsTileEnclosed(
        int startTileX,
        int startTileY,
        Func<int, int, bool> isWallOrDoor,
        int maxRoomTiles = DefaultMaxRoomTiles)
    {
        ArgumentNullException.ThrowIfNull(isWallOrDoor);

        if (maxRoomTiles <= 0 || isWallOrDoor(startTileX, startTileY))
        {
            return false;
        }

        EnclosureScratch scratch = s_enclosureScratch ??= new EnclosureScratch();
        scratch.Prepare(maxRoomTiles);

        int stamp = scratch.Stamp;
        long[] keys = scratch.TableKeys;
        int[] stamps = scratch.TableStamps;
        int[] queueX = scratch.QueueX;
        int[] queueY = scratch.QueueY;
        int tableMask = keys.Length - 1;

        int head = 0;
        int tail = 0;
        int roomCount = 0;

        _ = TryMarkVisited(startTileX, startTileY, keys, stamps, tableMask, stamp);
        queueX[tail] = startTileX;
        queueY[tail] = startTileY;
        tail++;

        while (head < tail)
        {
            int cx = queueX[head];
            int cy = queueY[head];
            head++;
            roomCount++;

            if (roomCount > maxRoomTiles)
            {
                return false;
            }

            if (!TryEnqueueNeighborZeroAlloc(cx, cy - 1, isWallOrDoor, keys, stamps, tableMask, stamp, queueX, queueY, ref tail, maxRoomTiles) ||
                !TryEnqueueNeighborZeroAlloc(cx + 1, cy, isWallOrDoor, keys, stamps, tableMask, stamp, queueX, queueY, ref tail, maxRoomTiles) ||
                !TryEnqueueNeighborZeroAlloc(cx, cy + 1, isWallOrDoor, keys, stamps, tableMask, stamp, queueX, queueY, ref tail, maxRoomTiles) ||
                !TryEnqueueNeighborZeroAlloc(cx - 1, cy, isWallOrDoor, keys, stamps, tableMask, stamp, queueX, queueY, ref tail, maxRoomTiles))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryEnqueueNeighborZeroAlloc(
        int nx,
        int ny,
        Func<int, int, bool> isWallOrDoor,
        long[] keys,
        int[] stamps,
        int tableMask,
        int stamp,
        int[] queueX,
        int[] queueY,
        ref int tail,
        int maxRoomTiles)
    {
        if (!TryMarkVisited(nx, ny, keys, stamps, tableMask, stamp))
        {
            return true;
        }

        if (isWallOrDoor(nx, ny))
        {
            return true;
        }

        if (tail > maxRoomTiles)
        {
            return false;
        }

        queueX[tail] = nx;
        queueY[tail] = ny;
        tail++;
        return true;
    }

    private static bool TryMarkVisited(int x, int y, long[] keys, int[] stamps, int mask, int stamp)
    {
        long packed = ((long)x << 32) | (uint)y;
        int slot = ((x * 73856093) ^ (y * 19349663)) & mask;

        while (true)
        {
            if (stamps[slot] != stamp)
            {
                stamps[slot] = stamp;
                keys[slot] = packed;
                return true;
            }

            if (keys[slot] == packed)
            {
                return false;
            }

            slot = (slot + 1) & mask;
        }
    }

    private sealed class EnclosureScratch
    {
        public int[] QueueX = [];
        public int[] QueueY = [];
        public long[] TableKeys = [];
        public int[] TableStamps = [];
        public int Stamp;

        public void Prepare(int maxRoomTiles)
        {
            int requiredQueue = maxRoomTiles + 2;
            if (this.QueueX.Length < requiredQueue)
            {
                this.QueueX = new int[requiredQueue];
                this.QueueY = new int[requiredQueue];
            }

            int requiredTable = NextPowerOfTwo((maxRoomTiles + 2) * 8);
            if (this.TableKeys.Length < requiredTable)
            {
                this.TableKeys = new long[requiredTable];
                this.TableStamps = new int[requiredTable];
                this.Stamp = 0;
            }

            if (this.Stamp == int.MaxValue)
            {
                Array.Clear(this.TableStamps);
                this.Stamp = 1;
            }
            else
            {
                this.Stamp++;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int capacity = 64;
            while (capacity < value)
            {
                capacity <<= 1;
            }

            return capacity;
        }
    }
}
