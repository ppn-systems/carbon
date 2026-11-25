// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Data;

/// <summary>A pure, deterministic terrain sampler: the same coordinate always yields the same tile.</summary>
public interface ITileSource
{
    /// <summary>Samples the terrain tile at a world tile coordinate.</summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>The tile.</returns>
    MapTile SampleTile(int worldX, int worldY);
}
