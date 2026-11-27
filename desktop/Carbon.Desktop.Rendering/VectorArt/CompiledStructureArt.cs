// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Pre-compiled constructible tile structure artwork loaded from <c>structures/*.cvg.json</c>.
/// </summary>
public sealed class CompiledStructureArt
{
    /// <summary>Initializes a compiled structure blueprint.</summary>
    public CompiledStructureArt(
        byte structureType,
        string id,
        string displayName,
        CompiledVectorShape[] shapes,
        CompiledVectorShape[] openShapes,
        int footprintWidth = 1,
        int footprintHeight = 1)
    {
        this.FootprintWidth = footprintWidth;
        this.FootprintHeight = footprintHeight;
        this.StructureType = structureType;
        this.Id = id;
        this.DisplayName = displayName;
        this.Shapes = shapes;
        this.OpenShapes = openShapes;
    }

    /// <summary>Numeric structure type matching <c>StructureType</c>.</summary>
    public byte StructureType { get; }

    /// <summary>Canonical structure identifier.</summary>
    public string Id { get; }

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; }

    /// <summary>Default (or closed door) tile geometry in <c>[0..32]</c> tile coordinates.</summary>
    public CompiledVectorShape[] Shapes { get; }

    /// <summary>
    /// Gets the unrotated footprint width in tiles the art is authored over (<c>[0..32 × width]</c>). Multi-tile art is
    /// drawn once, at the structure's anchor, rotated with its record.
    /// </summary>
    public int FootprintWidth { get; }

    /// <summary>Gets the unrotated footprint height in tiles the art is authored over.</summary>
    public int FootprintHeight { get; }

    /// <summary>Gets whether the art covers more than one tile.</summary>
    public bool IsMultiTile => this.FootprintWidth > 1 || this.FootprintHeight > 1;

    /// <summary>Alternate geometry rendered when a door structure is in the open state.</summary>
    public CompiledVectorShape[] OpenShapes { get; }
}
