// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Roads;

/// <summary>
/// How road sites are planned ahead of a streaming player, off the tick (ADR-045 D1, D5). Tuning only: the roads are
/// the same whatever these values are.
/// </summary>
/// <param name="Workers">Road sites planned at once in the background; 0 turns the prewarm off.</param>
/// <param name="MarginChunks">Chunks beyond the load radius whose road sites are planned ahead.</param>
public sealed record RoadPrewarmOptions(int Workers = RoadPrewarmOptions.DefaultWorkers, int MarginChunks = RoadPrewarmOptions.DefaultMarginChunks)
{
    /// <summary>The default number of background road builds (ADR-045 D5).</summary>
    public const int DefaultWorkers = 2;

    /// <summary>The default margin, in chunks (ADR-045 D1).</summary>
    public const int DefaultMarginChunks = 2;

    /// <summary>Gets the default options.</summary>
    public static RoadPrewarmOptions Default { get; } = new();

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(this.Workers);
        ArgumentOutOfRangeException.ThrowIfNegative(this.MarginChunks);
    }
}
