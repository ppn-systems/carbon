// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Building;

namespace Carbon.Maps.Settlements;

/// <summary>One structure tile a village building puts on the building grid.</summary>
/// <param name="X">World tile X.</param>
/// <param name="Y">World tile Y.</param>
/// <param name="Type">Structure type of the tile.</param>
/// <param name="Rotation">Rotation of the structure the tile belongs to.</param>
/// <param name="AnchorX">World tile X of that structure's anchor (top-left).</param>
/// <param name="AnchorY">World tile Y of that structure's anchor (top-left).</param>
/// <param name="FloorBeneath">Floor under furniture, restored if the furniture is ever removed.</param>
public readonly record struct VillagePlacement(
    int X,
    int Y,
    StructureType Type,
    StructureRotation Rotation,
    int AnchorX,
    int AnchorY,
    StructureType FloorBeneath);
