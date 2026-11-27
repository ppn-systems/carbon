// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Maps.S2C;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Client-side construction view: tracks sites and deconstructions pushed by the server and previews placements with
/// the same <see cref="BuildingService"/> rules the server uses, from the server's synced <c>structures.json</c>
/// (<see cref="SyncedStructureCatalog"/>). Nothing here mutates authoritative state.
/// </summary>
public sealed partial class MultiplayerSession
{
    private readonly SyncedStructureCatalog _structures = new();

    /// <inheritdoc/>
    public float BuildReachTiles => _structures.Current?.Rules.PlacementReachTiles ?? 0f;

    private readonly ConstructionMarkerBook _constructionMarkers = new();
    private readonly BuildPermissionTable _previewPermissions = new();
    private BuildingService? _previewService;
    private ChunkWorld2D? _previewWorld;
    private StructureCatalog? _previewCatalog;
    private IReadOnlyList<RemoteEntityView> _previewRemotes = [];
    private int _previewLocalTileX = int.MinValue;
    private int _previewLocalTileY = int.MinValue;

    // ADR-045 D7: a generator caches its own world's river regions, so the session keeps one per seed and catalog. The
    // menu backdrop and the world it enters share it, and so do re-entries into the same world: none rebuilds regions
    // the other already built.
    private readonly Lock _generatorGate = new();
    private ProceduralWorldGenerator? _generator;
    private ulong _generatorSeed;
    private StructureCatalog? _generatorStructures;

    /// <summary>
    /// Gets the local world generator for <paramref name="seed"/>, creating it the first time. Villages are planned from
    /// <c>structures.json</c> footprints, so the generator takes the server's synced catalog; entering the world waits for
    /// the config sync. The same seed and catalog return the same generator (the backdrop's, for the world it shows).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown before the first config sync.</exception>
    internal ProceduralWorldGenerator CreateWorldGenerator(ulong seed)
    {
        StructureCatalog structures = _structures.Current ?? throw new InvalidOperationException("structures.json has not been synced from the server yet.");
        lock (_generatorGate)
        {
            if (_generator is null || _generatorSeed != seed || !ReferenceEquals(_generatorStructures, structures))
            {
                _generator = new ProceduralWorldGenerator(seed, structures: structures);
                _generatorSeed = seed;
                _generatorStructures = structures;
            }

            return _generator;
        }
    }

    /// <inheritdoc/>
    public ProceduralWorldGenerator? TryCreateWorldGenerator(ulong seed) =>
        _structures.Current is null ? null : this.CreateWorldGenerator(seed);

    /// <inheritdoc/>
    public void CopyConstructionMarkers(List<ConstructionMarker> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_worldGate)
        {
            _constructionMarkers.CopyTo(destination);
        }
    }

    /// <inheritdoc/>
    public BuildResultCode PreviewPlacement(int tileX, int tileY, StructureType type, StructureRotation rotation, out int width, out int height)
    {
        width = 1;
        height = 1;
        if (_structures.Current is not { } catalog || !catalog.TryGet(type, out StructureDefinition definition))
        {
            return BuildResultCode.UnknownStructure;
        }

        (width, height) = definition.GetFootprint(rotation);

        // Snapshot entity positions before taking the world lock (both properties synchronize internally).
        IReadOnlyList<RemoteEntityView> remotes = this.RemoteEntities;
        Vector2 local = this.LocalPlayerInterpolatedPosition;

        lock (_worldGate)
        {
            if (_chunkWorld is null)
            {
                return BuildResultCode.NotFound;
            }

            if (!ReferenceEquals(_previewWorld, _chunkWorld) || !ReferenceEquals(_previewCatalog, catalog) || _previewService is null)
            {
                _previewWorld = _chunkWorld;
                _previewCatalog = catalog;
                _previewService = BuildingService.ForWorld(_chunkWorld, catalog, _previewPermissions, this.IsEntityOnTileForPreview);
            }

            _previewRemotes = remotes;
            _previewLocalTileX = (int)MathF.Floor(local.X / _tileSize);
            _previewLocalTileY = (int)MathF.Floor(local.Y / _tileSize);
            return _previewService.ValidatePlacement(tileX, tileY, type, rotation);
        }
    }

    private bool IsEntityOnTileForPreview(int tileX, int tileY)
    {
        if (tileX == _previewLocalTileX && tileY == _previewLocalTileY)
        {
            return true;
        }

        for (int i = 0; i < _previewRemotes.Count; i++)
        {
            RemoteEntityView remote = _previewRemotes[i];
            if (remote.Kind is EntityKind.LootDrop or EntityKind.Projectile)
            {
                continue;
            }

            if ((int)MathF.Floor(remote.Position.X / _tileSize) == tileX && (int)MathF.Floor(remote.Position.Y / _tileSize) == tileY)
            {
                return true;
            }
        }

        return false;
    }

    private void OnMapConstructionChanged(MapConstructionChanged pkt)
    {
        ConstructionMarker marker = new(
            pkt.TileX,
            pkt.TileY,
            Math.Max(1, pkt.Size & 0x0F),
            Math.Max(1, pkt.Size >> 4),
            (StructureType)pkt.StructureType,
            (StructureRotation)(pkt.Rotation & 0x03),
            (ConstructionStage)pkt.Stage,
            Math.Clamp(pkt.Progress, 0f, 1f),
            pkt.IsDeconstruction);

        // The marker is a copy, so the mutation can run later on the main thread (audit TS-2).
        this.PostWorldMutation(() => this.ApplyConstructionMarker(marker));
    }

    private void ApplyConstructionMarker(ConstructionMarker marker)
    {
        lock (_worldGate)
        {
            _constructionMarkers.Apply(in marker);
            if (_chunkWorld is null || marker.IsDeconstruction)
            {
                return;
            }

            // Keep the client's ledger in step so placement previews see fresh sites before the next chunk refetch.
            if (marker.Stage is ConstructionStage.Blueprint or ConstructionStage.Frame)
            {
                float work = _structures.Current is { } catalog && catalog.TryGet(marker.Type, out StructureDefinition definition) ? definition.WorkAmount : 0f;
                _chunkWorld.Construction.ImportSite(marker.AnchorX, marker.AnchorY, marker.Type, marker.Rotation, marker.Width, marker.Height, marker.Stage, marker.Progress * work);
            }
            else
            {
                _ = _chunkWorld.Construction.ForgetSite(marker.AnchorX, marker.AnchorY);
            }
        }
    }

    /// <summary>Refreshes site markers from a chunk delta just imported under the world lock.</summary>
    private void OnChunkDeltaImportedLocked(ChunkDelta delta)
    {
        if (_structures.Current is { } catalog)
        {
            _constructionMarkers.ReplaceChunkSites(delta.ChunkX, delta.ChunkY, delta.Sites, catalog);
        }
    }
}
