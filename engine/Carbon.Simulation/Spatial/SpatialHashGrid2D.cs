// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Physics.Shapes;

namespace Carbon.Simulation.Spatial;

/// <summary>
/// High-performance, zero-allocation 2D spatial hash grid for Area of Interest (AoI) and entity collision broadphase.
/// Partitions continuous 2D space into uniform square cells and supports multi-cell registration for large colliders.
/// </summary>
public sealed class SpatialHashGrid2D
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly Dictionary<ulong, List<int>> _cells = new(1024);
    private readonly Dictionary<int, (CellCoord Min, CellCoord Max)> _entityBounds = new(2048);
    private readonly Stack<List<int>> _bucketPool = new(256);

    /// <summary>
    /// Gets the dimension size of each cell in world units.
    /// </summary>
    public float CellSize => _cellSize;

    /// <summary>
    /// Gets the total number of non-empty cells currently managed in the grid.
    /// </summary>
    public int ActiveCellCount => _cells.Count;

    /// <summary>
    /// Gets the total number of tracked entities in the grid.
    /// </summary>
    public int EntityCount => _entityBounds.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialHashGrid2D"/> class.
    /// </summary>
    /// <param name="cellSize">Width and height of each spatial cell in world units (e.g. 32f or 64f).</param>
    public SpatialHashGrid2D(float cellSize = 32f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize, nameof(cellSize));

        _cellSize = cellSize;
        _invCellSize = 1f / cellSize;
    }

    /// <summary>
    /// Converts a continuous 2D world position into integer cell coordinates.
    /// </summary>
    /// <param name="position">Continuous world position.</param>
    /// <returns>Discrete cell coordinate.</returns>
    public CellCoord WorldToCell(Vector2 position)
    {
        int cx = (int)MathF.Floor(position.X * _invCellSize);
        int cy = (int)MathF.Floor(position.Y * _invCellSize);
        return new CellCoord(cx, cy);
    }

    /// <summary>
    /// Inserts an entity into the grid at the specified world position.
    /// </summary>
    /// <param name="entityId">Unique integer entity identifier.</param>
    /// <param name="position">World position.</param>
    public void Insert(int entityId, Vector2 position)
    {
        CellCoord coord = this.WorldToCell(position);
        _ = this.UpdateBounds(entityId, coord, coord);
    }

    /// <summary>
    /// Inserts an entity into all spatial grid cells overlapped by <paramref name="worldBounds"/>.
    /// </summary>
    /// <param name="entityId">Unique integer entity identifier.</param>
    /// <param name="worldBounds">World-space AABB bounds of the entity.</param>
    public void Insert(int entityId, in AABB2D worldBounds)
    {
        CellCoord min = this.WorldToCell(worldBounds.Min);
        CellCoord max = this.WorldToCell(worldBounds.Max);
        _ = this.UpdateBounds(entityId, min, max);
    }

    /// <summary>
    /// Updates an entity's cell assignment if it has crossed a cell boundary.
    /// </summary>
    /// <param name="entityId">Unique integer entity identifier.</param>
    /// <param name="newPosition">New continuous world position.</param>
    /// <returns><see langword="true"/> if the entity migrated to a different cell; otherwise, <see langword="false"/>.</returns>
    public bool Update(int entityId, Vector2 newPosition)
    {
        CellCoord newCoord = this.WorldToCell(newPosition);
        return this.UpdateBounds(entityId, newCoord, newCoord);
    }

    /// <summary>
    /// Updates an entity's multi-cell assignment when its world-space AABB moves across cell boundaries.
    /// </summary>
    /// <param name="entityId">Unique integer entity identifier.</param>
    /// <param name="worldBounds">Current world-space AABB bounds.</param>
    /// <returns><see langword="true"/> if any occupied cell changed; otherwise, <see langword="false"/>.</returns>
    public bool Update(int entityId, in AABB2D worldBounds)
    {
        CellCoord min = this.WorldToCell(worldBounds.Min);
        CellCoord max = this.WorldToCell(worldBounds.Max);
        return this.UpdateBounds(entityId, min, max);
    }

    /// <summary>
    /// Removes an entity from the grid.
    /// </summary>
    /// <param name="entityId">Unique integer entity identifier.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(int entityId)
    {
        if (_entityBounds.Remove(entityId, out (CellCoord Min, CellCoord Max) bounds))
        {
            for (int cy = bounds.Min.Y; cy <= bounds.Max.Y; cy++)
            {
                for (int cx = bounds.Min.X; cx <= bounds.Max.X; cx++)
                {
                    this.RemoveEntityFromCell(new CellCoord(cx, cy).Key, entityId);
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Queries all entities residing in the 9 adjacent cells (3x3 neighborhood) centered on the given cell.
    /// Hot-path zero allocation: outputs into user-provided <see cref="Span{T}"/>.
    /// </summary>
    /// <param name="centerCell">Center cell coordinate.</param>
    /// <param name="resultsBuffer">Output span to write found entity IDs.</param>
    /// <param name="count">Output total count of entities written.</param>
    public void QueryNeighbors(in CellCoord centerCell, Span<int> resultsBuffer, out int count) =>
        this.QueryCellRadius(in centerCell, cellRadius: 1, resultsBuffer, out count);

    /// <summary>
    /// Queries all entities residing within a Chebyshev cell radius around <paramref name="centerCell"/>.
    /// Hot-path zero allocation: outputs unique entity IDs into user-provided <see cref="Span{T}"/>.
    /// </summary>
    /// <param name="centerCell">Center cell coordinate.</param>
    /// <param name="cellRadius">Non-negative Chebyshev radius in cells (1 = 3x3 neighborhood).</param>
    /// <param name="resultsBuffer">Output span to write found entity IDs.</param>
    /// <param name="count">Output total count of entities written.</param>
    public void QueryCellRadius(in CellCoord centerCell, int cellRadius, Span<int> resultsBuffer, out int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellRadius);

        CellCoord minCell = new(centerCell.X - cellRadius, centerCell.Y - cellRadius);
        CellCoord maxCell = new(centerCell.X + cellRadius, centerCell.Y + cellRadius);
        this.QueryRegion(in minCell, in maxCell, resultsBuffer, out count);
    }

    /// <summary>
    /// Computes which entities entered or exited an observer's Area of Interest (AoI) when moving from
    /// <paramref name="previousCell"/> to <paramref name="currentCell"/>. Zero heap allocation.
    /// </summary>
    /// <param name="previousCell">Observer's previous cell coordinate.</param>
    /// <param name="currentCell">Observer's current cell coordinate.</param>
    /// <param name="cellRadius">AoI Chebyshev radius in cells.</param>
    /// <param name="enteredBuffer">Output buffer receiving entity IDs that newly entered the AoI.</param>
    /// <param name="enteredCount">Total number of entered entity IDs written.</param>
    /// <param name="exitedBuffer">Output buffer receiving entity IDs that left the AoI.</param>
    /// <param name="exitedCount">Total number of exited entity IDs written.</param>
    public void QueryAoiDiff(
        in CellCoord previousCell,
        in CellCoord currentCell,
        int cellRadius,
        Span<int> enteredBuffer,
        out int enteredCount,
        Span<int> exitedBuffer,
        out int exitedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellRadius);

        enteredCount = 0;
        exitedCount = 0;

        if (previousCell == currentCell)
        {
            return;
        }

        int maxEntered = enteredBuffer.Length;
        for (int dy = -cellRadius; dy <= cellRadius && enteredCount < maxEntered; dy++)
        {
            for (int dx = -cellRadius; dx <= cellRadius && enteredCount < maxEntered; dx++)
            {
                CellCoord cell = new(currentCell.X + dx, currentCell.Y + dy);
                if (cell.ChebyshevDistance(in previousCell) <= cellRadius)
                {
                    continue;
                }

                if (_cells.TryGetValue(cell.Key, out List<int>? bucket) && bucket is not null)
                {
                    int bucketCount = bucket.Count;
                    for (int i = 0; i < bucketCount && enteredCount < maxEntered; i++)
                    {
                        int id = bucket[i];
                        if (_entityBounds.TryGetValue(id, out (CellCoord Min, CellCoord Max) bounds) &&
                            bounds.Min != bounds.Max)
                        {
                            if (IntersectsChebyshevWindow(in bounds, in previousCell, cellRadius) ||
                                ContainsId(enteredBuffer[..enteredCount], id))
                            {
                                continue;
                            }
                        }

                        enteredBuffer[enteredCount++] = id;
                    }
                }
            }
        }

        int maxExited = exitedBuffer.Length;
        for (int dy = -cellRadius; dy <= cellRadius && exitedCount < maxExited; dy++)
        {
            for (int dx = -cellRadius; dx <= cellRadius && exitedCount < maxExited; dx++)
            {
                CellCoord cell = new(previousCell.X + dx, previousCell.Y + dy);
                if (cell.ChebyshevDistance(in currentCell) <= cellRadius)
                {
                    continue;
                }

                if (_cells.TryGetValue(cell.Key, out List<int>? bucket) && bucket is not null)
                {
                    int bucketCount = bucket.Count;
                    for (int i = 0; i < bucketCount && exitedCount < maxExited; i++)
                    {
                        int id = bucket[i];
                        if (_entityBounds.TryGetValue(id, out (CellCoord Min, CellCoord Max) bounds) &&
                            bounds.Min != bounds.Max)
                        {
                            if (IntersectsChebyshevWindow(in bounds, in currentCell, cellRadius) ||
                                ContainsId(exitedBuffer[..exitedCount], id))
                            {
                                continue;
                            }
                        }

                        exitedBuffer[exitedCount++] = id;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Queries all entities residing inside an inclusive rectangular range of spatial cells.
    /// Hot-path zero allocation: outputs unique entity IDs into user-provided <see cref="Span{T}"/>.
    /// </summary>
    /// <param name="minCell">Minimum inclusive cell coordinate.</param>
    /// <param name="maxCell">Maximum inclusive cell coordinate.</param>
    /// <param name="resultsBuffer">Output span to write found entity IDs.</param>
    /// <param name="count">Output total count of entities written.</param>
    public void QueryRegion(in CellCoord minCell, in CellCoord maxCell, Span<int> resultsBuffer, out int count)
    {
        count = 0;
        int maxCapacity = resultsBuffer.Length;

        for (int cy = minCell.Y; cy <= maxCell.Y; cy++)
        {
            for (int cx = minCell.X; cx <= maxCell.X; cx++)
            {
                CellCoord cell = new(cx, cy);
                if (_cells.TryGetValue(cell.Key, out List<int>? bucket) && bucket is not null)
                {
                    int bucketCount = bucket.Count;
                    for (int i = 0; i < bucketCount; i++)
                    {
                        int id = bucket[i];
                        if (_entityBounds.TryGetValue(id, out (CellCoord Min, CellCoord Max) bounds) &&
                            bounds.Min != bounds.Max)
                        {
                            // Emit a multi-cell entity only at its unique top-left intersection cell.
                            int firstX = Math.Max(bounds.Min.X, minCell.X);
                            int firstY = Math.Max(bounds.Min.Y, minCell.Y);
                            if (cx != firstX || cy != firstY)
                            {
                                continue;
                            }
                        }

                        if (count < maxCapacity)
                        {
                            resultsBuffer[count++] = id;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Queries entities whose spatial cells intersect a circular area.
    /// </summary>
    /// <param name="center">World circle center.</param>
    /// <param name="radius">Search radius in world units.</param>
    /// <param name="resultsBuffer">Output span to write found entity IDs.</param>
    /// <param name="count">Output total count of entities written.</param>
    public void QueryRadius(Vector2 center, float radius, Span<int> resultsBuffer, out int count)
    {
        CellCoord minCoord = this.WorldToCell(new Vector2(center.X - radius, center.Y - radius));
        CellCoord maxCoord = this.WorldToCell(new Vector2(center.X + radius, center.Y + radius));
        this.QueryRegion(in minCoord, in maxCoord, resultsBuffer, out count);
    }

    /// <summary>
    /// Clears all cells and entity mappings from the grid, recycling buckets back to the internal pool.
    /// </summary>
    public void Clear()
    {
        foreach (List<int> bucket in _cells.Values)
        {
            bucket.Clear();
            _bucketPool.Push(bucket);
        }

        _cells.Clear();
        _entityBounds.Clear();
    }

    private bool UpdateBounds(int entityId, CellCoord newMin, CellCoord newMax)
    {
        if (_entityBounds.TryGetValue(entityId, out (CellCoord Min, CellCoord Max) oldBounds))
        {
            if (oldBounds.Min == newMin && oldBounds.Max == newMax)
            {
                return false;
            }

            for (int cy = oldBounds.Min.Y; cy <= oldBounds.Max.Y; cy++)
            {
                for (int cx = oldBounds.Min.X; cx <= oldBounds.Max.X; cx++)
                {
                    if (!ContainsCell(newMin, newMax, cx, cy))
                    {
                        this.RemoveEntityFromCell(new CellCoord(cx, cy).Key, entityId);
                    }
                }
            }

            for (int cy = newMin.Y; cy <= newMax.Y; cy++)
            {
                for (int cx = newMin.X; cx <= newMax.X; cx++)
                {
                    if (!ContainsCell(oldBounds.Min, oldBounds.Max, cx, cy))
                    {
                        this.AddEntityToCell(new CellCoord(cx, cy).Key, entityId);
                    }
                }
            }
        }
        else
        {
            for (int cy = newMin.Y; cy <= newMax.Y; cy++)
            {
                for (int cx = newMin.X; cx <= newMax.X; cx++)
                {
                    this.AddEntityToCell(new CellCoord(cx, cy).Key, entityId);
                }
            }
        }

        _entityBounds[entityId] = (newMin, newMax);
        return true;
    }

    private static bool ContainsCell(in CellCoord min, in CellCoord max, int x, int y) =>
        x >= min.X && x <= max.X && y >= min.Y && y <= max.Y;

    private static bool IntersectsChebyshevWindow(in (CellCoord Min, CellCoord Max) bounds, in CellCoord center, int radius) =>
        bounds.Max.X >= center.X - radius &&
        bounds.Min.X <= center.X + radius &&
        bounds.Max.Y >= center.Y - radius &&
        bounds.Min.Y <= center.Y + radius;

    private static bool ContainsId(ReadOnlySpan<int> span, int id) => span.IndexOf(id) >= 0;

    private void AddEntityToCell(ulong key, int entityId)
    {
        if (!_cells.TryGetValue(key, out List<int>? bucket))
        {
            bucket = _bucketPool.Count > 0 ? _bucketPool.Pop() : new List<int>(16);
            _cells[key] = bucket;
        }

        bucket.Add(entityId);
    }

    private void RemoveEntityFromCell(ulong key, int entityId)
    {
        if (_cells.TryGetValue(key, out List<int>? bucket))
        {
            _ = bucket.Remove(entityId);
            if (bucket.Count == 0)
            {
                _ = _cells.Remove(key);
                bucket.Clear();
                _bucketPool.Push(bucket);
            }
        }
    }
}
