// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Lifecycle stage of a structure: Blueprint → Frame → Complete, or Removed after cancel / deconstruct / destruction.
/// </summary>
public enum ConstructionStage : byte
{
    /// <summary>No structure or site at the location.</summary>
    None = 0,

    /// <summary>Placed and validated; waiting for materials.</summary>
    Blueprint = 1,

    /// <summary>All materials delivered and consumed; waiting for construction work.</summary>
    Frame = 2,

    /// <summary>Finished structure registered on the <see cref="BuildingGrid"/>.</summary>
    Complete = 3,

    /// <summary>The site or structure was removed by the mutation.</summary>
    Removed = 4
}
