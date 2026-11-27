// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Building;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Short player-facing labels for <see cref="BuildResultCode"/>s, shown next to the placement ghost and in toasts.
/// The codes come from the same <see cref="BuildingService"/> rules the server enforces.
/// </summary>
public static class BuildResultText
{
    /// <summary>Describes a placement / construction outcome.</summary>
    /// <param name="code">The result code.</param>
    /// <returns>A short English label.</returns>
    public static string Describe(BuildResultCode code) => code switch
    {
        BuildResultCode.Ok => "Can build here",
        BuildResultCode.UnknownStructure => "Unknown structure",
        BuildResultCode.RotationNotAllowed => "Cannot rotate this",
        BuildResultCode.BlockedByWater => "Cannot build on water",
        BuildResultCode.BlockedByCliff => "Cliff or uneven ground",
        BuildResultCode.InsufficientAffordance => "Ground too soft",
        BuildResultCode.BlockedByNaturalObstacle => "Clear the tree or rock first",
        BuildResultCode.BlockedByExistingStructure => "Already built here",
        BuildResultCode.BlockedByConstructionSite => "Blueprint already here",
        BuildResultCode.BlockedByEntity => "Someone is standing here",
        BuildResultCode.RequiresFloor => "Needs a floor",
        BuildResultCode.RequiresWallsOnSides => "Doors go between walls",
        BuildResultCode.OutOfReach => "Too far away",
        BuildResultCode.PermissionDenied => "Not your building",
        BuildResultCode.NotFound => "Nothing to build",
        BuildResultCode.InsufficientMaterials => "Missing materials",
        BuildResultCode.MaterialsNotDelivered => "Waiting for materials",
        BuildResultCode.ToolTierTooLow => "Needs a better tool",
        BuildResultCode.InvalidArgument => "Invalid request",
        BuildResultCode.ProtectedSettlement => "Village property",
        _ => "Cannot build here"
    };
}
