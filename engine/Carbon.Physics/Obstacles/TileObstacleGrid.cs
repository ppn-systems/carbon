// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;

namespace Carbon.Physics.Obstacles;

/// <summary>
/// High-performance uniform 2D tile obstacle grid for static terrain collision, wall sliding, and Line-of-Sight raycasting.
/// Zero heap allocation on hot-path collision queries.
/// </summary>
public sealed class TileObstacleGrid
{
    private readonly int _width;
    private readonly int _height;
    private readonly float _tileSize;
    private readonly float _invTileSize;
    private readonly byte[] _solidTiles;

    /// <summary>
    /// Gets the horizontal grid width in tiles.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the vertical grid height in tiles.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the physical width and height of each tile in world units.
    /// </summary>
    public float TileSize => _tileSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="TileObstacleGrid"/> class.
    /// </summary>
    /// <param name="width">Grid width in tiles.</param>
    /// <param name="height">Grid height in tiles.</param>
    /// <param name="tileSize">Dimension of each square tile in world units (e.g. 16f, 32f).</param>
    public TileObstacleGrid(int width, int height, float tileSize = 32f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize, nameof(tileSize));

        _width = width;
        _height = height;
        _tileSize = tileSize;
        _invTileSize = 1f / tileSize;
        _solidTiles = new byte[width * height];
    }

    /// <summary>
    /// Marks a tile cell as solid (obstacle) or passable.
    /// </summary>
    public void SetSolid(int x, int y, bool isSolid)
    {
        if (this.IsInBounds(x, y))
        {
            _solidTiles[(y * _width) + x] = (byte)(isSolid ? 1 : 0);
        }
    }

    /// <summary>
    /// Determines whether the specified tile coordinate is solid.
    /// Out-of-bounds tiles are treated as solid boundaries.
    /// </summary>
    public bool IsSolid(int x, int y)
    {
        if (!this.IsInBounds(x, y))
        {
            return true;
        }

        return _solidTiles[(y * _width) + x] != 0;
    }

    /// <summary>
    /// Determines whether tile coordinates are within the grid bounds.
    /// </summary>
    public bool IsInBounds(int x, int y) => x >= 0 && x < _width && y >= 0 && y < _height;

    /// <summary>
    /// Converts a world coordinate position into discrete tile grid coordinates.
    /// </summary>
    public void WorldToTile(Vector2 worldPos, out int tileX, out int tileY)
    {
        tileX = (int)MathF.Floor(worldPos.X * _invTileSize);
        tileY = (int)MathF.Floor(worldPos.Y * _invTileSize);
    }

    /// <summary>
    /// Computes the world-space <see cref="AABB2D"/> bounding box of a tile.
    /// </summary>
    public AABB2D TileToAABB(int tileX, int tileY)
    {
        float minX = tileX * _tileSize;
        float minY = tileY * _tileSize;
        return new AABB2D(new Vector2(minX, minY), new Vector2(minX + _tileSize, minY + _tileSize));
    }

    /// <summary>
    /// Resolves circular entity collision against adjacent solid obstacle tiles, pushing the circle outward to allow smooth wall sliding.
    /// </summary>
    /// <param name="circlePosition">World position of the circle to adjust.</param>
    /// <param name="radius">Collision radius of the entity.</param>
    /// <returns><see langword="true"/> if any obstacle collision was resolved; otherwise, <see langword="false"/>.</returns>
    public bool ResolveCircle(ref Vector2 circlePosition, float radius) =>
        this.ResolveCircle(ref circlePosition, radius, out _);

    /// <summary>
    /// Resolves circular entity collision against adjacent solid obstacle tiles, outputting the composite push direction.
    /// </summary>
    /// <param name="circlePosition">World position of the circle to adjust.</param>
    /// <param name="radius">Collision radius of the entity.</param>
    /// <param name="pushNormal">Output normalized pushout direction.</param>
    /// <returns><see langword="true"/> if any obstacle collision was resolved; otherwise, <see langword="false"/>.</returns>
    public bool ResolveCircle(ref Vector2 circlePosition, float radius, out Vector2 pushNormal)
    {
        int minX = (int)MathF.Floor((circlePosition.X - radius) * _invTileSize);
        int maxX = (int)MathF.Floor((circlePosition.X + radius) * _invTileSize);
        int minY = (int)MathF.Floor((circlePosition.Y - radius) * _invTileSize);
        int maxY = (int)MathF.Floor((circlePosition.Y + radius) * _invTileSize);

        bool resolvedAny = false;
        pushNormal = Vector2.Zero;

        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                if (!this.IsSolid(tx, ty))
                {
                    continue;
                }

                AABB2D tileBox = this.TileToAABB(tx, ty);

                // Find closest point on the solid tile AABB to circle center
                float closestX = Math.Clamp(circlePosition.X, tileBox.Min.X, tileBox.Max.X);
                float closestY = Math.Clamp(circlePosition.Y, tileBox.Min.Y, tileBox.Max.Y);

                Vector2 diff = circlePosition - new Vector2(closestX, closestY);
                float distSq = diff.LengthSquared();

                Vector2 pushDir;
                float penetration;

                if (distSq > 1e-6f)
                {
                    if (distSq < radius * radius)
                    {
                        float dist = MathF.Sqrt(distSq);
                        pushDir = diff / dist;
                        penetration = radius - dist;

                        circlePosition += pushDir * penetration;
                        pushNormal += pushDir;
                        resolvedAny = true;
                    }
                }
                else
                {
                    // Circle center is inside the solid tile box: push out through closest face
                    float dLeft = circlePosition.X - tileBox.Min.X;
                    float dRight = tileBox.Max.X - circlePosition.X;
                    float dBottom = circlePosition.Y - tileBox.Min.Y;
                    float dTop = tileBox.Max.Y - circlePosition.Y;

                    float minD = MathF.Min(MathF.Min(dLeft, dRight), MathF.Min(dBottom, dTop));
                    if (minD == dLeft)
                    {
                        pushDir = -Vector2.UnitX;
                        penetration = dLeft + radius;
                    }
                    else if (minD == dRight)
                    {
                        pushDir = Vector2.UnitX;
                        penetration = dRight + radius;
                    }
                    else if (minD == dBottom)
                    {
                        pushDir = -Vector2.UnitY;
                        penetration = dBottom + radius;
                    }
                    else
                    {
                        pushDir = Vector2.UnitY;
                        penetration = dTop + radius;
                    }

                    circlePosition += pushDir * penetration;
                    pushNormal += pushDir;
                    resolvedAny = true;
                }
            }
        }

        if (resolvedAny && pushNormal.LengthSquared() > 1e-6f)
        {
            pushNormal = Vector2.Normalize(pushNormal);
        }

        return resolvedAny;
    }

    /// <summary>
    /// Performs a raycast against the grid's solid obstacle tiles, finding the earliest hit point.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="hit">Output hit information.</param>
    /// <returns><see langword="true"/> if an obstacle tile was intersected; otherwise, <see langword="false"/>.</returns>
    public bool Raycast(in Ray2D ray, out RaycastHit2D hit)
    {
        hit = RaycastHit2D.None;
        float closestDistance = float.PositiveInfinity;
        bool foundHit = false;

        // Bounding box of the ray segment
        Vector2 start = ray.Origin;
        float testDist = float.IsInfinity(ray.MaxDistance) ? (MathF.Max(_width, _height) * _tileSize) : ray.MaxDistance;
        Vector2 end = ray.GetPoint(testDist);

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(start.X, end.X) * _invTileSize));
        int maxX = Math.Min(_width - 1, (int)MathF.Floor(MathF.Max(start.X, end.X) * _invTileSize));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(start.Y, end.Y) * _invTileSize));
        int maxY = Math.Min(_height - 1, (int)MathF.Floor(MathF.Max(start.Y, end.Y) * _invTileSize));

        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                if (!_solidTiles[(ty * _width) + tx].Equals(0))
                {
                    AABB2D box = this.TileToAABB(tx, ty);
                    if (Raycaster2D.IntersectAABB(ray, box, out RaycastHit2D tileHit))
                    {
                        if (tileHit.Distance < closestDistance)
                        {
                            closestDistance = tileHit.Distance;
                            hit = tileHit;
                            foundHit = true;
                        }
                    }
                }
            }
        }

        return foundHit;
    }
}
