// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Carbon.Simulation.Spatial;

/// <summary>
/// High-performance, zero-allocation 2D spatial hash grid for Area of Interest (AoI) and entity collision broadphase.
/// Partitions continuous 2D space into uniform square cells for O(1) proximity queries.
/// </summary>
public sealed class SpatialHashGrid2D
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly Dictionary<ulong, List<int>> _cells = new(1024);
    private readonly Dictionary<int, CellCoord> _entityLocations = new(2048);
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
    public int EntityCount => _entityLocations.Count;

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
        _entityLocations[entityId] = coord;
        this.AddEntityToCell(coord.Key, entityId);
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

        if (_entityLocations.TryGetValue(entityId, out CellCoord oldCoord))
        {
            if (oldCoord == newCoord)
            {
                return false;
            }

            this.RemoveEntityFromCell(oldCoord.Key, entityId);
        }

        _entityLocations[entityId] = newCoord;
        this.AddEntityToCell(newCoord.Key, entityId);
        return true;
    }

    /// <summary>
    /// Removes an entity from the grid.
    /// </summary>
    /// <param name="entityId">Unique integer entity identifier.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(int entityId)
    {
        if (_entityLocations.Remove(entityId, out CellCoord coord))
        {
            this.RemoveEntityFromCell(coord.Key, entityId);
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
    public void QueryNeighbors(in CellCoord centerCell, Span<int> resultsBuffer, out int count)
    {
        count = 0;
        int maxCapacity = resultsBuffer.Length;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                CellCoord neighbor = new(centerCell.X + dx, centerCell.Y + dy);
                if (_cells.TryGetValue(neighbor.Key, out List<int>? bucket) && bucket != null)
                {
                    int bucketCount = bucket.Count;
                    for (int i = 0; i < bucketCount; i++)
                    {
                        if (count < maxCapacity)
                        {
                            resultsBuffer[count++] = bucket[i];
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
        count = 0;
        int maxCapacity = resultsBuffer.Length;

        CellCoord minCoord = this.WorldToCell(new Vector2(center.X - radius, center.Y - radius));
        CellCoord maxCoord = this.WorldToCell(new Vector2(center.X + radius, center.Y + radius));

        for (int cy = minCoord.Y; cy <= maxCoord.Y; cy++)
        {
            for (int cx = minCoord.X; cx <= maxCoord.X; cx++)
            {
                CellCoord cell = new(cx, cy);
                if (_cells.TryGetValue(cell.Key, out List<int>? bucket) && bucket != null)
                {
                    int bucketCount = bucket.Count;
                    for (int i = 0; i < bucketCount; i++)
                    {
                        if (count < maxCapacity)
                        {
                            resultsBuffer[count++] = bucket[i];
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
        _entityLocations.Clear();
    }

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
