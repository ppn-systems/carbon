// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Carbon.Items.Races;

/// <summary>
/// One choice of an appearance option (ADR-042). A choice either recolours the rig shapes tagged with the option's role
/// (<see cref="Colors"/>), or shows the named vector part of the rig (<see cref="Part"/>). Never both.
/// </summary>
public sealed class AppearanceChoiceDefinition
{
    /// <summary>Gets the localization key of the choice's name.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Gets the palette, <c>#RRGGBB</c> strings: base first, then shades (e.g. shadow, highlight).</summary>
    public List<string> Colors { get; set; } = [];

    /// <summary>Gets the rig part name this choice shows, or <see langword="null"/> for a palette choice.</summary>
    public string? Part { get; set; }

    /// <summary>Gets the parsed palette as <c>0xRRGGBB</c> values; filled by <see cref="RaceCatalog"/> validation.</summary>
    [JsonIgnore]
    public IReadOnlyList<uint> Rgb { get; internal set; } = [];
}
