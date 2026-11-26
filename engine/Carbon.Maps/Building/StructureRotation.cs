// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Quarter-turn facing of a placed structure. East and West swap a footprint's width and height.
/// </summary>
public enum StructureRotation : byte
{
    /// <summary>Default facing; the footprint is used as defined.</summary>
    North = 0,

    /// <summary>Rotated 90 degrees clockwise; width and height swap.</summary>
    East = 1,

    /// <summary>Rotated 180 degrees; the footprint is used as defined.</summary>
    South = 2,

    /// <summary>Rotated 270 degrees clockwise; width and height swap.</summary>
    West = 3
}
