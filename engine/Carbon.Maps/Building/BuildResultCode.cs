// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Maps.Building;

/// <summary>
/// Detailed outcome of a <see cref="BuildingService"/> mutation.
/// </summary>
public enum BuildResultCode : byte
{
    /// <summary>The mutation was applied.</summary>
    Ok = 0,

    /// <summary>The structure type has no definition in the catalog.</summary>
    UnknownStructure = 1,

    /// <summary>The definition does not allow the requested rotation.</summary>
    RotationNotAllowed = 2,

    /// <summary>A footprint tile is open water.</summary>
    BlockedByWater = 3,

    /// <summary>A footprint tile is a cliff / solid rock, or the footprint spans different height levels.</summary>
    BlockedByCliff = 4,

    /// <summary>The terrain cannot bear the structure.</summary>
    InsufficientAffordance = 5,

    /// <summary>A solid natural prop (tree, rock) occupies a footprint tile.</summary>
    BlockedByNaturalObstacle = 6,

    /// <summary>A completed structure occupies a footprint tile.</summary>
    BlockedByExistingStructure = 7,

    /// <summary>Another blueprint or frame reserves a footprint tile.</summary>
    BlockedByConstructionSite = 8,

    /// <summary>A live entity stands on a footprint tile of a solid structure.</summary>
    BlockedByEntity = 9,

    /// <summary>The structure needs a completed floor under every footprint tile.</summary>
    RequiresFloor = 10,

    /// <summary>A door must sit between two opposite walls.</summary>
    RequiresWallsOnSides = 11,

    /// <summary>The actor is too far from the footprint.</summary>
    OutOfReach = 12,

    /// <summary>The actor is neither the owner nor allowed by the owner.</summary>
    PermissionDenied = 13,

    /// <summary>No blueprint, frame or structure exists at the location.</summary>
    NotFound = 14,

    /// <summary>The material source has none of the still-missing materials.</summary>
    InsufficientMaterials = 15,

    /// <summary>Construction work requires every material to be delivered first.</summary>
    MaterialsNotDelivered = 16,

    /// <summary>The actor's tool tier is below the structure's required tier.</summary>
    ToolTierTooLow = 17,

    /// <summary>The argument is invalid (non-positive work or damage, type None).</summary>
    InvalidArgument = 18,

    /// <summary>The tile belongs to a settlement faction (a generated village) that players may not build on or modify.</summary>
    ProtectedSettlement = 19
}

/// <summary>
/// Result of a <see cref="BuildingService"/> mutation, carrying the reason code and the affected footprint so the
/// server can mark dirty chunks and broadcast pushes.
/// </summary>
/// <param name="Code">Detailed outcome.</param>
/// <param name="Stage">Stage of the site/structure after the mutation.</param>
/// <param name="AnchorX">Anchor world tile X of the footprint.</param>
/// <param name="AnchorY">Anchor world tile Y of the footprint.</param>
/// <param name="Width">Footprint width in tiles (0 when nothing was resolved).</param>
/// <param name="Height">Footprint height in tiles (0 when nothing was resolved).</param>
/// <param name="Progress">Work progress in [0, 1] for construction or deconstruction.</param>
public readonly record struct BuildMutationResult(
    BuildResultCode Code,
    ConstructionStage Stage,
    int AnchorX,
    int AnchorY,
    int Width,
    int Height,
    float Progress)
{
    /// <summary>Gets a value indicating whether the mutation was applied.</summary>
    public bool IsSuccess => this.Code == BuildResultCode.Ok;

    /// <summary>Creates a failed result without footprint information.</summary>
    /// <param name="code">Failure code.</param>
    /// <returns>The result.</returns>
    public static BuildMutationResult Fail(BuildResultCode code) => new(code, ConstructionStage.None, 0, 0, 0, 0, 0f);

    /// <summary>Maps the detailed code onto the wire-level <see cref="OpStatus"/> used by <see cref="MutationResult"/>.</summary>
    /// <returns>The wire status.</returns>
    public OpStatus ToOpStatus() => this.Code switch
    {
        BuildResultCode.Ok => OpStatus.Ok,
        BuildResultCode.PermissionDenied => OpStatus.Forbidden,
        BuildResultCode.ProtectedSettlement => OpStatus.Forbidden,
        BuildResultCode.NotFound => OpStatus.NotFound,
        BuildResultCode.OutOfReach => OpStatus.OutOfRange,
        BuildResultCode.BlockedByNaturalObstacle or
        BuildResultCode.BlockedByExistingStructure or
        BuildResultCode.BlockedByConstructionSite or
        BuildResultCode.BlockedByEntity or
        BuildResultCode.InsufficientMaterials or
        BuildResultCode.MaterialsNotDelivered => OpStatus.Conflict,
        BuildResultCode.UnknownStructure or
        BuildResultCode.RotationNotAllowed or
        BuildResultCode.BlockedByWater or
        BuildResultCode.BlockedByCliff or
        BuildResultCode.InsufficientAffordance or
        BuildResultCode.RequiresFloor or
        BuildResultCode.RequiresWallsOnSides or
        BuildResultCode.ToolTierTooLow or
        BuildResultCode.InvalidArgument => OpStatus.Invalid,
        _ => OpStatus.Invalid
    };
}
