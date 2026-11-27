// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Roads;

/// <summary>
/// How road sites are planned ahead of a streaming player, off the tick (ADR-045 D1, D5). Tuning only: the roads are
/// the same whatever the value is. No margin beyond the load radius is needed: a road query already reads two sites
/// (about 14 chunks) beyond any rectangle.
/// </summary>
/// <param name="Workers">Road sites planned at once in the background; 0 turns the prewarm off.</param>
public sealed record RoadPrewarmOptions(int Workers = RoadPrewarmOptions.DefaultWorkers)
{
    /// <summary>The default number of background road builds (ADR-045 D5).</summary>
    public const int DefaultWorkers = 2;

    /// <summary>Gets the default options.</summary>
    public static RoadPrewarmOptions Default { get; } = new();

    /// <summary>Validates the options.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Workers"/> is negative.</exception>
    public void Validate() => ArgumentOutOfRangeException.ThrowIfNegative(this.Workers);
}
