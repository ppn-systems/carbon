// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Creatures.Spawning;

/// <summary>Where the world's boss lairs are (ADR-032 D15): at most one per lair cell, fixed by world data.</summary>
public interface ILairSource
{
    /// <summary>Gets the width of a lair cell in world units.</summary>
    float CellSize { get; }

    /// <summary>Gets the width of a terrain chunk in world units (a fight in the boss's chunk keeps it in the world).</summary>
    float ChunkSize { get; }

    /// <summary>Finds the lair of a cell, if it has one. Called once per cell; the result is cached by the caller.</summary>
    /// <param name="cellX">Lair cell X.</param>
    /// <param name="cellY">Lair cell Y.</param>
    /// <param name="anchor">The lair's anchor in world units.</param>
    /// <returns><see langword="true"/> when the cell holds a lair.</returns>
    bool TryGetLair(int cellX, int cellY, out Vector2 anchor);
}
