// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Building;

namespace Carbon.Maps.Settlements;

/// <summary>A piece of furniture or a station in a template, in the template's authored (door-south) orientation.</summary>
/// <param name="Type">Structure type.</param>
/// <param name="X">Anchor (top-left) cell X.</param>
/// <param name="Y">Anchor (top-left) cell Y.</param>
/// <param name="Rotation">Authored rotation.</param>
public readonly record struct VillageFurniture(StructureType Type, int X, int Y, StructureRotation Rotation);
