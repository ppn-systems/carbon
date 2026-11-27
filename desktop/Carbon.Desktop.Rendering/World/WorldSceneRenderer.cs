// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Carbon.Combat.Statuses.Config;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Overlays;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Silk.NET.OpenGL;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Desktop.Rendering.Camera;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Orchestrates the 6-pass 2.5D world rendering pipeline using declarative <c>.cvg.json</c> vector rigs (DIP),
/// cached terrain chunks, Y-sorted props &amp; buildings, combat VFX ribbons, day/night lighting, and screen-space overlays.
/// Depends strictly on read-only <see cref="IWorldReadModel"/> (ISP).
/// </summary>
public sealed class WorldSceneRenderer : IDisposable
{
    private readonly IGraphicsDevice _device;

    // GPU resources, created on first draw (EnsureGpuResources) so the renderer can be built without a GL context.
    private GeometryBatch _geometryBatch = null!;
    private ChunkTerrainRenderer _terrainRenderer = null!;
    private bool _gpuReady;
    private readonly SlowTrail _slowTrail = new();
    private readonly MonsterEffectTints _monsterTints = new();
    private float _slowTrailTime;
    private readonly Atmosphere _atmosphere;
    private readonly IVectorArtRepository? _art;
    private readonly CombatFeedbackTracker _feedback = new();
    private readonly WorldEntityOverlayRenderer _overlays = new();
    private readonly ConstructionOverlayRenderer _construction = new();
    private RoofRenderer? _roofs;
    private readonly List<PointLight> _lights = new(64);
    private readonly List<GroundRenderItem> _groundSortList = new(2048);
    private readonly List<CanopyRenderItem> _canopySortList = new(1024);
    private readonly List<DestroyedPropRenderItem> _remnantList = new(512);
    private readonly List<DamagedPropHudMarker> _damagedProps = new(64);
    private readonly List<PointOfInterest> _visibleWorldTrees = new(4);

    private PointOfInterestResolver? _landmarks;
    private PointOfInterestRenderer? _poiRenderer;
    private ulong _boundSeed;
    private float _animTime;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldSceneRenderer"/> class. GPU resources (geometry batch, terrain
    /// renderer) are created on the first <see cref="Render"/>, so construction needs no GL context.
    /// </summary>
    /// <param name="device">Graphics device the world renders with.</param>
    /// <param name="options">Client rendering/atmosphere options.</param>
    /// <param name="artRepository">Optional injected declarative vector-art repository (DIP).</param>
    public WorldSceneRenderer(
        IGraphicsDevice device,
        DesktopOptions options,
        IVectorArtRepository? artRepository = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(options);

        _art = artRepository ?? CarbonVectorArtLibrary.LoadOrDefault();
        _atmosphere = new Atmosphere(options.StartHour, options.DayLengthSeconds, options.WeatherRainOverride);
    }

    /// <summary>Gets the current in-game hour <c>[0..24)</c> from the atmospheric cycle.</summary>
    public float TimeOfDayHour => _atmosphere.Hour;

    /// <summary>Gets the declarative vector-art repository shared with HUD widgets.</summary>
    public IVectorArtRepository? VectorArt => _art;

    /// <summary>Gets the combat &amp; gathering feedback tracker shared with HUD target unit frames.</summary>
    public CombatFeedbackTracker FeedbackTracker => _feedback;

    /// <summary>Gets or sets a value indicating whether solid collision debug overlays are rendered.</summary>
    public bool ShowDebugOverlay { get; set; }

    /// <summary>Ensures deterministic landmark metadata matches the active world seed.</summary>
    public void EnsureWorldBound(IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (_poiRenderer is null || _boundSeed != world.WorldSeed || !ReferenceEquals(_landmarks, world.ChunkWorld.Generator.Landmarks))
        {
            // The session's generator has the server's seed and frequency (CharacterEnterWorld); a seed-only one resolves other POIs.
            _landmarks = world.ChunkWorld.Generator.Landmarks;
            _poiRenderer = new PointOfInterestRenderer(world.ChunkWorld.TileSize, _landmarks);
            _roofs = new RoofRenderer(world.ChunkWorld.TileSize);
            _boundSeed = world.WorldSeed;
            this.InvalidateAll();
        }
    }

    /// <summary>Queries whether the player is currently standing inside a landmark building interior.</summary>
    public bool TryGetBuildingAt(Vector2 playerWorld, out RectangleF room)
    {
        if (_poiRenderer is not null && _poiRenderer.TryGetBuildingAt(playerWorld, out _, out room))
        {
            return true;
        }

        // Rooms of the building grid (village houses, player builds) from the roof renderer's last refresh.
        if (_roofs is not null && _roofs.TryGetRoomAt(playerWorld, out room))
        {
            return true;
        }

        room = default;
        return false;
    }

    /// <summary>Invalidates cached GPU terrain meshes around a modified chunk (nothing is cached before the first draw).</summary>
    public void InvalidateChunk(int chunkX, int chunkY)
    {
        if (_gpuReady)
        {
            _terrainRenderer.InvalidateChunk(chunkX, chunkY);
        }
    }

    /// <summary>Invalidates all cached GPU terrain meshes (nothing is cached before the first draw).</summary>
    public void InvalidateAll()
    {
        if (_gpuReady)
        {
            _terrainRenderer.InvalidateAll();
        }
    }

    /// <summary>Evicts cached GPU terrain meshes outside the specified chunk radius (nothing is cached before the first draw).</summary>
    public void EvictDistant(int centerChunkX, int centerChunkY, int evictionRadius)
    {
        if (_gpuReady)
        {
            _terrainRenderer.EvictDistant(centerChunkX, centerChunkY, evictionRadius);
        }
    }

