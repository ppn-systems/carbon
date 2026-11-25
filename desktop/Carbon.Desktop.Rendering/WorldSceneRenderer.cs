// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Renders the full 2.5D top-down Carbon world scene: cached terrain VBO chunks, animated water/ripples,
/// Y-sorted ground props, buildings/landmarks, local &amp; remote players, swaying canopies,
/// dynamic day/night point lighting, weather overlays, and smooth interior room masking.
/// </summary>
public sealed class WorldSceneRenderer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly GeometryBatch _geometryBatch;
    private readonly ChunkTerrainRenderer _terrainRenderer;
    private readonly Atmosphere _atmosphere;
    private readonly List<PointLight> _lights = new(64);
    private readonly List<GroundRenderItem> _groundSortList = new(2048);
    private readonly List<CanopyRenderItem> _canopySortList = new(1024);
    private readonly List<PointOfInterest> _visibleWorldTrees = new(4);

    private PointOfInterestResolver? _landmarks;
    private PointOfInterestRenderer? _poiRenderer;
    private ulong _boundSeed;
    private float _animTime;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldSceneRenderer"/> class.
    /// </summary>
    /// <param name="device">Active graphics device.</param>
    /// <param name="options">Client rendering/atmosphere options.</param>
    public WorldSceneRenderer(IGraphicsDevice device, DesktopClientOptions options)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(options);

        _geometryBatch = new GeometryBatch(device);
        _terrainRenderer = new ChunkTerrainRenderer(device);
        _atmosphere = new Atmosphere(options.StartHour, options.DayLengthSeconds, options.WeatherRainOverride);
    }

    /// <summary>Gets or sets a value indicating whether solid collision debug overlays are rendered.</summary>
    public bool ShowDebugOverlay { get; set; }

    /// <summary>
    /// Ensures deterministic landmark metadata matches the active world seed, resetting cached chunk meshes if changed.
    /// </summary>
    /// <param name="session">Active client world session.</param>
    public void EnsureWorldBound(IClientWorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_poiRenderer is null || _boundSeed != session.WorldSeed)
        {
            ProceduralWorldGenerator generator = new(new WorldGenSettings(session.WorldSeed, session.ChunkWorld.TileSize));
            _landmarks = generator.Landmarks;
            _poiRenderer = new PointOfInterestRenderer(session.ChunkWorld.TileSize, _landmarks);
            _boundSeed = session.WorldSeed;
            _terrainRenderer.InvalidateAll();
        }
    }

    /// <summary>
    /// Queries whether the player is currently standing inside a landmark building interior.
    /// </summary>
    public bool TryGetBuildingAt(Vector2 playerWorld, out RectangleF room)
    {
        if (_poiRenderer is not null && _poiRenderer.TryGetBuildingAt(playerWorld, out _, out room))
        {
            return true;
        }

        room = default;
        return false;
    }

    /// <summary>Invalidates cached GPU terrain meshes around a modified chunk.</summary>
    public void InvalidateChunk(int chunkX, int chunkY) =>
        _terrainRenderer.InvalidateChunk(chunkX, chunkY);

    /// <summary>Invalidates all cached GPU terrain meshes.</summary>
    public void InvalidateAll() =>
        _terrainRenderer.InvalidateAll();

    /// <summary>Evicts cached GPU terrain meshes outside the specified chunk radius.</summary>
    public void EvictDistant(int centerChunkX, int centerChunkY, int evictionRadius) =>
        _terrainRenderer.EvictDistant(centerChunkX, centerChunkY, evictionRadius);

    /// <summary>
    /// Executes the full multi-pass 2.5D frame render pipeline.
    /// </summary>
    public void Render(
        IClientWorldSession session,
        CameraRig2D cameraRig,
        int viewportWidth,
        int viewportHeight,
        float frameDt,
        float timeScale)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cameraRig);
        if (!session.IsReady || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        this.EnsureWorldBound(session);

        _animTime += frameDt;
        _atmosphere.Update(frameDt * timeScale);
        _lights.Clear();

        _device.SetViewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
        _device.SetClearColor(new Vector4(0.04f, 0.05f, 0.08f, 1.0f));
        _device.Clear(ClearBufferMask.ColorBufferBit);

        float halfW = viewportWidth * 0.5f / cameraRig.Camera.Zoom;
        float halfH = viewportHeight * 0.5f / cameraRig.Camera.Zoom;
        float minX = cameraRig.Camera.Position.X - halfW;
        float maxX = cameraRig.Camera.Position.X + halfW;
        float minY = cameraRig.Camera.Position.Y - halfH;
        float maxY = cameraRig.Camera.Position.Y + halfH;

        Matrix4x4 viewProj = cameraRig.Camera.ViewProjectionMatrix;
        ChunkWorld2D chunkWorld = session.ChunkWorld;
        float tileSize = chunkWorld.TileSize;

        // 1. Static cached terrain chunk meshes
        _terrainRenderer.Begin(in viewProj);
        foreach (Chunk2D chunk in chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            _terrainRenderer.RenderChunk(chunk, chunkWorld);
        }

        _terrainRenderer.End();

        // 2. Animated water ripples, waterfalls, cloud shadows, debug solids, and POI ground
        _geometryBatch.Begin(viewProj);

        foreach (Chunk2D chunk in chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            AmbientAnimator.DrawRipples(_geometryBatch, _terrainRenderer.GetRipples(chunk.Key), _animTime);
            AmbientAnimator.DrawWaterfalls(_geometryBatch, _terrainRenderer.GetWaterfalls(chunk.Key), _animTime);
        }

        _atmosphere.DrawCloudShadows(_geometryBatch, minX, minY, maxX, maxY);

        if (this.ShowDebugOverlay)
        {
            DrawDebugSolids(_geometryBatch, chunkWorld, tileSize, minX, minY, maxX, maxY);
        }

        Vector2 playerPos = session.LocalPlayerInterpolatedPosition;

        _groundSortList.Clear();
        _canopySortList.Clear();
        _visibleWorldTrees.Clear();

        foreach (Chunk2D chunk in chunkWorld.ActiveChunks)
        {
            for (int i = 0; i < chunk.PointsOfInterest.Count; i++)
            {
                PointOfInterest poi = chunk.PointsOfInterest[i];
                float reach = (poi.Radius + 6) * tileSize * 2f;
                float px = (poi.TileX + 0.5f) * tileSize;
                float py = (poi.TileY + 0.5f) * tileSize;
                if (px + reach < minX || px - reach > maxX || py + reach < minY || py - reach > maxY || _poiRenderer is null)
                {
                    continue;
                }

                _poiRenderer.DrawGround(_geometryBatch, in poi, _animTime, playerPos);
                _poiRenderer.CollectLights(in poi, _lights, _animTime);
                if (poi.Type == PointOfInterestType.WorldTree)
                {
                    _visibleWorldTrees.Add(poi);
                }

                ReadOnlySpan<LandmarkPiece> pieces = _poiRenderer.GetLayout(in poi).Pieces;
                for (int k = 0; k < pieces.Length; k++)
                {
                    _groundSortList.Add(new GroundRenderItem(pieces[k].Y * tileSize, in pieces[k]));
                }
            }
        }

        foreach (Chunk2D chunk in chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            for (int i = 0; i < chunk.Props.Count; i++)
            {
                MapProp prop = chunk.Props[i];
                if (prop.Destructibility.IsDestroyed)
                {
                    continue;
                }

                if (prop.WorldPositionX + 96f >= minX && prop.WorldPositionX - 96f <= maxX &&
                    prop.WorldPositionY + 96f >= minY && prop.WorldPositionY - 96f <= maxY)
                {
                    _groundSortList.Add(new GroundRenderItem(prop.GroundFootprintY, in prop));
                    if (PropArt.TryGetLight(in prop, out PointLight propLight))
                    {
                        _lights.Add(propLight);
                    }

                    if (prop.HasCanopy)
                    {
                        _canopySortList.Add(new CanopyRenderItem(prop.WorldPositionY, in prop));
                    }
                }
            }
        }

        WorldSimulation sim = session.Simulation;
        for (int i = 0; i < sim.Entities.ActiveEntities.Count; i++)
        {
            Entity entity = sim.Entities.ActiveEntities[i];
            if (!sim.Entities.HasTransform(entity) || !sim.Entities.HasCollider(entity))
            {
                continue;
            }

            ref readonly Transform2D trans = ref sim.Entities.GetTransform(entity);
            ref readonly Collider2D col = ref sim.Entities.GetCollider(entity);
            _groundSortList.Add(new GroundRenderItem(trans.Position.Y + col.Radius, entity));
        }

        IReadOnlyList<RemoteEntityView> remotes = session.RemoteEntities;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView remote = remotes[i];
            if (remote.Position.X + 64f >= minX && remote.Position.X - 64f <= maxX &&
                remote.Position.Y + 64f >= minY && remote.Position.Y - 64f <= maxY)
            {
                _groundSortList.Add(new GroundRenderItem(remote.Position.Y + remote.Radius, in remote));
            }
        }

        // 3. Y-sorted ground pass (North-to-South 2.5D occlusion)
        _groundSortList.Sort();
        Span<GroundRenderItem> groundSpan = CollectionsMarshal.AsSpan(_groundSortList);
        for (int i = 0; i < groundSpan.Length; i++)
        {
            ref readonly GroundRenderItem item = ref groundSpan[i];
            switch (item.Kind)
            {
                case GroundItemKind.Piece:
                    _poiRenderer?.DrawPiece(_geometryBatch, in item.Piece, _animTime, playerPos);
                    break;
                case GroundItemKind.Prop:
                    PropArt.DrawGround(_geometryBatch, in item.Prop);
                    break;
                case GroundItemKind.LocalEntity:
                    DrawLocalEntity(_geometryBatch, session, item.Entity);
                    break;
                case GroundItemKind.RemoteEntity:
                    DrawRemoteEntity(_geometryBatch, in item.Remote);
                    break;
                default:
                    break;
            }
        }

        // 4. Overhead swaying canopies
        _canopySortList.Sort();
        Span<CanopyRenderItem> canopySpan = CollectionsMarshal.AsSpan(_canopySortList);
        for (int i = 0; i < canopySpan.Length; i++)
        {
            MapProp tree = canopySpan[i].Prop;
            float gust = 0.6f + (0.4f * MathF.Sin(_animTime * 0.35f));
            float sway = MathF.Sin((_animTime * 1.25f) + (tree.WorldPositionX * 0.013f) + (tree.WorldPositionY * 0.007f)) * 1.8f * gust * tree.Scale;
            byte alpha = tree.IsUnderCanopy(playerPos) ? (byte)90 : (byte)255;
            PropArt.DrawCanopy(_geometryBatch, in tree, alpha, sway);
        }

        for (int i = 0; i < _visibleWorldTrees.Count; i++)
        {
            PointOfInterest tree = _visibleWorldTrees[i];
            _poiRenderer?.DrawWorldTreeCanopy(_geometryBatch, in tree, _animTime, playerPos);
        }

        _geometryBatch.End();

        // 5. Atmosphere multiply ambient, additive point lights, sky weather overlay, and interior darkening
        _geometryBatch.Begin(viewProj, Atmosphere.Multiply);
        _atmosphere.DrawAmbient(_geometryBatch, minX, minY, maxX, maxY);
        _geometryBatch.End();

        _lights.Add(new PointLight(playerPos, 150f, new ColorRgba(255, 214, 150), 0.6f));
        _geometryBatch.Begin(viewProj, BlendState.Additive);
        _atmosphere.DrawLights(_geometryBatch, _lights);
        _geometryBatch.End();

        _geometryBatch.Begin(viewProj);
        if (cameraRig.InteriorBlend < 0.5f)
        {
            _atmosphere.DrawOverlay(_geometryBatch, minX, minY, maxX, maxY, playerPos);
        }

        if (cameraRig.InteriorBlend > 0.01f)
        {
            DrawInteriorRoomMask(_geometryBatch, cameraRig.InteriorRoom, cameraRig.InteriorBlend, minX, minY, maxX, maxY);
        }

        _geometryBatch.End();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _terrainRenderer.Dispose();
            _geometryBatch.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private static void DrawDebugSolids(
        GeometryBatch batch,
        ChunkWorld2D chunkWorld,
        float tileSize,
        float minX,
        float minY,
        float maxX,
        float maxY)
    {
        foreach (Chunk2D chunk in chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            for (int ly = 0; ly < Chunk2D.Size; ly++)
            {
                float tileWorldY = cMinY + (ly * tileSize);
                for (int lx = 0; lx < Chunk2D.Size; lx++)
                {
                    ref readonly MapTile tile = ref chunk.GetTile(lx, ly);
                    if (tile.IsSolid)
                    {
                        float tileWorldX = cMinX + (lx * tileSize);
                        batch.FillRectangle(new RectangleF(tileWorldX + 2f, tileWorldY + 2f, tileSize - 4f, tileSize - 4f), new ColorRgba(0, 0, 0, 70));
                    }
                }
            }
        }
    }

    private static void DrawLocalEntity(GeometryBatch batch, IClientWorldSession session, Entity entity)
    {
        WorldSimulation world = session.Simulation;
        if (entity != session.LocalPlayer || !world.Entities.HasTransform(entity) || !world.Entities.HasCollider(entity))
        {
            return;
        }

        Vector2 entityPos = world.GetInterpolatedPosition(entity);
        ref readonly Collider2D col = ref world.Entities.GetCollider(entity);
        float tileSize = session.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(entityPos.X / tileSize);
        int pty = (int)MathF.Floor(entityPos.Y / tileSize);
        MapTile currentTile = session.ChunkWorld.GetTileAtWorld(ptx, pty);

        if (currentTile.Biome == BiomeType.ShallowWater)
        {
            batch.FillCircle(entityPos, col.Radius + 6f, new ColorRgba(255, 255, 255, 55));
            batch.FillCircle(entityPos, col.Radius + 4f, new ColorRgba(56, 189, 248, 75));
        }

        batch.FillCircle(new Vector2(entityPos.X + 2f, entityPos.Y + 3f), col.Radius + 1f, new ColorRgba(0, 0, 0, 75));
        batch.FillCircle(entityPos, col.Radius + 1.5f, new ColorRgba(248, 250, 252, 220));
        batch.FillCircle(entityPos, col.Radius, new ColorRgba(15, 23, 42, 255));
        batch.FillCircle(entityPos, col.Radius * 0.82f, new ColorRgba(30, 64, 175, 255));
        batch.FillCircle(entityPos, col.Radius * 0.52f, new ColorRgba(56, 189, 248, 255));
        batch.FillCircle(entityPos, col.Radius * 0.22f, new ColorRgba(250, 204, 21, 255));
    }

    private static void DrawRemoteEntity(GeometryBatch batch, in RemoteEntityView remote)
    {
        float r = remote.Radius > 0f ? remote.Radius : 7f;
        (ColorRgba outerRing, ColorRgba coreColor) = remote.Kind switch
        {
            EntityKind.Monster => (new ColorRgba(185, 28, 28, 255), new ColorRgba(248, 113, 113, 255)),
            EntityKind.Npc => (new ColorRgba(180, 83, 9, 255), new ColorRgba(251, 191, 36, 255)),
            EntityKind.Projectile => (new ColorRgba(126, 34, 206, 255), new ColorRgba(216, 180, 254, 255)),
            EntityKind.LootDrop => (new ColorRgba(161, 98, 7, 255), new ColorRgba(253, 224, 71, 255)),
            EntityKind.Player => (new ColorRgba(4, 120, 87, 255), new ColorRgba(52, 211, 153, 255)),
            _ => (new ColorRgba(4, 120, 87, 255), new ColorRgba(52, 211, 153, 255)),
        };

        batch.FillCircle(new Vector2(remote.Position.X + 2f, remote.Position.Y + 3f), r + 1f, new ColorRgba(0, 0, 0, 75));
        batch.FillCircle(remote.Position, r + 1.4f, new ColorRgba(241, 245, 249, 210));
        batch.FillCircle(remote.Position, r, new ColorRgba(15, 23, 42, 255));
        batch.FillCircle(remote.Position, r * 0.78f, outerRing);
        batch.FillCircle(remote.Position, r * 0.42f, coreColor);

        if (remote.MaxHealth > 0f && remote.CurrentHealth < remote.MaxHealth)
        {
            float ratio = Math.Clamp(remote.CurrentHealth / remote.MaxHealth, 0f, 1f);
            float barW = r * 2.8f;
            float barX = remote.Position.X - (barW * 0.5f);
            float barY = remote.Position.Y - r - 8f;
            batch.FillRectangle(new RectangleF(barX, barY, barW, 3f), new ColorRgba(15, 23, 42, 200));
            batch.FillRectangle(new RectangleF(barX, barY, barW * ratio, 3f), new ColorRgba(34, 197, 94, 240));
        }
    }

    private static void DrawInteriorRoomMask(
        GeometryBatch batch,
        RectangleF room,
        float interiorBlend,
        float minX,
        float minY,
        float maxX,
        float maxY)
    {
        const float pad = 4f;
        float left = room.X - pad;
        float right = room.X + room.Width + pad;
        float topY = room.Y - pad;
        float bottom = room.Y + room.Height + pad;
        ColorRgba dim = new(6, 8, 14, (byte)(225f * interiorBlend));
        float bigL = minX - 64f;
        float bigT = minY - 64f;
        float bigR = maxX + 64f;
        float bigB = maxY + 64f;
        batch.FillRectangle(new RectangleF(bigL, bigT, bigR - bigL, topY - bigT), dim);
        batch.FillRectangle(new RectangleF(bigL, bottom, bigR - bigL, bigB - bottom), dim);
        batch.FillRectangle(new RectangleF(bigL, topY, left - bigL, bottom - topY), dim);
        batch.FillRectangle(new RectangleF(right, topY, bigR - right, bottom - topY), dim);

        ColorRgba edge = new(6, 8, 14, (byte)(90f * interiorBlend));
        batch.FillRectangle(new RectangleF(left, topY, right - left, 6f), edge);
        batch.FillRectangle(new RectangleF(left, bottom - 6f, right - left, 6f), edge);
        batch.FillRectangle(new RectangleF(left, topY, 6f, bottom - topY), edge);
        batch.FillRectangle(new RectangleF(right - 6f, topY, 6f, bottom - topY), edge);
    }

    private enum GroundItemKind : byte
    {
        Prop = 0,
        LocalEntity = 1,
        Piece = 2,
        RemoteEntity = 3,
    }

    private readonly struct GroundRenderItem : IComparable<GroundRenderItem>
    {
        public readonly float SortY;
        public readonly GroundItemKind Kind;
        public readonly MapProp Prop;
        public readonly Entity Entity;
        public readonly LandmarkPiece Piece;
        public readonly RemoteEntityView Remote;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, in MapProp prop)
        {
            this.SortY = sortY;
            this.Kind = GroundItemKind.Prop;
            this.Prop = prop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, Entity entity)
        {
            this.SortY = sortY;
            this.Kind = GroundItemKind.LocalEntity;
            this.Entity = entity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, in LandmarkPiece piece)
        {
            this.SortY = sortY;
            this.Kind = GroundItemKind.Piece;
            this.Piece = piece;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, in RemoteEntityView remote)
        {
            this.SortY = sortY;
            this.Kind = GroundItemKind.RemoteEntity;
            this.Remote = remote;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(GroundRenderItem other) => this.SortY.CompareTo(other.SortY);
    }

    private readonly struct CanopyRenderItem : IComparable<CanopyRenderItem>
    {
        public readonly float SortY;
        public readonly MapProp Prop;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CanopyRenderItem(float sortY, in MapProp prop)
        {
            this.SortY = sortY;
            this.Prop = prop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(CanopyRenderItem other) => this.SortY.CompareTo(other.SortY);
    }
}
