// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Separation;
using Carbon.Physics.Shapes;

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
    /// Assembles an active obstacle collision grid combining terrain and physical props
    /// across the specified local chunk radius.
    /// </summary>
    public TileObstacleGrid CreateObstacleGrid(Vector2 centerWorldPos, int chunkRadius = 2)
    {
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int centerChunkX = (int)MathF.Floor(centerWorldPos.X / chunkSizeWorld);
        int centerChunkY = (int)MathF.Floor(centerWorldPos.Y / chunkSizeWorld);

        int minChunkX = centerChunkX - chunkRadius;
        int minChunkY = centerChunkY - chunkRadius;
        int chunksAcross = (chunkRadius * 2) + 1;
        int tilesAcross = chunksAcross * Chunk2D.Size;

        TileObstacleGrid combined = new(tilesAcross, tilesAcross, _tileSize);

        for (int cy = 0; cy < chunksAcross; cy++)
        {
            int chunkY = minChunkY + cy;
            for (int cx = 0; cx < chunksAcross; cx++)
            {
                int chunkX = minChunkX + cx;
                Chunk2D chunk = _world.GetOrCreateChunk(chunkX, chunkY);

                int baseOffsetX = cx * Chunk2D.Size;
                int baseOffsetY = cy * Chunk2D.Size;

                for (int ly = 0; ly < Chunk2D.Size; ly++)
                {
                    for (int lx = 0; lx < Chunk2D.Size; lx++)
                    {
                        if (chunk.GetTile(lx, ly).IsSolid)
                        {
                            combined.SetSolid(baseOffsetX + lx, baseOffsetY + ly, true);
                        }
                    }
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp prop = chunk.Props[i];
                    if (prop.HasCollision)
                    {
                        int px = Math.Clamp(baseOffsetX + prop.LocalTileX, 0, tilesAcross - 1);
                        int py = Math.Clamp(baseOffsetY + prop.LocalTileY, 0, tilesAcross - 1);
                        combined.SetSolid(px, py, true);
                    }
                }
            }
        }

        return combined;
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

    /// <summary>
    /// Performs zero-allocation AABB collision resolution against terrain, cliffs, props, landmarks, and buildings.
    /// </summary>
    public bool ResolveAabb(ref Vector2 position, in AABB2D localBox, out Vector2 pushNormal)
    {
        pushNormal = Vector2.Zero;
        bool collided = false;

        AABB2D worldBox = localBox.Offset(position);
        int minTileX = (int)MathF.Floor(worldBox.Min.X / _tileSize);
        int maxTileX = (int)MathF.Floor(worldBox.Max.X / _tileSize);
        int minTileY = (int)MathF.Floor(worldBox.Min.Y / _tileSize);
        int maxTileY = (int)MathF.Floor(worldBox.Max.Y / _tileSize);

        Vector2 boxCenter = worldBox.Center;
        int currentTileX = (int)MathF.Floor(boxCenter.X / _tileSize);
        int currentTileY = (int)MathF.Floor(boxCenter.Y / _tileSize);

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
                AABB2D tileBox = new(new Vector2(tileMinX, tileMinY), new Vector2(tileMinX + _tileSize, tileMinY + _tileSize));
                if (EntitySeparation.ResolveStaticAabb(ref position, in localBox, in tileBox, ref pushNormal))
                {
                    collided = true;
                }
            }
        }

        worldBox = localBox.Offset(position);
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int minChunkX = (int)MathF.Floor((worldBox.Min.X - 48f) / chunkSizeWorld);
        int maxChunkX = (int)MathF.Floor((worldBox.Max.X + 48f) / chunkSizeWorld);
        int minChunkY = (int)MathF.Floor((worldBox.Min.Y - 48f) / chunkSizeWorld);
        int maxChunkY = (int)MathF.Floor((worldBox.Max.Y + 48f) / chunkSizeWorld);

        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (!_world.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk is null)
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

                    if (EntitySeparation.ResolveStaticAabbVsCircle(
                        ref position,
                        in localBox,
                        prop.WorldPosition,
                        prop.CollisionRadius,
                        ref pushNormal))
                    {
                        collided = true;
                    }
                }
            }
        }

        if (this.ResolveLandmarksAabb(ref position, in localBox, ref pushNormal))
        {
            collided = true;
        }

        if (_world.Buildings.ResolveAabbCollision(ref position, in localBox, _tileSize, out Vector2 buildingPushNormal))
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

    private bool ResolveLandmarksAabb(ref Vector2 position, in AABB2D localBox, ref Vector2 pushNormal)
    {
        AABB2D worldBox = localBox.Offset(position);
        int minSiteX = FloorDiv((int)MathF.Floor(worldBox.Min.X / _tileSize), PointOfInterestResolver.SiteSize);
        int maxSiteX = FloorDiv((int)MathF.Floor(worldBox.Max.X / _tileSize), PointOfInterestResolver.SiteSize);
        int minSiteY = FloorDiv((int)MathF.Floor(worldBox.Min.Y / _tileSize), PointOfInterestResolver.SiteSize);
        int maxSiteY = FloorDiv((int)MathF.Floor(worldBox.Max.Y / _tileSize), PointOfInterestResolver.SiteSize);

        bool collided = false;
        Span<TileBox> boxes = stackalloc TileBox[LandmarkPiece.MaxColliders];

        for (int sy = minSiteY; sy <= maxSiteY; sy++)
        {
            for (int sx = minSiteX; sx <= maxSiteX; sx++)
            {
                PointOfInterest site = _world.Generator.Landmarks.GetSite(sx, sy);
                if (site.Type == PointOfInterestType.None)
                {
                    continue;
                }

                ReadOnlySpan<LandmarkPiece> pieces = _world.Generator.Landmarks.GetLayout(sx, sy).Pieces;
                for (int i = 0; i < pieces.Length; i++)
                {
                    int n = pieces[i].GetColliders(boxes);
                    for (int k = 0; k < n; k++)
                    {
                        TileBox b = boxes[k];
                        AABB2D pieceBox = new(
                            new Vector2(b.MinX * _tileSize, b.MinY * _tileSize),
                            new Vector2(b.MaxX * _tileSize, b.MaxY * _tileSize));
                        if (EntitySeparation.ResolveStaticAabb(ref position, in localBox, in pieceBox, ref pushNormal))
                        {
                            collided = true;
                        }
                    }
                }
            }
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

    /// <summary>
    /// Casts a ray against solid terrain tiles, cliffs, constructed buildings, resident natural props, and landmark structures.
    /// Allocation-free on the hot path.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="hit">Output hit information if an obstacle was intersected.</param>
    /// <returns><see langword="true"/> if an obstacle was hit; otherwise, <see langword="false"/>.</returns>
    public bool Raycast(in Ray2D ray, out RaycastHit2D hit)
    {
        float maxDist = float.IsInfinity(ray.MaxDistance) ? (128f * _tileSize) : MathF.Min(ray.MaxDistance, 512f * _tileSize);
        if (maxDist <= 0f)
        {
            hit = RaycastHit2D.None;
            return false;
        }

        Ray2D boundedRay = new(ray.Origin, ray.Direction, maxDist);
        bool found = false;
        RaycastHit2D bestHit = RaycastHit2D.None;
        float bestDist = maxDist;

        // 1. 2D DDA traversal across tiles (terrain, cliff barriers, constructed buildings)
        int tx = (int)MathF.Floor(boundedRay.Origin.X / _tileSize);
        int ty = (int)MathF.Floor(boundedRay.Origin.Y / _tileSize);
        int prevTx = tx;
        int prevTy = ty;

        int stepX = boundedRay.Direction.X > 0f ? 1 : (boundedRay.Direction.X < 0f ? -1 : 0);
        int stepY = boundedRay.Direction.Y > 0f ? 1 : (boundedRay.Direction.Y < 0f ? -1 : 0);

        float tDeltaX = stepX != 0 ? _tileSize / MathF.Abs(boundedRay.Direction.X) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? _tileSize / MathF.Abs(boundedRay.Direction.Y) : float.PositiveInfinity;

        float nextBoundaryX = (tx + (stepX > 0 ? 1 : 0)) * _tileSize;
        float nextBoundaryY = (ty + (stepY > 0 ? 1 : 0)) * _tileSize;

        float tMaxX = stepX != 0 ? (nextBoundaryX - boundedRay.Origin.X) / boundedRay.Direction.X : float.PositiveInfinity;
        float tMaxY = stepY != 0 ? (nextBoundaryY - boundedRay.Origin.Y) / boundedRay.Direction.Y : float.PositiveInfinity;

        const int maxDdaSteps = 512;
        for (int step = 0; step < maxDdaSteps; step++)
        {
            bool blocked = _world.PeekTileAtWorld(tx, ty).IsSolid || _world.Buildings.IsSolidAt(tx, ty);
            if (!blocked && (tx != prevTx || ty != prevTy))
            {
                if (!_world.NavGrid.CanTraverse(prevTx, prevTy, tx, ty, includeLandmarks: false))
                {
                    blocked = true;
                }
            }

            if (blocked)
            {
                AABB2D tileBox = new(
                    new Vector2(tx * _tileSize, ty * _tileSize),
                    new Vector2((tx + 1) * _tileSize, (ty + 1) * _tileSize));
                if (Raycaster2D.IntersectAABB(in boundedRay, in tileBox, out RaycastHit2D tileHit) && tileHit.Distance <= bestDist)
                {
                    bestHit = tileHit;
                    bestDist = tileHit.Distance;
                    found = true;
                }

                break;
            }

            prevTx = tx;
            prevTy = ty;

            if (tMaxX < tMaxY)
            {
                if (tMaxX > bestDist)
                {
                    break;
                }

                tx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                if (tMaxY > bestDist)
                {
                    break;
                }

                ty += stepY;
                tMaxY += tDeltaY;
            }
        }

        // 2. Resident chunk solid props along the ray bounding box
        Vector2 rayEnd = boundedRay.GetPoint(bestDist);
        float minWorldX = MathF.Min(boundedRay.Origin.X, rayEnd.X) - 48f;
        float maxWorldX = MathF.Max(boundedRay.Origin.X, rayEnd.X) + 48f;
        float minWorldY = MathF.Min(boundedRay.Origin.Y, rayEnd.Y) - 48f;
        float maxWorldY = MathF.Max(boundedRay.Origin.Y, rayEnd.Y) + 48f;

        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int minChunkX = (int)MathF.Floor(minWorldX / chunkSizeWorld);
        int maxChunkX = (int)MathF.Floor(maxWorldX / chunkSizeWorld);
        int minChunkY = (int)MathF.Floor(minWorldY / chunkSizeWorld);
        int maxChunkY = (int)MathF.Floor(maxWorldY / chunkSizeWorld);

        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (!_world.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk is null)
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

                    Circle2D propCircle = new(prop.WorldPosition, prop.CollisionRadius);
                    if (Raycaster2D.IntersectCircle(in boundedRay, in propCircle, out RaycastHit2D propHit) && propHit.Distance < bestDist)
                    {
                        bestHit = propHit;
                        bestDist = propHit.Distance;
                        found = true;
                    }
                }
            }
        }

        // 3. Landmark piece colliders intersecting the ray bounding box
        int minSiteX = FloorDiv((int)MathF.Floor(minWorldX / _tileSize), PointOfInterestResolver.SiteSize);
        int maxSiteX = FloorDiv((int)MathF.Floor(maxWorldX / _tileSize), PointOfInterestResolver.SiteSize);
        int minSiteY = FloorDiv((int)MathF.Floor(minWorldY / _tileSize), PointOfInterestResolver.SiteSize);
        int maxSiteY = FloorDiv((int)MathF.Floor(maxWorldY / _tileSize), PointOfInterestResolver.SiteSize);
        Span<TileBox> boxes = stackalloc TileBox[LandmarkPiece.MaxColliders];

        for (int sy = minSiteY; sy <= maxSiteY; sy++)
        {
            for (int sx = minSiteX; sx <= maxSiteX; sx++)
            {
                PointOfInterest site = _world.Generator.Landmarks.GetSite(sx, sy);
                if (site.Type == PointOfInterestType.None)
                {
                    continue;
                }

                ReadOnlySpan<LandmarkPiece> pieces = _world.Generator.Landmarks.GetLayout(sx, sy).Pieces;
                for (int i = 0; i < pieces.Length; i++)
                {
                    int n = pieces[i].GetColliders(boxes);
                    for (int k = 0; k < n; k++)
                    {
                        TileBox b = boxes[k];
                        AABB2D box = new(
                            new Vector2(b.MinX * _tileSize, b.MinY * _tileSize),
                            new Vector2(b.MaxX * _tileSize, b.MaxY * _tileSize));
                        if (Raycaster2D.IntersectAABB(in boundedRay, in box, out RaycastHit2D pieceHit) && pieceHit.Distance < bestDist)
                        {
                            bestHit = pieceHit;
                            bestDist = pieceHit.Distance;
                            found = true;
                        }
                    }
                }
            }
        }

        hit = bestHit;
        return found;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
