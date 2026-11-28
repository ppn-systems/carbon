// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Application.World;
using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Settlements;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Audit SA-10: which chunks a client may request. The client asks for the chunks around its own position, but the
/// request carries only coordinates, and serving a village chunk builds the village under the world lock first.
/// </summary>
public sealed class ChunkRequestReachTests
{
    [Fact]
    public void TryCreateChunkDeltaPacket_AVillageFarFromEveryPlayer_IsBuiltAndServed()
    {
        // Characterisation: any coordinates are served, and the far village is generated for the request.
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1);
        AuthoritativeWorld world = harness.World;
        (int cx, int cy) = FarVillageChunk(world, minChunks: 12);
        world.Chunks.TryGetDelta(cx, cy, out ChunkDelta? before).Should().BeFalse("nobody has been near the village");
        _ = before;

        world.TryCreateChunkDeltaPacket(cx, cy, 0U, out MapChunkDelta? packet).Should().BeTrue("any coordinates are served");
        using (packet)
        {
            packet!.StructureIndices.Should().NotBeEmpty("the village was built for the request");
        }
    }

    // The chunk of a village building at least minChunks away from the spawn chunk, on both axes.
    internal static (int ChunkX, int ChunkY) FarVillageChunk(AuthoritativeWorld world, int minChunks)
    {
        ProceduralWorldGenerator generator = world.Generator;
        int spawnCx = Chunk2D.ToChunkCoord((int)MathF.Floor(world.DefaultSpawnWorldPosition.X / world.TileSize));
        int spawnCy = Chunk2D.ToChunkCoord((int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / world.TileSize));
        for (int ring = 1; ring < 12; ring++)
        {
            for (int sy = -ring; sy <= ring; sy++)
            {
                for (int sx = -ring; sx <= ring; sx++)
                {
                    foreach (VillagePlot plot in generator.Landmarks.GetLayout(sx, sy).Plots)
                    {
                        int cx = Chunk2D.ToChunkCoord(plot.OriginX);
                        int cy = Chunk2D.ToChunkCoord(plot.OriginY);
                        if (Math.Abs(cx - spawnCx) >= minChunks && Math.Abs(cy - spawnCy) >= minChunks)
                        {
                            return (cx, cy);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("no village far from spawn for the test seed");
    }
}
