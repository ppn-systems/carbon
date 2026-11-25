// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed partial class DeltaSyncAndDeterminismTests
{
    [Fact]
    public void MapsSource_UsesNoPlatformDependentMath()
    {
        // Sin/Cos/Log/Exp/Pow/Atan come from the platform math library and may differ in the last bit between
        // x64, ARM64 and WebAssembly; server and client would then generate different worlds. Use
        // DeterministicMath or exact tables instead. (+ - * / Sqrt Abs Floor Min Max are exact everywhere.)
        string root = FindMapsSource();
        string[] offenders =
        [
            .. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && BannedMath().IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}"),
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void ChunkDelta_SerializeRoundTrip_RebuildsTheSameChunk()
    {
        ChunkWorld2D server = new(new ProceduralWorldGenerator(42UL));
        (int sx, int sy) = server.SpawnTile;
        MapTile edited = server.GetTileAtWorld(sx + 3, sy) with { Variant = 2, HeightLevel = 1 };
        server.SetTileAtWorld(sx + 3, sy, edited);
        (int px, int py) = FindDestructibleProp(server);
        Assert.True(server.TryDamagePropAt(px, py, 9999f, out _));

        ChunkWorld2D client = new(new ProceduralWorldGenerator(42UL));
        foreach (ChunkDelta delta in server.Deltas)
        {
            byte[] bytes = delta.Serialize();
            ChunkDelta decoded = ChunkDelta.Deserialize(bytes);
            Assert.Equal(bytes, decoded.Serialize());
            Assert.Equal(delta.Revision, decoded.Revision);
            client.ImportDelta(decoded);
        }

        Assert.Equal(edited, client.GetTileAtWorld(sx + 3, sy));
        Chunk2D a = server.GetOrCreateChunk(Chunk2D.ToChunkCoord(px), Chunk2D.ToChunkCoord(py));
        Chunk2D b = client.GetOrCreateChunk(Chunk2D.ToChunkCoord(px), Chunk2D.ToChunkCoord(py));
        Assert.Equal(a.Props, b.Props);
    }

    [Fact]
    public void ChunkDelta_FromOlderGenerator_KeepsTileEditsAndDropsPropStates()
    {
        ChunkDelta delta = new(1, 2);
        MapTile tile = new(Biomes.BiomeType.BeachSand, 0.5f, 0.5f, 0.5f, false, 1, 0);
        delta.SetTile(4, 5, tile);
        delta.SetPropState(6, 7, PropDestructibility.Create(10f) with { IsDestroyed = true });

        byte[] bytes = delta.Serialize();
        bytes[1] = ProceduralWorldGenerator.GeneratorVersion - 1; // recorded by the previous generator
        bytes[2] = 0;
        ChunkDelta old = ChunkDelta.Deserialize(bytes);

        ChunkDelta migrated = old.MigrateTo(ProceduralWorldGenerator.GeneratorVersion);
        Assert.Equal(ProceduralWorldGenerator.GeneratorVersion, migrated.GeneratorVersion);
        Assert.Equal(tile, migrated.Tiles[ChunkDelta.ToLocalIndex(4, 5)]);
        Assert.Empty(migrated.PropStates);
        Assert.True(migrated.Revision > old.Revision);
        Assert.Same(migrated, migrated.MigrateTo(ProceduralWorldGenerator.GeneratorVersion));
    }

    [Fact]
    public void ChunkDelta_Deserialize_RejectsCorruptData()
    {
        ChunkDelta delta = new(0, 0);
        delta.SetTile(1, 1, new MapTile(Biomes.BiomeType.LushPlains, 0.5f, 0.5f, 0.5f, false, 0, 0));
        byte[] bytes = delta.Serialize();

        _ = Assert.Throws<FormatException>(() => ChunkDelta.Deserialize(bytes.AsSpan(0, bytes.Length - 3)));
        bytes[0] = 99;
        _ = Assert.Throws<FormatException>(() => ChunkDelta.Deserialize(bytes));
    }

    [GeneratedRegex(@"\bMathF?\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Log|Log2|Log10|Exp|Pow|Cbrt|Sinh|Cosh|Tanh)\s*\(")]
    private static partial Regex BannedMath();

    private static string FindMapsSource()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "engine", "Carbon.Maps")))
        {
            dir = dir.Parent;
        }

        return dir == null
            ? throw new DirectoryNotFoundException("engine/Carbon.Maps not found above the test directory.")
            : Path.Combine(dir.FullName, "engine", "Carbon.Maps");
    }

    private static (int X, int Y) FindDestructibleProp(ChunkWorld2D world)
    {
        for (int cy = -3; cy <= 3; cy++)
        {
            for (int cx = -3; cx <= 3; cx++)
            {
                Chunk2D chunk = world.GetOrCreateChunk(cx, cy);
                foreach (MapProp prop in chunk.Props)
                {
                    if (prop.Destructibility.IsDestructible)
                    {
                        return ((cx * Chunk2D.Size) + prop.LocalTileX, (cy * Chunk2D.Size) + prop.LocalTileY);
                    }
                }
            }
        }

        throw new InvalidOperationException("No destructible prop near the origin.");
    }
}