    /// <summary>Executes the full multi-pass 2.5D frame render pipeline.</summary>
    public void Render(
        IWorldReadModel world,
        CameraRig2D cameraRig,
        int viewportWidth,
        int viewportHeight,
        float frameDt,
        float timeScale,
        SpriteBatch? spriteBatch = null,
        BitmapFont? font = null,
        Vector2? aimedWorldPosition = null,
        bool showCombatNumbers = true)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cameraRig);
        if (!world.IsReady || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        this.EnsureGpuResources();
        this.EnsureWorldBound(world);
        _animTime += frameDt;
        if (world.AuthoritativeTimeHour is float authHour)
        {
            _atmosphere.SyncToHour(authHour);
            _atmosphere.Update(0f);
        }
        else
        {
            _atmosphere.Update(frameDt * timeScale);
        }
        _lights.Clear();

        _feedback.Update(world, frameDt, _animTime, aimedWorldPosition);

        _device.SetViewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
        cameraRig.Resize(viewportWidth, viewportHeight);
        _device.SetClearColor(new Vector4(0.04f, 0.05f, 0.08f, 1.0f));
        _device.Clear(ClearBufferMask.ColorBufferBit);

        float halfW = viewportWidth * 0.5f / cameraRig.Camera.Zoom;
        float halfH = viewportHeight * 0.5f / cameraRig.Camera.Zoom;
        float minX = cameraRig.Camera.Position.X - halfW;
        float maxX = cameraRig.Camera.Position.X + halfW;
        float minY = cameraRig.Camera.Position.Y - halfH;
        float maxY = cameraRig.Camera.Position.Y + halfH;

        Matrix4x4 viewProj = cameraRig.Camera.ViewProjectionMatrix;
        ChunkWorld2D chunkWorld = world.ChunkWorld;
        float tileSize = chunkWorld.TileSize;

        // Pass 1: Static cached terrain VBO chunks
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

        // Pass 2..4: Ambient ripples, ground remnants, Y-sorted props & vector-rig entities, canopies & VFX
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

        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;
        int aimTileX = int.MinValue;
        int aimTileY = int.MinValue;
        bool hasAimTile = false;
        if (aimedWorldPosition.HasValue)
        {
            Span<Carbon.Items.Containers.ItemSlot> hSlots = stackalloc Carbon.Items.Containers.ItemSlot[Carbon.Items.Catalog.PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(hSlots);
            Carbon.Items.Containers.ItemSlot held = world.SelectedHotbarSlot < hSlots.Length ? hSlots[world.SelectedHotbarSlot] : Carbon.Items.Containers.ItemSlot.Empty;
            if (!held.IsEmpty && world.Items.Rules.Items.TryGet(held.ItemId, out Carbon.Items.Definitions.ItemArchetype? heldArchetype) &&
                heldArchetype.Category is Carbon.Items.Definitions.ItemCategory.Placeable or Carbon.Items.Definitions.ItemCategory.Tool)
            {
                aimTileX = (int)MathF.Floor(aimedWorldPosition.Value.X / tileSize);
                aimTileY = (int)MathF.Floor(aimedWorldPosition.Value.Y / tileSize);
                Vector2 aimCenter = new((aimTileX + 0.5f) * tileSize, (aimTileY + 0.5f) * tileSize);
                float reachPx = heldArchetype.Category == Carbon.Items.Definitions.ItemCategory.Placeable ? world.BuildReachTiles * tileSize : 52f;
                hasAimTile = Vector2.DistanceSquared(playerPos, aimCenter) <= (reachPx * reachPx);
            }
        }

        this.CollectVisibleObjects(chunkWorld, tileSize, minX, maxX, minY, maxY, playerPos, hasAimTile, aimTileX, aimTileY);
        _construction.BeginFrame(world);

        Span<DestroyedPropRenderItem> remnantSpan = CollectionsMarshal.AsSpan(_remnantList);
        for (int i = 0; i < remnantSpan.Length; i++)
        {
            MapProp remnant = remnantSpan[i].Prop;
            VectorPropRenderer.DrawDestroyedRemnant(_geometryBatch, in remnant, _art);
        }

        if (hasAimTile && !_construction.TryDrawPlacementGhost(_geometryBatch, world, _art, tileSize, aimTileX, aimTileY))
        {
            DrawTargetTileHighlight(_geometryBatch, world, chunkWorld, tileSize, aimTileX, aimTileY);
        }

        this.CollectActors(world, minX, maxX, minY, maxY);

        // Pass 3: Strict North-to-South Y-sorted drawing
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
                    VectorPropRenderer.DrawGround(_geometryBatch, in item.Prop, _art, item.ShakeOffset, item.HitFlash);
                    break;
                case GroundItemKind.LocalEntity:
                    this.DrawLocalEntity(_geometryBatch, world, item.Entity);
                    break;
                case GroundItemKind.RemoteEntity:
                    this.DrawRemoteEntity(_geometryBatch, world, in item.Remote);
                    break;
                default:
                    break;
            }
        }

        _construction.DrawMarkers(_geometryBatch, _art, tileSize, minX, minY, maxX, maxY, _animTime);

        // Roofs over enclosed rooms (ADR-025), above everything walking inside them; the player's own room stays open.
        _roofs?.Refresh(chunkWorld.Buildings, minX, minY, maxX, maxY);
        _roofs?.Draw(_geometryBatch, chunkWorld.Buildings, playerPos, minX, minY, maxX, maxY, _animTime);

        // Pass 4: Overhead swaying canopies, particles & action swing ribbons
        _canopySortList.Sort();
        Span<CanopyRenderItem> canopySpan = CollectionsMarshal.AsSpan(_canopySortList);
        for (int i = 0; i < canopySpan.Length; i++)
        {
            ref readonly CanopyRenderItem canopyItem = ref canopySpan[i];
            MapProp tree = canopyItem.Prop;
            float gust = 0.6f + (0.4f * MathF.Sin(_animTime * 0.35f));
            float sway = MathF.Sin((_animTime * 1.25f) + (tree.WorldPositionX * 0.013f) + (tree.WorldPositionY * 0.007f)) * 1.8f * gust * tree.Scale;
            byte alpha = tree.IsUnderCanopy(playerPos) ? (byte)90 : (byte)255;
            VectorPropRenderer.DrawCanopy(_geometryBatch, in tree, _art, alpha, sway + canopyItem.ShakeX, canopyItem.DamageTilt);
        }

        for (int i = 0; i < _visibleWorldTrees.Count; i++)
        {
            PointOfInterest tree = _visibleWorldTrees[i];
            _poiRenderer?.DrawWorldTreeCanopy(_geometryBatch, in tree, _animTime, playerPos);
        }

        _feedback.DrawDebrisParticles(_geometryBatch);
        this.DrawActionTrails(world, playerPos);
        _geometryBatch.End();

        // Pass 5: Day/night multiply ambient, point lights, weather overlay & interior dimming
        _geometryBatch.Begin(viewProj, Atmosphere.Multiply);
        _atmosphere.DrawAmbient(_geometryBatch, minX, minY, maxX, maxY);
        _geometryBatch.End();

        // The player emits no light of their own: an automatic lantern here made the character glow after dark.
        // Light comes only from world sources (props, POIs, projectiles) and, later, an equipped light item.
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

        // Pass 6: Screen-space proportional monster/NPC/player plates, ghost-trail HP bars & combat numbers
        if (spriteBatch is not null && font is not null)
        {
            Matrix4x4 screenProj = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
            _geometryBatch.Begin(screenProj);
            _overlays.DrawGeometry(
                _geometryBatch,
                world,
                _art,
                _feedback,
                cameraRig,
                CollectionsMarshal.AsSpan(_damagedProps),
                font,
                viewportWidth,
                viewportHeight,
                _animTime);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _overlays.DrawText(spriteBatch, font, world, _art, _feedback, cameraRig, viewportWidth, viewportHeight, showCombatNumbers);
            _construction.DrawText(spriteBatch, font, cameraRig, playerPos, tileSize);
            spriteBatch.End();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_gpuReady)
            {
                _terrainRenderer.Dispose();
                _geometryBatch.Dispose();
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // Creates the GPU resources once, in the original order; later frames only read a flag and allocate nothing here.
    private void EnsureGpuResources()
    {
        if (_gpuReady)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        _geometryBatch = new GeometryBatch(_device);
        _terrainRenderer = new ChunkTerrainRenderer(_device, _art);
        _gpuReady = true;
    }

    private void CollectVisibleObjects(
        ChunkWorld2D chunkWorld,
        float tileSize,
        float minX,
        float maxX,
        float minY,
        float maxY,
        Vector2 playerPos,
        bool hasAimTile,
        int aimTileX,
        int aimTileY)
    {
        _groundSortList.Clear();
        _canopySortList.Clear();
        _remnantList.Clear();
        _damagedProps.Clear();
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

            chunk.GetBounds(tileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            int baseTileX = chunk.ChunkX * Chunk2D.Size;
            int baseTileY = chunk.ChunkY * Chunk2D.Size;

            for (int i = 0; i < chunk.Props.Count; i++)
            {
                MapProp prop = chunk.Props[i];
                if (prop.WorldPositionX + 96f < minX || prop.WorldPositionX - 96f > maxX ||
                    prop.WorldPositionY + 96f < minY || prop.WorldPositionY - 96f > maxY)
                {
                    continue;
                }

                if (prop.Destructibility.IsDestroyed)
                {
                    _remnantList.Add(new DestroyedPropRenderItem(in prop));
                    continue;
                }

                int propWx = baseTileX + prop.LocalTileX;
                int propWy = baseTileY + prop.LocalTileY;
                Vector2 shakeOffset = Vector2.Zero;
                float hitFlash = 0f;
                bool hasRecentHit = _feedback.TryGetPropHit(propWx, propWy, out PropHitState hitState);
                if (hasRecentHit && hitState.ShakeTimer > 0f)
                {
                    float frac = hitState.ShakeTimer / 0.26f;
                    float amp = frac * 4.5f;
                    shakeOffset = new Vector2(MathF.Sin(_animTime * 68f) * amp, MathF.Cos(_animTime * 54f) * amp * 0.45f);
                    hitFlash = Math.Clamp(frac * 0.72f, 0f, 0.72f);
                }

                _groundSortList.Add(new GroundRenderItem(prop.GroundFootprintY, in prop, shakeOffset, hitFlash));
                if (VectorPropRenderer.TryGetLight(in prop, _art, out PointLight propLight))
                {
                    _lights.Add(propLight);
                }

                if (prop.HasCanopy)
                {
                    float hpRatio = prop.Destructibility.IsDestructible ? prop.Destructibility.HealthPercentage : 1f;
                    float damageTilt = hpRatio < 0.99f ? (1f - hpRatio) * ((propWx & 1) == 0 ? 5.5f : -5.5f) : 0f;
                    _canopySortList.Add(new CanopyRenderItem(prop.WorldPositionY, in prop, shakeOffset.X, damageTilt));
                }

                bool isHovered = hasAimTile && propWx == aimTileX && propWy == aimTileY;
                float distToPropSq = Vector2.DistanceSquared(playerPos, prop.WorldPosition);
                bool inActivePropScope = distToPropSq <= (256f * 256f);
                if (prop.Destructibility.IsDestructible &&
                    prop.Destructibility.MaxDurability > 0f &&
                    inActivePropScope &&
                    (prop.Destructibility.HealthPercentage < 0.99f || hasRecentHit || isHovered))
                {
                    float alpha = hasRecentHit
                        ? Math.Clamp(hitState.BarTimer / 1.0f, 0f, 1f)
                        : 1f;
                    _damagedProps.Add(new DamagedPropHudMarker(
                        prop.WorldPosition + new Vector2(0f, prop.Category == PropCategory.Flora && prop.HasCanopy ? -prop.Radius - 22f : -prop.Radius - 10f),
                        prop.Destructibility.CurrentDurability,
                        prop.Destructibility.MaxDurability,
                        prop.Category == PropCategory.Mineral,
                        alpha));
                }
            }
        }
    }

    private void CollectActors(IWorldReadModel world, float minX, float maxX, float minY, float maxY)
    {
        WorldSimulation sim = world.Simulation;
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

        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView remote = remotes[i];
            if (remote.Position.X + 64f >= minX && remote.Position.X - 64f <= maxX &&
                remote.Position.Y + 64f >= minY && remote.Position.Y - 64f <= maxY)
            {
                _groundSortList.Add(new GroundRenderItem(remote.Position.Y + remote.Radius, in remote));
            }
        }
    }

    /// <summary>Gets whether the local player has an effect that lowers move speed (ADR-029).</summary>
    private static bool IsSlowed(IWorldReadModel world)
    {
        IReadOnlyList<LocalStatusEffect> effects = world.LocalStatusEffects;
        for (int i = 0; i < effects.Count; i++)
        {
            if (world.StatusEffectDefinitions.TryGet(effects[i].EffectId, out StatusEffectDefinition def) && def.MoveSpeedMultiplier < 1f)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawLocalEntity(GeometryBatch batch, IWorldReadModel world, Entity entity)
    {
        WorldSimulation sim = world.Simulation;
        if (entity != world.LocalPlayer || !sim.Entities.HasTransform(entity) || !sim.Entities.HasCollider(entity))
        {
            return;
        }

        Vector2 entityPos = world.LocalPlayerInterpolatedPosition;
        ref readonly Collider2D col = ref sim.Entities.GetCollider(entity);
        Vector2 velocity = sim.Entities.HasVelocity(entity) ? sim.Entities.GetVelocity(entity).Linear : Vector2.Zero;
        float tileSize = world.ChunkWorld.TileSize;
        Vector2 footPos = new(entityPos.X, entityPos.Y + col.Radius);
        CompiledVectorRig? rig = null;
        bool hasRig = _art is not null && _art.TryGetCharacter(world.LocalPlayerAppearancePreset, out rig);
        WadingRenderer.Wade wade = WadingRenderer.Resolve(world.ChunkWorld, entityPos, hasRig ? rig!.WorldHeight : 35f, col.Radius);
        _slowTrail.Update(footPos, _animTime - _slowTrailTime, IsSlowed(world));
        _slowTrailTime = _animTime;
        _slowTrail.Draw(batch, col.Radius * 1.1f);

        if (wade.IsWading)
        {
            WadingRenderer.DrawBehind(batch, footPos, in wade, _animTime, 1u);
        }
        else
        {
            batch.FillCircle(new Vector2(footPos.X + 1.5f, footPos.Y + 1.5f), col.Radius * 1.18f, new ColorRgba(0, 0, 0, 88), 12);
            batch.DrawCircle(footPos, col.Radius + 3.5f, new ColorRgba(56, 189, 248, 150), 14, 1.3f);
        }

        ActionState action = world.CurrentAction;
        Vector2 facing = action.IsActive
            ? action.AimDirection
            : (world.LocalFacingDirection.LengthSquared() > 1e-4f ? world.LocalFacingDirection : world.LastAttackDirection);
        Direction8 dir8 = DirectionExtensions.FromVector(facing, Direction8.South);
        bool downed = this.PreviewDowned || world.LocalDeath is not null;
        string clip = downed ? DownedClip : ResolveCharacterClip(action, world.AttackSwingTimer, velocity.LengthSquared() > 9f);
        bool isNorm = downed || action.IsActive || world.AttackSwingTimer > 0f;
        if (!isNorm && SwimClip(wade.State) is string swimClip)
        {
            clip = swimClip; // ADR-032: strokes on the surface, a breaststroke under it
        }

        float eval = downed
            ? 1f
            : action.IsActive
                ? action.Progress
                : (world.AttackSwingTimer > 0f ? 1f - Math.Clamp(world.AttackSwingTimer / 0.24f, 0f, 1f) : _animTime);
        float flash = world.LocalHitFlashTimer > 0f ? Math.Clamp(world.LocalHitFlashTimer / 0.16f, 0f, 0.8f) : 0f;

        // The body commits toward the target in every facing (docs/art/combat-animation.md §3.3).
        Vector2 lean = action.IsActive ? AttackLean(in action) : Vector2.Zero;

        if (_art is not null && hasRig && rig is not null)
        {
            Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(slots);
            ItemSlot activeItem = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
            uint shownId = action.IsActive && action.EquippedItemId != 0U ? action.EquippedItemId : activeItem.ItemId;
            CompiledItemVectorArt? equipped = null;
            if (shownId != 0U)
            {
                _ = _art.TryGetItem(shownId, out equipped);
            }

            ushort headId = PlayerInventoryLayout.HeadSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.HeadSlot].ItemId : (ushort)0;
            ushort chestId = PlayerInventoryLayout.ChestSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.ChestSlot].ItemId : (ushort)0;
            ushort legsId = PlayerInventoryLayout.LegsSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.LegsSlot].ItemId : (ushort)0;
            ushort feetId = PlayerInventoryLayout.FeetSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.FeetSlot].ItemId : (ushort)0;
            VisualEquipmentLoadout loadout = new(headId, chestId, legsId, feetId, (ushort)shownId, ItemEnhancementPolicy.GetUpgradeLevel(in activeItem));

            VectorRigRenderer.DrawEntityRig(
                batch,
                rig,
                footPos + new Vector2(0f, wade.Sink) + lean,
                dir8,
                clip,
                eval,
                isNorm,
                0f,
                flash,
                equipped,
                action,
                world.AttackSwingTimer,
                world.LocalPlayerAppearancePreset,
                loadout,
                _art);
            if (!downed)
            {
                WeaponTrailRenderer.Draw(batch, rig, equipped, footPos + new Vector2(0f, wade.Sink), dir8, in action, loadout.WeaponUpgradeLevel);
            }
        }
        else
        {
            batch.FillCircle(entityPos, col.Radius, new ColorRgba(56, 189, 248, 255));
        }

        WadingRenderer.DrawFront(batch, _art, tileSize, footPos, in wade, velocity, _animTime, 1u);
    }

    /// <summary>
    /// Draws a ground loot drop: the item's art, or the coin/diamond art for a currency pile (ADR-019). Loot still reserved
    /// for another player is drawn faded, without its light beam.
    /// </summary>
    private static void DrawLootDrop(GeometryBatch batch, IVectorArtRepository? art, in RemoteEntityView remote, float animTime)
    {
        float bob = (MathF.Sin((animTime * 4.2f) + remote.NetworkId) * 2.8f) - 5f;
        byte alpha = remote.IsLootLocked ? (byte)105 : (byte)255;
        batch.FillCircle(new Vector2(remote.Position.X + 1f, remote.Position.Y + 4f), 8.5f, new ColorRgba(0, 0, 0, remote.IsLootLocked ? (byte)50 : (byte)95), 10);

        CompiledItemVectorArt? lootArt = null;
        bool hasArt = art is not null && remote.LootCurrency switch
        {
            LootCurrency.Gold => art.TryGetUiIcon("coin", out lootArt),
            LootCurrency.Diamond => art.TryGetUiIcon("diamond", out lootArt),
            LootCurrency.None or _ => art.TryGetItem(remote.ArchetypeId, out lootArt),
        };
        if (!hasArt || lootArt is null)
        {
            batch.FillCircle(new Vector2(remote.Position.X, remote.Position.Y + bob), 6f, new ColorRgba(250, 204, 21, (byte)(240 * alpha / 255)), 10);
            return;
        }

        if (!remote.IsLootLocked)
        {
            ColorRgba beamCol = remote.WeaponUpgradeLevel > 0
                ? new ColorRgba(34, 211, 238, 70)
                : new ColorRgba(lootArt.AccentColor.R, lootArt.AccentColor.G, lootArt.AccentColor.B, 55);
            batch.FillRectangle(new RectangleF(remote.Position.X - 3f, remote.Position.Y - 22f, 6f, 24f), beamCol);
        }

        ColorRgba ringCol = remote.IsLootLocked
            ? new ColorRgba(148, 163, 184, 90)
            : remote.WeaponUpgradeLevel > 0
                ? new ColorRgba(34, 211, 238, 170)
                : new ColorRgba(lootArt.AccentColor.R, lootArt.AccentColor.G, lootArt.AccentColor.B, 140);
        batch.DrawCircle(new Vector2(remote.Position.X, remote.Position.Y + 2f), 10.5f, ringCol, 14, 1.2f);
        float size = remote.LootCurrency == LootCurrency.None ? 18f : 14f;
        VectorRigRenderer.DrawItemIcon(batch, lootArt, new Vector2(remote.Position.X, remote.Position.Y + bob), size, alpha: alpha);
    }

    /// <summary>Gets the character clip for a swim state (ADR-032, <c>base_hero.cvg.json</c>), or <see langword="null"/> out of deep water.</summary>
    /// <param name="state">The swim state.</param>
    /// <returns><c>swim</c>, <c>dive</c>, or <see langword="null"/>.</returns>
    internal static string? SwimClip(SwimState state) => state switch
    {
        SwimState.Swimming => "swim",
        SwimState.Diving => "dive",
        SwimState.Walking or SwimState.Wading => null,
        _ => null,
    };

    /// <summary>Character clip of a dead player lying on the ground (ADR-031, <c>base_hero.cvg.json</c>).</summary>
    private const string DownedClip = "downed";

    /// <summary>Gets or sets whether the local player is drawn downed for a screenshot preview (<c>CARBON_UI_PREVIEW=death</c>).</summary>
    public bool PreviewDowned { get; set; }

    private void DrawRemoteEntity(GeometryBatch batch, IWorldReadModel world, in RemoteEntityView remote)
    {
        if (remote.Kind == EntityKind.Projectile)
        {
            DrawProjectile(batch, in remote, _lights);
            return;
        }

        if (remote.Kind == EntityKind.LootDrop)
        {
            DrawLootDrop(batch, _art, in remote, _animTime);
            return;
        }

        float r = remote.Radius > 0f ? remote.Radius : 7f;
        Vector2 footPos = new(remote.Position.X, remote.Position.Y + r);
        Vector2 facing = remote.FacingDirection.LengthSquared() > 1e-4f
            ? remote.FacingDirection
            : (remote.Velocity.LengthSquared() > 4f ? Vector2.Normalize(remote.Velocity) : new Vector2(0f, 1f));
        facing = MonsterAttackAnimator.ResolveFacing(remote.Attack, facing);
        Direction8 dir8 = DirectionExtensions.FromVector(facing, Direction8.South);
        bool moving = remote.Velocity.LengthSquared() > 9f;
        bool attacking = remote.AttackAnimRemaining > 0f;
        float flash = remote.HitFlashRemaining > 0f ? Math.Clamp(remote.HitFlashRemaining / 0.16f, 0f, 0.8f) : 0f;

        CompiledVectorRig? wadeRig = null;
        bool hasWadeRig = _art is not null && remote.Kind switch
        {
            EntityKind.Player => _art.TryGetCharacter((byte)remote.ArchetypeId, out wadeRig),
            EntityKind.Npc => _art.TryGetNpc(remote.ArchetypeId, out wadeRig),
            EntityKind.Monster => _art.TryGetMonster(remote.ArchetypeId, out wadeRig),
            EntityKind.Projectile or EntityKind.LootDrop or _ => false,
        };
        uint wadeSeed = (uint)remote.NetworkId;
        WadingRenderer.Wade wade = WadingRenderer.Resolve(world.ChunkWorld, remote.Position, hasWadeRig ? wadeRig!.WorldHeight : 35f, r);
        if (wade.IsWading)
        {
            WadingRenderer.DrawBehind(batch, footPos, in wade, _animTime, wadeSeed);
        }
        else
        {
            batch.FillCircle(new Vector2(footPos.X + 1.5f, footPos.Y + 1.5f), r * 1.15f, new ColorRgba(0, 0, 0, 82), 12);
        }

        if (remote.Kind == EntityKind.Npc)
        {
            batch.DrawCircle(footPos, r + 2.5f, new ColorRgba(253, 224, 71, 110), 12, 1.1f);
        }
        else if (remote.Kind == EntityKind.Monster)
        {
            if (remote.NetworkId == world.LockedTargetNetworkId)
            {
                float pulse = r + 5.5f + (MathF.Sin(_animTime * 8f) * 1.4f);
                batch.DrawCircle(footPos, pulse, new ColorRgba(244, 63, 94, 225), 18, 1.8f);
                batch.DrawCircle(footPos, pulse + 3f, new ColorRgba(250, 204, 21, 160), 18, 1.1f);
            }
        }

        if (_art is not null)
        {
            CompiledVectorRig? rig = null;
            CompiledItemVectorArt? remoteWeapon = null;
            byte preset = 0;
            RigOverlay overlays = RigOverlay.None;
            VisualEquipmentLoadout remoteLoadout = default;
            string clip = moving ? "walk" : "idle";
            ActionState remoteAction = default;
            bool norm = false;
            float eval = _animTime + (remote.NetworkId % 7 * 0.13f);

            if (remote.Kind == EntityKind.Player && _art.TryGetCharacter((byte)remote.ArchetypeId, out rig))
            {
                preset = (byte)remote.ArchetypeId;
                remoteLoadout = new VisualEquipmentLoadout(
                    remote.EquippedHeadId,
                    remote.EquippedChestId,
                    remote.EquippedLegsId,
                    remote.EquippedFeetId,
                    remote.EquippedWeaponId,
                    remote.WeaponUpgradeLevel);
                if (remote.EquippedWeaponId != 0)
                {
                    _ = _art.TryGetItem(remote.EquippedWeaponId, out remoteWeapon);
                }

                if (remote.MaxHealth > 0f && remote.CurrentHealth <= 0f)
                {
                    // A dead player lies where they fell until they respawn (ADR-031).
                    clip = DownedClip;
                    norm = true;
                    eval = 1f;
                }
                else if (attacking)
                {
                    remoteAction = new ActionState(
                        remote.ActiveActionKind,
                        MathF.Max(0.05f, remote.AttackAnimDuration),
                        remote.AttackAnimRemaining,
                        facing,
                        remote.EquippedWeaponId,
                        remote.ComboStep);
                    clip = HeroAttackPose.ClipName(HeroAttackTiming.StyleOf(remote.ActiveActionKind, remote.ComboStep)) ?? remote.ActiveActionKind switch
                    {
                        ActionAnimationKind.AxeChop => "chop",
                        ActionAnimationKind.PickaxeMine => "mine",
                        ActionAnimationKind.ArcaneWave or ActionAnimationKind.SelfAura or ActionAnimationKind.StaffCast or ActionAnimationKind.BowShot => "cast",
                        ActionAnimationKind.None
                        or ActionAnimationKind.SwordSlash
                        or ActionAnimationKind.Punch
                        or ActionAnimationKind.MonsterStrike
                        or ActionAnimationKind.MonsterBowShot
                        or ActionAnimationKind.MonsterSpellCast
                        or ActionAnimationKind.MonsterHeavySlam
                        or ActionAnimationKind.ConsumeItem
                        or ActionAnimationKind.PlaceStructure
                        or ActionAnimationKind.SpinSlash
                        or _ => "slash",
                    };
                    norm = true;
                    eval = remoteAction.Progress;
                }
                else if (SwimClip(wade.State) is string swimClip)
                {
                    // The terrain is generated on the client, so a remote swimmer's depth needs no extra wire data.
                    clip = swimClip;
                }
            }
            else if (remote.Kind == EntityKind.Npc && _art.TryGetNpc(remote.ArchetypeId, out rig))
            {
                // NPC rig resolved
            }
            else if (remote.Kind == EntityKind.Monster && _art.TryGetMonster(remote.ArchetypeId, out rig))
            {
                // Night empowerment and elite status are painted on the rig (eye-glow, aura, crown), never as lights.
                overlays = (Carbon.Maps.Generators.WorldProgression.IsNightHour(_atmosphere.Hour) ? RigOverlay.NightGlow : RigOverlay.None) |
                           (rig.IsElite ? RigOverlay.EliteCrown : RigOverlay.None);
                if (remote.Attack.IsActive)
                {
                    // Server-authoritative phase timeline (ADR-013): kind-specific clip, beats aligned to phase markers.
                    // ADR-036 D10: the telegraph takes the colour of the effect the monster can apply (green for poison).
                    AttackTelegraphRenderer.Draw(batch, footPos, remote.Attack, _animTime, _monsterTints.Resolve(world, remote.ArchetypeId));
                    clip = MonsterAttackAnimator.ResolveClip(rig, remote.ArchetypeId, remote.Attack.AttackKind);
                    AttackPhaseMarks marks = rig.TryGetAnimation(clip, out CompiledVectorAnimation? attackAnim)
                        ? attackAnim.PhaseMarks
                        : AttackPhaseMarks.Default;
                    eval = MonsterAttackAnimator.ToClipTime(remote.Attack.Phase, remote.Attack.PhaseProgress, in marks);
                    norm = true;
                }
                else if (attacking)
                {
                    clip = remote.ActiveActionKind switch
                    {
                        ActionAnimationKind.MonsterBowShot => "attack_bow",
                        ActionAnimationKind.MonsterSpellCast => "attack_cast",
                        ActionAnimationKind.MonsterHeavySlam => "attack_slam",
                        ActionAnimationKind.None
                        or ActionAnimationKind.Punch
                        or ActionAnimationKind.SwordSlash
                        or ActionAnimationKind.AxeChop
                        or ActionAnimationKind.PickaxeMine
                        or ActionAnimationKind.MonsterStrike
                        or ActionAnimationKind.ConsumeItem
                        or ActionAnimationKind.PlaceStructure
                        or ActionAnimationKind.SpinSlash
                        or ActionAnimationKind.ArcaneWave
                        or ActionAnimationKind.SelfAura
                        or ActionAnimationKind.BowShot
                        or ActionAnimationKind.StaffCast
                        or _ => "attack",
                    };
                    norm = true;
                    eval = 1f - Math.Clamp(remote.AttackAnimRemaining / MathF.Max(0.05f, remote.AttackAnimDuration), 0f, 1f);
                }
            }

            if (rig is not null)
            {
                VectorRigRenderer.DrawEntityRig(
                    batch,
                    rig,
                    footPos + new Vector2(0f, wade.Sink) + (remoteAction.IsActive ? AttackLean(in remoteAction) : Vector2.Zero),
                    dir8,
                    clip,
                    eval,
                    norm,
                    0f,
                    flash,
                    equippedItem: remoteWeapon,
                    action: remoteAction,
                    swingTimer: remoteAction.IsActive ? 0f : remote.AttackAnimRemaining,
                    appearancePreset: preset,
                    equipment: remoteLoadout,
                    art: _art,
                    overlays: overlays,
                    overlayTime: _animTime + (remote.NetworkId % 11 * 0.37f));
                if (remoteAction.IsActive)
                {
                    WeaponTrailRenderer.Draw(batch, rig, remoteWeapon, footPos + new Vector2(0f, wade.Sink), dir8, in remoteAction, remote.WeaponUpgradeLevel);
                }
                WadingRenderer.DrawFront(batch, _art, world.ChunkWorld.TileSize, footPos, in wade, remote.Velocity, _animTime, wadeSeed);
                return;
            }
        }

        batch.FillCircle(remote.Position, r, new ColorRgba(250, 204, 21, 255));
    }

    private static void DrawProjectile(GeometryBatch batch, in RemoteEntityView proj, List<PointLight> lights)
    {
        Vector2 vel = proj.Velocity.LengthSquared() > 1e-4f
            ? Vector2.Normalize(proj.Velocity)
            : Vector2.UnitX;
        Vector2 perp = new(-vel.Y, vel.X);
        Vector2 pos = proj.Position + new Vector2(0f, -8f);

        switch (proj.ArchetypeId)
        {
            case 2: // ArcaneBolt (blue/cyan energy orb + trail)
                batch.DrawLine(pos - (vel * 12f), pos, new ColorRgba(56, 189, 248, 175), 3.4f);
                batch.FillCircle(pos, 5.2f, new ColorRgba(56, 189, 248, 225), 10);
                batch.FillCircle(pos, 2.8f, new ColorRgba(224, 242, 254, 255), 8);
                lights.Add(new PointLight(pos, 48f, new ColorRgba(56, 189, 248), 0.55f));
                break;

            case 4: // DarkBolt (Ash Wraith violet flame bolt)
                batch.DrawLine(pos - (vel * 11f), pos, new ColorRgba(168, 85, 247, 185), 3.2f);
                batch.FillCircle(pos, 5.0f, new ColorRgba(147, 51, 234, 230), 10);
                batch.FillCircle(pos, 2.4f, new ColorRgba(245, 208, 254, 255), 8);
                lights.Add(new PointLight(pos, 42f, new ColorRgba(192, 132, 252), 0.45f));
                break;

            case 6: // Shaman Hex Bolt (bright violet/magenta hex orb)
                batch.DrawLine(pos - (vel * 13f), pos, new ColorRgba(217, 70, 239, 195), 3.6f);
                batch.FillCircle(pos, 5.8f, new ColorRgba(168, 85, 247, 235), 12);
                batch.FillCircle(pos, 2.8f, new ColorRgba(250, 232, 255, 255), 8);
                lights.Add(new PointLight(pos, 52f, new ColorRgba(217, 70, 239), 0.55f));
                break;

            case 3: // CrystalArrow (glowing prism arrow)
                batch.DrawLine(pos - (vel * 11f), pos + (vel * 6f), new ColorRgba(34, 211, 238, 235), 2.2f);
                batch.FillTriangle(pos + (vel * 9f), pos + (vel * 2f) + (perp * 3.0f), pos + (vel * 2f) - (perp * 3.0f), new ColorRgba(224, 242, 254, 255));
                lights.Add(new PointLight(pos, 36f, new ColorRgba(34, 211, 238), 0.35f));
                break;

            case 5: // BoneArrow (Skeleton Archer crimson-tipped arrow)
                batch.DrawLine(pos - (vel * 10f), pos + (vel * 5f), new ColorRgba(226, 232, 240, 230), 1.8f);
                batch.FillTriangle(pos + (vel * 8.5f), pos + (vel * 2f) + (perp * 2.6f), pos + (vel * 2f) - (perp * 2.6f), new ColorRgba(244, 63, 94, 255));
                break;

            default: // 1: Hunting Arrow
                batch.DrawLine(pos - (vel * 10f), pos + (vel * 5f), new ColorRgba(217, 119, 6, 240), 1.9f);
                batch.FillTriangle(pos + (vel * 8.5f), pos + (vel * 2f) + (perp * 2.8f), pos + (vel * 2f) - (perp * 2.8f), new ColorRgba(248, 250, 252, 255));
                break;
        }
    }

    private void DrawActionTrails(IWorldReadModel world, Vector2 playerPos)
    {
        ActionState activeAction = world.CurrentAction;
        if (activeAction.IsActive)
        {
            if (ReleaseVfxProgress(activeAction.Kind, activeAction.ComboStep, activeAction.Progress) is float localVfx)
            {
                ActionVfxRenderer.DrawActionTrail(
                    _geometryBatch,
                    playerPos,
                    activeAction.Kind,
                    activeAction.AimDirection,
                    localVfx,
                    activeAction.ComboStep,
                    activeAction.EquippedItemId);
            }
        }
        else if (world.AttackSwingTimer > 0f)
        {
            ActionVfxRenderer.DrawActionTrail(
                _geometryBatch,
                playerPos,
                ActionAnimationKind.SwordSlash,
                world.LastAttackDirection,
                1f - Math.Clamp(world.AttackSwingTimer / 0.24f, 0f, 1f),
                0);
        }

        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView r = remotes[i];
            if (r.AttackAnimRemaining > 0f)
            {
                float rProg = 1f - Math.Clamp(r.AttackAnimRemaining / MathF.Max(0.05f, r.AttackAnimDuration), 0f, 1f);
                ActionAnimationKind remoteKind = r.ActiveActionKind != ActionAnimationKind.None
                    ? r.ActiveActionKind
                    : (r.Kind == EntityKind.Player ? ActionAnimationKind.SwordSlash : ActionAnimationKind.MonsterStrike);
                if (ReleaseVfxProgress(remoteKind, r.ComboStep, rProg) is float remoteVfx)
                {
                    ActionVfxRenderer.DrawActionTrail(
                        _geometryBatch,
                        r.Position,
                        remoteKind,
                        r.FacingDirection,
                        remoteVfx,
                        r.ComboStep,
                        r.EquippedWeaponId);
                }
            }
        }
    }

    /// <summary>
    /// Gets the hero rig clip for the local player's action (<c>base_hero.cvg.json</c>). Public so the offline art
    /// showcase (<c>--showcase</c>) renders hero attack filmstrips through the same mapping as the world.
    /// </summary>
    /// <param name="action">The local action.</param>
    /// <param name="attackSwingTimer">Remaining legacy swing seconds.</param>
    /// <param name="moving">Whether the player is moving.</param>
    /// <returns>The clip name.</returns>
    public static string ResolveCharacterClip(ActionState action, float attackSwingTimer, bool moving)
    {
        if (action.IsActive && HeroAttackPose.ClipName(HeroAttackTiming.StyleOf(action.Kind, action.ComboStep)) is string attackClip)
        {
            return attackClip;
        }

        if (action.IsActive)
        {
            return action.Kind switch
            {
                ActionAnimationKind.AxeChop => "chop",
                ActionAnimationKind.PickaxeMine => "mine",
                ActionAnimationKind.SwordSlash
                or ActionAnimationKind.MonsterStrike
                or ActionAnimationKind.MonsterBowShot
                or ActionAnimationKind.MonsterSpellCast
                or ActionAnimationKind.MonsterHeavySlam
                or ActionAnimationKind.SpinSlash => "slash",
                ActionAnimationKind.ArcaneWave or ActionAnimationKind.SelfAura or ActionAnimationKind.StaffCast or ActionAnimationKind.BowShot => "cast",
                ActionAnimationKind.PlaceStructure => "chop",
                ActionAnimationKind.ConsumeItem => "idle",
                ActionAnimationKind.Punch or ActionAnimationKind.None or _ => moving ? "walk" : "idle",
            };
        }

        return attackSwingTimer > 0f ? "slash" : (moving ? "walk" : "idle");
    }

    /// <summary>
    /// Gets the progress to play an action's body-centred VFX at, or <see langword="null"/> to skip it. Melee and tool
    /// swings draw <see cref="WeaponTrailRenderer"/> along the weapon instead; the bow release streak and the staff burst
    /// start at release (contact), not at the first frame of the draw; other actions play as before.
    /// </summary>
    /// <param name="kind">Action kind.</param>
    /// <param name="comboStep">Combo step.</param>
    /// <param name="progress">Normalized action progress.</param>
    /// <returns>VFX progress, or <see langword="null"/>.</returns>
    public static float? ReleaseVfxProgress(ActionAnimationKind kind, byte comboStep, float progress)
    {
        HeroAttackStyle style = HeroAttackTiming.StyleOf(kind, comboStep);
        if (style == HeroAttackStyle.None)
        {
            return progress;
        }

        if (style is not (HeroAttackStyle.BowShot or HeroAttackStyle.StaffCast))
        {
            return null;
        }

        float contact = HeroAttackTiming.For(style).ContactFraction;
        return progress < contact ? null : (progress - contact) / MathF.Max(1e-4f, 1f - contact);
    }

    /// <summary>World-unit shift of a hero toward its aim for the attack lean (zero for non-attacks).</summary>
    /// <param name="action">Active action.</param>
    /// <returns>Offset added to the foot position.</returns>
    public static Vector2 AttackLean(in ActionState action)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(action.Kind, action.ComboStep));
        Vector2 aim = action.AimDirection;
        return timing.Total > 0f && aim.LengthSquared() > 1e-4f
            ? Vector2.Normalize(aim) * timing.LeanAt(action.Progress)
            : Vector2.Zero;
    }

    private static void DrawDebugSolids(GeometryBatch batch, ChunkWorld2D chunkWorld, float tileSize, float minX, float minY, float maxX, float maxY)
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
                    if (chunk.GetTile(lx, ly).IsSolid)
                    {
                        float tileWorldX = cMinX + (lx * tileSize);
                        batch.FillRectangle(new RectangleF(tileWorldX + 2f, tileWorldY + 2f, tileSize - 4f, tileSize - 4f), new ColorRgba(0, 0, 0, 70));
                    }
                }
            }
        }
    }

    private static void DrawTargetTileHighlight(GeometryBatch batch, IWorldReadModel world, ChunkWorld2D chunkWorld, float tileSize, int tileX, int tileY)
    {
        float x = tileX * tileSize;
        float y = tileY * tileSize;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot active = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        bool isPlaceable = active.Quantity > 0 &&
            world.Items.Rules.Items.TryGet(active.ItemId, out ItemArchetype? archetype) &&
            archetype is { Category: ItemCategory.Placeable };

        if (isPlaceable)
        {
            MapTile tile = chunkWorld.GetTileAtWorld(tileX, tileY);
            bool occupied = chunkWorld.HasSolidPropAt(tileX, tileY) ||
                chunkWorld.HasGrowingTreeAt(tileX, tileY) ||
                chunkWorld.Buildings.TryGetStructure(tileX, tileY, out _) ||
                tile.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater ||
                tile.IsSolid;
            ColorRgba tint = occupied ? new ColorRgba(239, 68, 68, 120) : new ColorRgba(34, 197, 94, 110);
            ColorRgba border = occupied ? new ColorRgba(248, 113, 113, 225) : new ColorRgba(74, 222, 128, 225);
            batch.FillRectangle(new RectangleF(x + 2f, y + 2f, tileSize - 4f, tileSize - 4f), tint);
            batch.DrawRectangle(new RectangleF(x + 1f, y + 1f, tileSize - 2f, tileSize - 2f), border, 1.5f);
            return;
        }

        if (chunkWorld.TryGetPropAt(tileX, tileY, out MapProp aimedProp) &&
            !aimedProp.Destructibility.IsDestroyed &&
            aimedProp.Destructibility.IsDestructible)
        {
            batch.DrawRectangle(new RectangleF(x + 2f, y + 2f, tileSize - 4f, tileSize - 4f), new ColorRgba(250, 204, 21, 195), 1.6f);
        }
    }

    private static void DrawInteriorRoomMask(GeometryBatch batch, RectangleF room, float interiorBlend, float minX, float minY, float maxX, float maxY)
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

    private readonly record struct DestroyedPropRenderItem(in MapProp Prop);

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
        public readonly Vector2 ShakeOffset;
        public readonly float HitFlash;
        public readonly Entity Entity;
        public readonly LandmarkPiece Piece;
        public readonly RemoteEntityView Remote;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, in MapProp prop, Vector2 shakeOffset, float hitFlash)
        {
            this.SortY = sortY;
            this.Kind = GroundItemKind.Prop;
            this.Prop = prop;
            this.ShakeOffset = shakeOffset;
            this.HitFlash = hitFlash;
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
        public readonly float ShakeX;
        public readonly float DamageTilt;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CanopyRenderItem(float sortY, in MapProp prop, float shakeX, float damageTilt)
        {
            this.SortY = sortY;
            this.Prop = prop;
            this.ShakeX = shakeX;
            this.DamageTilt = damageTilt;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(CanopyRenderItem other) => this.SortY.CompareTo(other.SortY);
    }
}
