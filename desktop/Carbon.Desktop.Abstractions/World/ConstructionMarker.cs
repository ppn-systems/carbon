// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Building;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Client-side view of a construction site (blueprint / frame) or a structure being deconstructed, as pushed by the
/// server (<c>MapConstructionChanged</c>) or imported from a chunk delta. Render-only; the server stays authoritative.
/// </summary>
/// <param name="AnchorX">Anchor (top-left) world tile X.</param>
/// <param name="AnchorY">Anchor (top-left) world tile Y.</param>
/// <param name="Width">Rotated footprint width in tiles.</param>
/// <param name="Height">Rotated footprint height in tiles.</param>
/// <param name="Type">Structure type.</param>
/// <param name="Rotation">Placement rotation.</param>
/// <param name="Stage">Blueprint, Frame, or Complete while being deconstructed.</param>
/// <param name="Progress">Construction or deconstruction progress in [0, 1].</param>
/// <param name="IsDeconstruction">Whether <paramref name="Progress"/> is deconstruction work.</param>
public readonly record struct ConstructionMarker(
    int AnchorX,
    int AnchorY,
    int Width,
    int Height,
    StructureType Type,
    StructureRotation Rotation,
    ConstructionStage Stage,
    float Progress,
    bool IsDeconstruction);
