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
        // Sites are larger than chunks, so check the 3x3 sites around this one.
        int siteX = FloorDiv(context.ChunkX * Chunk2D.Size, PointOfInterestResolver.SiteSize);
        int siteY = FloorDiv(context.ChunkY * Chunk2D.Size, PointOfInterestResolver.SiteSize);
        for (int sy = siteY - 1; sy <= siteY + 1; sy++)
        {
            for (int sx = siteX - 1; sx <= siteX + 1; sx++)
            {
                PointOfInterest poi = _landmarks.GetSite(sx, sy);
                if (poi.Type != PointOfInterestType.None &&
                    Chunk2D.ToChunkCoord(poi.TileX) == context.ChunkX && Chunk2D.ToChunkCoord(poi.TileY) == context.ChunkY)
                {
                    chunk.AddPointOfInterest(poi);
                }
            }
        }
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
