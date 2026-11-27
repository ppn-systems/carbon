// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>What a strike hit, which picks its impact particles (docs/art/combat-animation.md §3.4).</summary>
public enum ImpactMaterial : byte
{
    /// <summary>Monsters and players: droplets and a puff.</summary>
    Flesh = 0,

    /// <summary>Trees and wooden structures: splinters.</summary>
    Wood = 1,

    /// <summary>Rocks and ores: chips and sparks.</summary>
    Stone = 2,
}
