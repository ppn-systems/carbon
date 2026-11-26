// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.ObjectModel;

namespace Carbon.Maps.Building;

/// <summary>
/// Data-driven definition of a constructible structure, loaded from <c>resources/config/structures.json</c>.
/// </summary>
public sealed class StructureDefinition
{
    /// <summary>Gets the stable string id (e.g. <c>wood_wall</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the structure type stored on the grid and sent over the wire.</summary>
    public required StructureType Type { get; init; }

    /// <summary>Gets the physical role (wall, door, floor, station, furniture, pillar).</summary>
    public required StructureCategory Category { get; init; }

    /// <summary>Gets the material composition.</summary>
    public required StructureMaterial Material { get; init; }

    /// <summary>Gets the materials consumed to build the structure.</summary>
    public required ReadOnlyCollection<MaterialCost> Cost { get; init; }

    /// <summary>Gets the construction work points required once all materials are delivered.</summary>
    public required float WorkAmount { get; init; }

    /// <summary>Gets the maximum hit points of the completed structure.</summary>
    public required float HitPoints { get; init; }

    /// <summary>Gets the minimum tool tier a builder must hold to construct it; also its harvest tier when damaged by tools.</summary>
    public required byte ToolTier { get; init; }

    /// <summary>Gets the minimum terrain affordance of every footprint tile.</summary>
    public required TerrainAffordance Affordance { get; init; }

    /// <summary>Gets the unrotated footprint width in tiles.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the unrotated footprint height in tiles.</summary>
    public required int Height { get; init; }

    /// <summary>Gets a value indicating whether the structure may be placed with a non-default rotation.</summary>
    public bool Rotatable { get; init; }

    /// <summary>Gets a value indicating whether every footprint tile must already carry a completed floor.</summary>
    public bool RequiresFloor { get; init; }

    /// <summary>Gets a value indicating whether the structure must sit between two opposite walls (doors).</summary>
    public bool RequiresWallsOnSides { get; init; }

    /// <summary>Gets the movement speed multiplier granted while walking over it (floors only).</summary>
    public float MoveSpeedMultiplier { get; init; }

    /// <summary>Gets a value indicating whether the completed structure blocks movement.</summary>
    public bool IsSolid => this.Category is not StructureCategory.Floor and not StructureCategory.None;

    /// <summary>
    /// Resolves the footprint size after applying <paramref name="rotation"/>.
    /// </summary>
    /// <param name="rotation">The placement rotation.</param>
    /// <returns>The rotated width and height in tiles.</returns>
    public (int Width, int Height) GetFootprint(StructureRotation rotation) =>
        rotation is StructureRotation.East or StructureRotation.West
            ? (this.Height, this.Width)
            : (this.Width, this.Height);

    /// <summary>
    /// Creates the grid tile of a freshly completed structure of this definition.
    /// </summary>
    /// <returns>The structure tile at full hit points.</returns>
    public StructureTile CreateTile() => this.Category switch
    {
        StructureCategory.Wall => StructureTile.CreateWall(this.Type, this.Material, this.HitPoints, this.ToolTier),
        StructureCategory.Door => StructureTile.CreateDoor(this.Type, this.Material, this.HitPoints, this.ToolTier),
        StructureCategory.Floor => StructureTile.CreateFloor(this.Type, this.Material, this.MoveSpeedMultiplier) with
        {
            Destructibility = Props.PropDestructibility.Create(this.HitPoints, this.ToolTier)
        },
        StructureCategory.Pillar => StructureTile.CreatePillar(this.Material, this.HitPoints),
        StructureCategory.Station => StructureTile.CreateStation(this.Type, this.Material, this.HitPoints, this.ToolTier),
        StructureCategory.Furniture => StructureTile.CreateFurniture(this.Type, this.Material, this.HitPoints, this.ToolTier),
        StructureCategory.None => StructureTile.Empty,
        _ => throw new InvalidOperationException($"Unsupported structure category {this.Category}.")
    };
}
