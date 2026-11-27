// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Streaming;

namespace Carbon.Maps.Settlements;

/// <summary>
/// Puts the buildings and paved streets of a village's plan on the world's building grid as real structures owned by
/// <see cref="StructureOwners.Village"/> (ADR-025). Every placement goes through <see cref="BuildingGrid.PlaceStructure"/>
/// and <see cref="ConstructionLedger"/>, so the structures are written to chunk deltas, persisted and replicated exactly
/// like player builds. Idempotent: tiles that already hold a structure or a construction site are left alone, so running
/// it again (after a restart, or for every chunk of the village) changes nothing. Server only; not thread-safe (world lock).
/// </summary>
public sealed class VillageMaterializer
{
    private readonly ChunkWorld2D _world;
    private readonly VillageBuildingCatalog _villages;
    private readonly StructureCatalog _structures;
    private readonly List<VillagePlacement> _scratch = new(512);

    /// <summary>Initializes a new instance of the <see cref="VillageMaterializer"/> class.</summary>
    /// <param name="world">The world to build in.</param>
    /// <param name="villages">Village templates (normally <see cref="VillageBuildingCatalog.Default"/>).</param>
    /// <param name="structures">Structure definitions.</param>
    public VillageMaterializer(ChunkWorld2D world, VillageBuildingCatalog villages, StructureCatalog structures)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _villages = villages ?? throw new ArgumentNullException(nameof(villages));
        _structures = structures ?? throw new ArgumentNullException(nameof(structures));
    }

    /// <summary>Gets the village templates.</summary>
    public VillageBuildingCatalog Villages => _villages;

    /// <summary>Gets the site (<see cref="PointOfInterestResolver.SiteSize"/> tiles) that owns a chunk.</summary>
    public static (int SiteX, int SiteY) SiteOfChunk(int chunkX, int chunkY) =>
        (FloorDiv(chunkX * Chunk2D.Size, PointOfInterestResolver.SiteSize), FloorDiv(chunkY * Chunk2D.Size, PointOfInterestResolver.SiteSize));

    /// <summary>Gets the village plots of a site (empty when the site holds no village).</summary>
    public ReadOnlySpan<VillagePlot> PlotsOf(int siteX, int siteY) => _world.Generator.Landmarks.GetLayout(siteX, siteY).Plots;

    /// <summary>
    /// Builds every missing structure of the site's village.
    /// </summary>
    /// <param name="siteX">Site X.</param>
    /// <param name="siteY">Site Y.</param>
    /// <param name="touchedChunks">Receives the packed keys of chunks that changed (optional).</param>
    /// <returns>Number of structure tiles placed.</returns>
    public int Materialize(int siteX, int siteY, ISet<long>? touchedChunks = null)
    {
        LandmarkLayout layout = _world.Generator.Landmarks.GetLayout(siteX, siteY);
        ReadOnlySpan<VillagePlot> plots = layout.Plots;
        int placed = 0;
        for (int i = 0; i < plots.Length; i++)
        {
            _scratch.Clear();
            VillageBlueprint.Expand(_villages, _structures, plots[i], _scratch);

            // Walls and doors first so autotiling sees whole outlines, then floors, then furniture over them.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (VillagePlacement p in _scratch)
                {
                    bool furniture = p.FloorBeneath != StructureType.None || IsFurnitureType(p.Type);
                    if (pass == 0 == furniture)
                    {
                        continue;
                    }

                    if (this.TryPlace(p))
                    {
                        placed++;
                        _ = touchedChunks?.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(p.X), Chunk2D.ToChunkCoord(p.Y)));
                    }
                }
            }
        }

        // The square, the streets and the door paths, paved with the village's floor (ADR-027 revision 3).
        if (plots.Length > 0)
        {
            StructureType paving = VillageBlueprint.PavingType(_villages, plots[0].Style);
            foreach ((int x, int y) in layout.PavedTiles)
            {
                if (this.TryPlace(new VillagePlacement(x, y, paving, StructureRotation.North, x, y, StructureType.None)))
                {
                    placed++;
                    _ = touchedChunks?.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y)));
                }
            }
        }

        return placed;
    }

    private static bool IsFurnitureType(StructureType type) =>
        BuildingGrid.CreateBlueprint(type).Category is StructureCategory.Furniture or StructureCategory.Station;

    private bool TryPlace(in VillagePlacement p)
    {
        if (_world.Buildings.TryGetStructure(p.X, p.Y, out StructureTile existing) && existing.IsOccupied)
        {
            return false;
        }

        if (_world.Construction.IsReserved(p.X, p.Y))
        {
            return false;
        }

        StructureTile tile = _structures.TryGet(p.Type, out StructureDefinition definition)
            ? definition.CreateTile()
            : BuildingGrid.CreateBlueprint(p.Type);
        if (!tile.IsOccupied)
        {
            return false;
        }

        _world.Buildings.PlaceStructure(p.X, p.Y, in tile);
        _world.Construction.SetRecord(p.X, p.Y, new StructureRecord(
            StructureOwners.Village,
            (byte)(p.X - p.AnchorX),
            (byte)(p.Y - p.AnchorY),
            p.Rotation,
            p.FloorBeneath));
        return true;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
