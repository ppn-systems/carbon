// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Abstractions.Configuration;

/// <summary>
/// Developer-only world overrides used by the screenshot review tool (<c>tools/screenshots</c>). They are read
/// from the host's own environment and only in Debug builds of the host (see
/// <c>HostRuntimeOptions.FromEnvironment</c>); nothing here is ever taken from a client.
/// </summary>
public sealed record WorldDebugOverrides
{
    /// <summary>Gets an explicit world tile every character enters the world at, if any.</summary>
    public (int X, int Y)? SpawnTile { get; init; }

    /// <summary>
    /// Gets the name of a biome (e.g. <c>DuneDesert</c>) whose nearest representative tile becomes the spawn
    /// tile, if any. <see cref="SpawnTile"/> wins when both are set.
    /// </summary>
    public string? SpawnBiome { get; init; }

    /// <summary>Gets the offset, in tiles, applied to the resolved spawn tile.</summary>
    public (int X, int Y) SpawnOffset { get; init; }

    /// <summary>Gets the in-game hour (0..24) the world clock starts at, if any.</summary>
    public float? StartHour { get; init; }

    /// <summary>Gets extra items granted to newly created characters (screenshots of enhanced gear, ADR-020).</summary>
    public IReadOnlyList<DebugStarterItem> StarterItems { get; init; } = [];

    /// <summary>Gets a value indicating whether any override is set.</summary>
    public bool IsActive => this.SpawnTile.HasValue || this.SpawnBiome is not null || this.StartHour.HasValue ||
        this.SpawnOffset != default || this.StarterItems.Count > 0;
}
