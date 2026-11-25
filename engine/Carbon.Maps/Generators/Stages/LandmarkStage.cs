// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;

namespace Carbon.Maps.Generators.Stages;

/// <summary>Adds the points of interest (villages, harbours, camps, shrines) centred in the chunk.</summary>
public sealed class LandmarkStage : IChunkStage
{
    private readonly PointOfInterestResolver _landmarks;

    /// <summary>Initializes a new instance of the <see cref="LandmarkStage"/> class.</summary>
    /// <param name="landmarks">The landmark resolver.</param>
    public LandmarkStage(PointOfInterestResolver landmarks) =>
        _landmarks = landmarks ?? throw new ArgumentNullException(nameof(landmarks));

    /// <inheritdoc/>
    public void Populate(Chunk2D chunk, ChunkContext context)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        int x0 = context.ChunkX * Chunk2D.Size;
        int y0 = context.ChunkY * Chunk2D.Size;
        int sx = FloorDiv(x0, PointOfInterestResolver.SiteSize);
        int sy = FloorDiv(y0, PointOfInterestResolver.SiteSize);

        // SiteMargin = 24 keeps centers strictly inside their owning site, so only owning site (sx, sy) can hold POIs
        // except very wide harbor piers near a boundary. Checking only (sx, sy) is fast and avoids redundant lookups.
        PointOfInterest poi = _landmarks.GetSite(sx, sy);
        if (poi.Type != PointOfInterestType.None)
        {
            int pad = poi.Radius + 4;
            if (poi.TileX >= x0 - pad && poi.TileX < x0 + Chunk2D.Size + pad &&
                poi.TileY >= y0 - pad && poi.TileY < y0 + Chunk2D.Size + pad)
            {
                chunk.AddPointOfInterest(poi);
            }
        }
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
