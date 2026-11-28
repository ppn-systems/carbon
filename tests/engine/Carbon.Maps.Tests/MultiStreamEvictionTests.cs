// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// The server streams once per player, each with its own position (<c>stream</c> = its connection). Eviction must keep
/// every chunk some player still keeps, or two players far apart evict and regenerate each other's chunks every step.
/// </summary>
public sealed class MultiStreamEvictionTests
{
    private const float TileSize = 32f;
    private const int LoadRadius = 2;
    private const int EvictRadius = 8;
    private const int Apart = 30; // chunks between the two players, beyond 2 x EvictRadius

    [Fact]
    public void UpdateAroundPosition_TwoPlayersFarApart_EvictsTheOtherPlayersChunks()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);
        world.TryGetChunk(Apart, 0, out _).Should().BeTrue();

        Step(world, 1UL, 0, 0);

        // Characterisation: player 1's step evicts every chunk beyond 8 of player 1, the chunks player 2 stands in too,
        // and player 2's next step regenerates them.
        world.TryGetChunk(Apart, 0, out _).Should().BeFalse();
    }

    [Fact]
    public void UpdateAroundPosition_TwoPlayersFarApart_KeepsAnEditNearTheOtherPlayer()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);
        Chunk2D chunk = world.GetOrCreateChunk(Apart, 0);
        MapProp target = chunk.Props.First(p => p.Destructibility.IsDestructible && !p.Destructibility.IsDestroyed);
        int wx = (Apart * Chunk2D.Size) + target.LocalTileX;
        int wy = target.LocalTileY;
        world.TryDamagePropAt(wx, wy, 1f, byte.MaxValue, out _).Should().BeTrue();

        for (int i = 0; i < 4; i++)
        {
            Step(world, 1UL, 0, 0);
            Step(world, 2UL, Apart, 0);
        }

        // Every chunk edit is recorded in the delta store and re-applied on rebuild, so no eviction loses it.
        world.TryGetPropAt(wx, wy, out MapProp after).Should().BeTrue();
        after.Destructibility.CurrentDurability.Should().Be(target.Destructibility.CurrentDurability - 1f);
    }

    private static void Step(ChunkWorld2D world, ulong stream, int chunkX, int chunkY)
    {
        float chunkWorld = Chunk2D.Size * TileSize;
        world.UpdateAroundPosition(new Vector2((chunkX + 0.5f) * chunkWorld, (chunkY + 0.5f) * chunkWorld), LoadRadius, EvictRadius, stream);
    }
}
