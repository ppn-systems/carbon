// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Navigation;

namespace Carbon.AI.Navigation;

/// <summary>
/// Reusable tile-path waypoint buffer connecting <see cref="GridPathfinder"/> output to continuous world-space steering.
/// </summary>
public sealed class PathWaypointBuffer
{
    private readonly (int X, int Y)[] _waypoints;
    private int _count;
    private int _cursor;

    /// <summary>
    /// Gets the number of waypoints remaining from the current cursor to the goal.
    /// </summary>
    public int RemainingWaypoints => _count - _cursor;

    /// <summary>
    /// Gets a value indicating whether an active path with remaining waypoints is stored.
    /// </summary>
    public bool HasPath => _cursor < _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathWaypointBuffer"/> class.
    /// </summary>
    /// <param name="capacity">Maximum number of tile waypoints stored per path.</param>
    public PathWaypointBuffer(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _waypoints = new (int X, int Y)[capacity];
    }

    /// <summary>
    /// Computes and stores a tile path between <paramref name="fromWorld"/> and <paramref name="toWorld"/>.
    /// </summary>
    /// <param name="pathfinder">Reusable A* pathfinder.</param>
    /// <param name="grid">Navigation walkability grid.</param>
    /// <param name="fromWorld">Starting world position.</param>
    /// <param name="toWorld">Destination world position.</param>
    /// <param name="tileSize">World units per tile.</param>
    /// <param name="maxExpandedNodes">Optional node expansion budget.</param>
    /// <returns>The path search status.</returns>
    public PathSearchStatus TryComputePath(
        GridPathfinder pathfinder,
        ITileWalkabilityGrid grid,
        Vector2 fromWorld,
        Vector2 toWorld,
        float tileSize,
        int? maxExpandedNodes = null)
    {
        ArgumentNullException.ThrowIfNull(pathfinder);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);

        (int X, int Y) start = ((int)MathF.Floor(fromWorld.X / tileSize), (int)MathF.Floor(fromWorld.Y / tileSize));
        (int X, int Y) goal = ((int)MathF.Floor(toWorld.X / tileSize), (int)MathF.Floor(toWorld.Y / tileSize));

        PathSearchStatus status = pathfinder.FindPath(grid, start, goal, _waypoints, out int length, maxExpandedNodes);
        if (status == PathSearchStatus.Found)
        {
            _count = length;
            _cursor = length > 1 ? 1 : 0;
        }
        else
        {
            _count = 0;
            _cursor = 0;
        }

        return status;
    }

    /// <summary>
    /// Advances past reached waypoints and computes a normalized steering direction toward the current waypoint center.
    /// </summary>
    /// <param name="currentWorldPos">Actor's current world position.</param>
    /// <param name="tileSize">World units per tile.</param>
    /// <param name="arrivalRadius">Distance within which a waypoint is considered reached.</param>
    /// <param name="direction">Normalized world steering direction if a waypoint remains.</param>
    /// <returns><see langword="true"/> if steering toward a remaining waypoint; <see langword="false"/> if path complete.</returns>
    public bool TryGetSteeringDirection(
        Vector2 currentWorldPos,
        float tileSize,
        float arrivalRadius,
        out Vector2 direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentOutOfRangeException.ThrowIfNegative(arrivalRadius);

        float arrivalSq = arrivalRadius * arrivalRadius;

        while (_cursor < _count)
        {
            (int wx, int wy) = _waypoints[_cursor];
            Vector2 targetCenter = new((wx + 0.5f) * tileSize, (wy + 0.5f) * tileSize);
            Vector2 delta = targetCenter - currentWorldPos;
            float distSq = delta.LengthSquared();

            if (distSq <= arrivalSq)
            {
                _cursor++;
                continue;
            }

            direction = delta / MathF.Sqrt(distSq);
            return true;
        }

        direction = Vector2.Zero;
        return false;
    }

    /// <summary>
    /// Clears any stored path waypoints.
    /// </summary>
    public void Clear()
    {
        _count = 0;
        _cursor = 0;
    }
}
