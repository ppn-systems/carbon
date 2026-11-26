// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Ownership and footprint metadata of one tile of a completed structure, persisted next to the
/// <see cref="StructureTile"/> in <see cref="Streaming.ChunkDelta.StructureRecords"/>.
/// </summary>
/// <param name="OwnerId">Player id that built the structure (0 = unowned / world-generated).</param>
/// <param name="AnchorOffsetX">Tile X offset from the anchor to this tile (0..14).</param>
/// <param name="AnchorOffsetY">Tile Y offset from the anchor to this tile (0..14).</param>
/// <param name="Rotation">Placement rotation of the structure.</param>
/// <param name="FloorBeneath">Floor type covered by the structure, restored when it is removed.</param>
public readonly record struct StructureRecord(
    ulong OwnerId,
    byte AnchorOffsetX,
    byte AnchorOffsetY,
    StructureRotation Rotation,
    StructureType FloorBeneath);
