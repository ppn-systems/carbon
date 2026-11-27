// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Races;

/// <summary>One body type a race allows (<c>races.json</c> <c>races[].bodies[]</c>, ADR-042).</summary>
public sealed class BodyTypeDefinition
{
    /// <summary>Gets the body type id, unique within its race. It is what characters store and the wire carries.</summary>
    public byte Id { get; set; }

    /// <summary>Gets the stable code (<c>male</c>, <c>female</c>), for logs and tools.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets the localization key of the body type's name.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Gets the rig file, relative to the art root (e.g. <c>heroes/human_male.cvg.json</c>).</summary>
    public string Rig { get; set; } = string.Empty;
}
