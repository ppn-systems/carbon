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
        CompiledVectorShape[] openShapes)
    {
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

    /// <summary>Alternate geometry rendered when a door structure is in the open state.</summary>
    public CompiledVectorShape[] OpenShapes { get; }
}
