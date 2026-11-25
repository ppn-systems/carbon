// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public void GenerateChunk_FromManyThreads_MatchesSequentialOutput()
    {
        const int N = 48;
        ulong[] sequential = new ulong[N];
        ProceduralWorldGenerator seqGen = new(9UL);
        for (int i = 0; i < N; i++)
        {
            sequential[i] = Hash(seqGen.GenerateChunk(i % 8 * 9, i / 8 * 9));
        }

        // A fresh generator shared by all threads: river regions, landmark sites and layouts are built
        // concurrently and must come out exactly as when built one at a time.
        ProceduralWorldGenerator shared = new(9UL);
        ulong[] parallel = new ulong[N];
        _ = Parallel.For(0, N, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            parallel[i] = Hash(shared.GenerateChunk(i % 8 * 9, i / 8 * 9)));

        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void ChunkWorld_ConcurrentReadsAndEdits_StayConsistent()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(5UL));
        (int sx, int sy) = world.SpawnTile;
        MapTile edited = world.GetTileAtWorld(sx, sy) with { Variant = 3 };

        _ = Parallel.For(0, 64, i =>
        {
            if (i % 8 == 0)
            {
                world.SetTileAtWorld(sx + i, sy, edited);
            }

            _ = world.GetTileAtWorld(sx + i, sy + (i % 5));
            _ = world.PeekTileAtWorld(sx - i, sy - i);
            world.UpdateAroundPosition(world.SpawnWorldPosition, 2, 4);
        });

        for (int i = 0; i < 64; i += 8)
        {
            Assert.Equal(edited, world.GetTileAtWorld(sx + i, sy));
        }
    }

    private static ulong Hash(Chunk2D chunk)
    {
        ulong h = 1469598103934665603UL;
        for (int y = 0; y < Chunk2D.Size; y++)
        {
            for (int x = 0; x < Chunk2D.Size; x++)
            {
                h = (h ^ (ulong)chunk.GetTile(x, y).GetHashCode()) * 1099511628211UL;
            }
        }

        for (int i = 0; i < chunk.Props.Count; i++)
        {
            h = (h ^ (ulong)chunk.Props[i].GetHashCode()) * 1099511628211UL;
        }

        return (h ^ (ulong)chunk.PointsOfInterest.Count) * 1099511628211UL;
    }
}
