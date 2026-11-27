// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Abstractions.Localization;
using Carbon.Maps.Building;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Short localized labels for <see cref="BuildResultCode"/>s (<c>build.result.*</c>, ADR-023), shown next to the placement
/// ghost and in toasts. The codes come from the same <see cref="BuildingService"/> rules the server enforces.
/// </summary>
public static class BuildResultText
{
    /// <summary>Describes a placement / construction outcome.</summary>
    /// <param name="code">The result code.</param>
    /// <returns>A short localized label (allocation-free).</returns>
    public static string Describe(BuildResultCode code) => code switch
    {
        BuildResultCode.Ok => GameText.Get("build.result.ok"),
        BuildResultCode.UnknownStructure => GameText.Get("build.result.unknown_structure"),
        BuildResultCode.RotationNotAllowed => GameText.Get("build.result.rotation_not_allowed"),
        BuildResultCode.BlockedByWater => GameText.Get("build.result.blocked_by_water"),
        BuildResultCode.BlockedByCliff => GameText.Get("build.result.blocked_by_cliff"),
        BuildResultCode.InsufficientAffordance => GameText.Get("build.result.insufficient_affordance"),
        BuildResultCode.BlockedByNaturalObstacle => GameText.Get("build.result.blocked_by_natural_obstacle"),
        BuildResultCode.BlockedByExistingStructure => GameText.Get("build.result.blocked_by_existing_structure"),
        BuildResultCode.BlockedByConstructionSite => GameText.Get("build.result.blocked_by_construction_site"),
        BuildResultCode.BlockedByEntity => GameText.Get("build.result.blocked_by_entity"),
        BuildResultCode.RequiresFloor => GameText.Get("build.result.requires_floor"),
        BuildResultCode.RequiresWallsOnSides => GameText.Get("build.result.requires_walls_on_sides"),
        BuildResultCode.OutOfReach => GameText.Get("build.result.out_of_reach"),
        BuildResultCode.PermissionDenied => GameText.Get("build.result.permission_denied"),
        BuildResultCode.NotFound => GameText.Get("build.result.not_found"),
        BuildResultCode.InsufficientMaterials => GameText.Get("build.result.insufficient_materials"),
        BuildResultCode.MaterialsNotDelivered => GameText.Get("build.result.materials_not_delivered"),
        BuildResultCode.ToolTierTooLow => GameText.Get("build.result.tool_tier_too_low"),
        BuildResultCode.InvalidArgument => GameText.Get("build.result.invalid_argument"),
        _ => GameText.Get("build.result.failed"),
    };
}
