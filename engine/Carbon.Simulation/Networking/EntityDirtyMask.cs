// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Simulation.Networking;

/// <summary>
/// Tracks which replicated state groups changed on an entity since the previous network snapshot flush.
/// </summary>
[Flags]
public enum EntityDirtyMask : byte
{
    /// <summary>No pending dirty state.</summary>
    None = 0,

    /// <summary>Entity was newly spawned or needs a full baseline snapshot.</summary>
    Spawned = 1 << 0,

    /// <summary>Transform position or rotation changed.</summary>
    Transform = 1 << 1,

    /// <summary>Linear velocity changed.</summary>
    Velocity = 1 << 2,

    /// <summary>Health changed.</summary>
    Health = 1 << 3,

    /// <summary>Custom module-defined state changed.</summary>
    Custom = 1 << 4
}
