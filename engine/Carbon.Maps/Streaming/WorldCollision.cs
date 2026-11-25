// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;

namespace Carbon.Maps.Streaming;

/// <summary>
/// Sliding circle-vs-world collision: solid tiles and cliff edges, natural prop trunks, point-of-interest
/// footprints and constructed buildings. Reads the world without generating chunks.
/// </summary>
internal sealed class WorldCollision
{
    private readonly ChunkWorld2D _world;
    private readonly float _tileSize;

    /// <summary>Initializes a new instance of the <see cref="WorldCollision"/> class.</summary>
    /// <param name="world">The world to collide against.</param>
    public WorldCollision(ChunkWorld2D world)
    {
        _world = world;
        _tileSize = world.TileSize;
    }

    /// <summary>
    /// Performs zero-allocation continuous spatial collision resolution for an entity against terrain and physical obstacles.
    /// Follows MMORPG sliding physics: prevents stepping on tree trunks or entering water/ores while allowing
    /// free passage underneath leafy canopies.
    /// </summary>
    /// <param name="position">Entity position in world coordinates, modified in-place to slide around obstacles.</param>
    /// <param name="radius">Entity collision radius.</param>
    /// <param name="pushNormal">Combined surface push normal if a collision occurred; otherwise Vector2.Zero.</param>
    /// <returns>True if a collision was resolved; otherwise false.</returns>
    public bool Resolve(ref Vector2 position, float radius, out Vector2 pushNormal)
    {
        pushNormal = Vector2.Zero;
        bool collided = false;

        // 1. Terrain collision against solid tiles (rivers, ocean, cliffs)
        int minTileX = (int)MathF.Floor((position.X - radius) / _tileSize);
        int maxTileX = (int)MathF.Floor((position.X + radius) / _tileSize);
        int minTileY = (int)MathF.Floor((position.Y - radius) / _tileSize);
        int maxTileY = (int)MathF.Floor((position.Y + radius) / _tileSize);

        int currentTileX = (int)MathF.Floor(position.X / _tileSize);
        int currentTileY = (int)MathF.Floor(position.Y / _tileSize);

        for (int ty = minTileY; ty <= maxTileY; ty++)
        {
            for (int tx = minTileX; tx <= maxTileX; tx++)
            {
                MapTile tile = _world.PeekTileAtWorld(tx, ty);
                bool isImpassable = tile.IsSolid;

                if (!isImpassable && (tx != currentTileX || ty != currentTileY))
                {
                    if (!_world.NavGrid.CanTraverse(currentTileX, currentTileY, tx, ty, includeLandmarks: false))
                    {
                        isImpassable = true;
                    }
                }

                if (!isImpassable)
                {
                    continue;
                }

                float tileMinX = tx * _tileSize;
                float tileMinY = ty * _tileSize;
                float tileMaxX = tileMinX + _tileSize;
                float tileMaxY = tileMinY + _tileSize;

                float closestX = Math.Clamp(position.X, tileMinX, tileMaxX);
                float closestY = Math.Clamp(position.Y, tileMinY, tileMaxY);
                Vector2 diff = position - new Vector2(closestX, closestY);
                float distSq = diff.LengthSquared();

                if (distSq < radius * radius)
                {
                    collided = true;
                    if (distSq > 0.0001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        Vector2 normal = diff / dist;
                        float penetration = radius - dist;
                        position += normal * penetration;
                        pushNormal += normal;
                    }
                    else
                    {
                        Vector2 normal = new(0f, -1f);
                        position += normal * radius;
                        pushNormal += normal;
                    }
                }
            }
        }

        // 2. Physical prop collision (tree trunks, mineral ore boulders, walls)
        // Free passage is permitted underneath tree canopies; collision applies only against the trunk footprint.
        float searchRadius = radius + 48f;
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int minChunkX = (int)MathF.Floor((position.X - searchRadius) / chunkSizeWorld);
        int maxChunkX = (int)MathF.Floor((position.X + searchRadius) / chunkSizeWorld);
        int minChunkY = (int)MathF.Floor((position.Y - searchRadius) / chunkSizeWorld);
        int maxChunkY = (int)MathF.Floor((position.Y + searchRadius) / chunkSizeWorld);

        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (!_world.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk == null)
                {
                    continue;
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp prop = chunk.Props[i];
                    if (!prop.HasCollision)
                    {
                        continue;
                    }

                    Vector2 propPos = prop.WorldPosition;
                    Vector2 diff = position - propPos;
                    float minDist = prop.CollisionRadius + radius;
                    float distSq = diff.LengthSquared();

                    if (distSq < minDist * minDist)
                    {
                        collided = true;
                        if (distSq > 0.0001f)
                        {
                            float dist = MathF.Sqrt(distSq);
                            Vector2 normal = diff / dist;
                            float penetration = minDist - dist;
                            position += normal * penetration;
                            pushNormal += normal;
                        }
                        else
                        {
                            Vector2 normal = new(1f, 0f);
                            position += normal * minDist;
                            pushNormal += normal;
                        }
                    }
                }
            }
        }

        // 3. Points of interest: houses, wells, tents, gate posts... (footprints in tile units)
        if (this.ResolveLandmarks(ref position, radius, ref pushNormal))
        {
            collided = true;
        }

        // 4. Resolve continuous collision against constructed building structures (walls, closed doors, pillars)
        if (_world.Buildings.ResolveCollision(ref position, radius, _tileSize, out Vector2 buildingPushNormal))
        {
            collided = true;
            pushNormal += buildingPushNormal;
        }

        if (pushNormal.LengthSquared() > 0.0001f)
        {
            pushNormal = Vector2.Normalize(pushNormal);
        }

        return collided;
    }

    private bool ResolveLandmarks(ref Vector2 position, float radius, ref Vector2 pushNormal)
    {
        int tileX = (int)MathF.Floor(position.X / _tileSize);
        int tileY = (int)MathF.Floor(position.Y / _tileSize);
        int siteX = FloorDiv(tileX, PointOfInterestResolver.SiteSize);
        int siteY = FloorDiv(tileY, PointOfInterestResolver.SiteSize);
        PointOfInterest site = _world.Generator.Landmarks.GetSite(siteX, siteY);
        if (site.Type == PointOfInterestType.None ||
            Math.Abs(site.TileX - tileX) > site.Radius + 6 ||
            Math.Abs(site.TileY - tileY) > site.Radius + 6)
        {
            return false;
        }

        bool collided = false;
        Span<TileBox> boxes = stackalloc TileBox[LandmarkPiece.MaxColliders];
        ReadOnlySpan<LandmarkPiece> pieces = _world.Generator.Landmarks.GetLayout(siteX, siteY).Pieces;
        for (int i = 0; i < pieces.Length; i++)
        {
            int n = pieces[i].GetColliders(boxes);
            for (int k = 0; k < n; k++)
            {
                TileBox b = boxes[k];
                float minX = b.MinX * _tileSize;
                float minY = b.MinY * _tileSize;
                float maxX = b.MaxX * _tileSize;
                float maxY = b.MaxY * _tileSize;
                Vector2 closest = new(Math.Clamp(position.X, minX, maxX), Math.Clamp(position.Y, minY, maxY));
                Vector2 diff = position - closest;
                float distSq = diff.LengthSquared();
                if (distSq >= radius * radius)
                {
                    continue;
                }

                collided = true;
                Vector2 normal;
                float push;
                if (distSq > 0.0001f)
                {
                    float dist = MathF.Sqrt(distSq);
                    normal = diff / dist;
                    push = radius - dist;
                }
                else
                {
                    // Centre inside the box: leave through the nearest side.
                    float left = position.X - minX;
                    float right = maxX - position.X;
                    float top = position.Y - minY;
                    float bottom = maxY - position.Y;
                    float m = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                    normal = m == bottom ? new Vector2(0f, 1f) : m == top ? new Vector2(0f, -1f) : m == left ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
                    push = m + radius;
                }

                position += normal * push;
                pushNormal += normal;
            }
        }

        return collided;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
