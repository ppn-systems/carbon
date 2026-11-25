// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Navigation;

/// <summary>
/// Outcome status of a pathfinding query on <see cref="GridPathfinder"/>.
/// </summary>
public enum PathSearchStatus : byte
{
    /// <summary>A valid path from start to goal was found and written into the output span.</summary>
    Found = 0,

    /// <summary>Either the start tile or goal tile is impassable.</summary>
    StartOrGoalBlocked = 1,

    /// <summary>The open set exhausted without reaching the goal tile.</summary>
    Unreachable = 2,

    /// <summary>The search reached the node expansion budget before finding the goal.</summary>
    SearchLimitReached = 3,

    /// <summary>A path was found, but the caller-provided output buffer is smaller than the path length.</summary>
    BufferTooSmall = 4
}
