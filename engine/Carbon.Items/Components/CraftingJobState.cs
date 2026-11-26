// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Components;

/// <summary>
/// Lifecycle state of a <see cref="CraftingJobComponent"/>.
/// </summary>
public enum CraftingJobState : byte
{
    /// <summary>Work is progressing every tick.</summary>
    Running = 0,

    /// <summary>Work is done; outputs are waiting to be delivered (e.g. inventory was full).</summary>
    Completed = 1,

    /// <summary>Work is suspended (crafter left the station); no progress is made.</summary>
    Paused = 2
}
