// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Primitives.Random;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// Fixed abyss lair anchors derived from world data (ADR-032 D15). The world is split into
/// <see cref="CellTiles"/>-wide lair cells. A hash of the world seed and the cell decides whether the cell holds a
/// lair (<c>chance</c>), and the first of <see cref="Probes"/> hashed points that sits in open Abyss water, with
/// Abyss in every direction for <see cref="OpenRadius"/> tiles, is its anchor.
/// <list type="bullet">
/// <item>The same seed always gives the same lairs. The lairs are part of the world like rivers, not combat rolls, so
/// they come from the public seed.</item>
/// <item>A cell with too little open abyss (land, shelves, lakes) has no lair, so lairs stay rare.</item>
/// <item>Nothing is written into the terrain, so the generator version is unchanged.</item>
/// </list>
/// </summary>
public sealed class AbyssLairs
{
    /// <summary>Width of a lair cell in tiles: at most one lair per cell.</summary>
    public const int CellTiles = 1024;

    /// <summary>Hashed candidate points tried per lair cell.</summary>
    public const int Probes = 24;

    /// <summary>Tiles of Abyss water the anchor must have in each of the eight directions.</summary>
    public const int OpenRadius = 4;

    private const ulong LairSalt = 0x4C41495241425953UL; // "LAIRABYS"

    private readonly ITileSource _tiles;
    private readonly ulong _seed;
    private readonly float _chance;

    /// <summary>Initializes a new instance of the <see cref="AbyssLairs"/> class.</summary>
    /// <param name="tiles">The world's terrain (the procedural generator).</param>
    /// <param name="seed">The public world seed.</param>
    /// <param name="chance">Chance, 0..1, that a lair cell holds a lair when it has open abyss.</param>
    public AbyssLairs(ITileSource tiles, ulong seed, float chance)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _seed = seed;
        _chance = chance is >= 0f and <= 1f ? chance : throw new ArgumentOutOfRangeException(nameof(chance));
    }

    /// <summary>Gets the lair cell of a world tile.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns>The cell.</returns>
    public static (int X, int Y) CellOf(int worldTileX, int worldTileY) =>
        (FloorDiv(worldTileX, CellTiles), FloorDiv(worldTileY, CellTiles));

    /// <summary>Finds the lair of a cell, if it has one. Deterministic; samples up to <see cref="Probes"/> sites.</summary>
    /// <param name="cellX">Lair cell X.</param>
    /// <param name="cellY">Lair cell Y.</param>
    /// <param name="anchor">The lair's anchor tile.</param>
    /// <returns><c>true</c> when the cell holds a lair.</returns>
    public bool TryGetLair(int cellX, int cellY, out (int X, int Y) anchor)
    {
        anchor = default;
        DeterministicRandom rng = new(Mix(_seed ^ LairSalt, cellX, cellY));
        if (rng.NextFloat() >= _chance)
        {
            return false;
        }

        int margin = OpenRadius + 1;
        for (int probe = 0; probe < Probes; probe++)
        {
            int x = (cellX * CellTiles) + rng.NextInt(margin, CellTiles - margin);
            int y = (cellY * CellTiles) + rng.NextInt(margin, CellTiles - margin);
            if (this.IsOpenAbyss(x, y))
            {
                anchor = (x, y);
                return true;
            }
        }

        return false;
    }

    private bool IsOpenAbyss(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (WaterDepth.TierOf(_tiles.SampleTile(x + (dx * OpenRadius), y + (dy * OpenRadius))) != WaterDepthTier.Abyss)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);

    private static ulong Mix(ulong seed, int x, int y)
    {
        ulong z = seed ^ ((ulong)(uint)x << 32) ^ (uint)y;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
