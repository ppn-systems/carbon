// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Maps.Roads;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-045 D1: a streaming step plans the road sites of the next one. When a player moves at most one chunk between
/// steps, every chunk the next step loads must read only sites this step's prewarm already queued, or the next step
/// races the background worker for them on the calling thread (the flake in <see cref="RoadPrewarmTests"/>).
/// Pure geometry: the same rectangles <see cref="ChunkWorld2D.UpdateAroundPosition"/> and the road query use.
/// </summary>
public sealed class RoadPrewarmCoverageTests
{
    private const int LoadRadius = 2; // the server's StreamChunkRadius

    [Fact]
    public void RoadPrewarmTiles_OneChunkStepInAnyDirection_EveryLoadedChunkReadsOnlySitesQueuedAStepAhead()
    {
        int uncovered = 0;

        // Every start chunk within one road site (7 chunks), so every way the load radius can meet a site boundary.
        for (int cy = 0; cy < 7; cy++)
        {
            for (int cx = 0; cx < 7; cx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        uncovered += UncoveredChunks(cx, cy, cx + dx, cy + dy);
                    }
                }
            }
        }

        uncovered.Should().Be(0, "the one-chunk margin covers every chunk a one-chunk step loads, site boundary or not");
    }

    // Chunks loaded at the step centred on (toX, toY) whose road query reads a site outside the prewarm of (fromX, fromY).
    private static int UncoveredChunks(int fromX, int fromY, int toX, int toY)
    {
        (int pMinX, int pMinY, int pMaxX, int pMaxY) = ChunkWorld2D.RoadPrewarmTiles(fromX, fromY, LoadRadius);
        (int qx0, int qy0, int qx1, int qy1) = RoadNetwork.QueriedSites(pMinX, pMinY, pMaxX, pMaxY);
        int uncovered = 0;
        for (int y = toY - LoadRadius; y <= toY + LoadRadius; y++)
        {
            for (int x = toX - LoadRadius; x <= toX + LoadRadius; x++)
            {
                if (Math.Max(Math.Abs(x - fromX), Math.Abs(y - fromY)) <= LoadRadius)
                {
                    continue; // loaded by the earlier step already
                }

                int x0 = x * Chunk2D.Size;
                int y0 = y * Chunk2D.Size;
                (int sx0, int sy0, int sx1, int sy1) = RoadNetwork.QueriedSites(x0, y0, x0 + Chunk2D.Size - 1, y0 + Chunk2D.Size - 1);
                if (sx0 < qx0 || sy0 < qy0 || sx1 > qx1 || sy1 > qy1)
                {
                    uncovered++;
                }
            }
        }

        return uncovered;
    }
}
