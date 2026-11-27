// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Carbon.Items.Races;

/// <summary>
/// One appearance option a race offers (<c>races.json</c> <c>races[].options[]</c>, ADR-042), e.g. skin tone or hair
/// style. A character stores one choice index per option, in the race's option order.
/// </summary>
public sealed class AppearanceOptionDefinition
{
    /// <summary>Gets the stable code (<c>skin_tone</c>, <c>hair_style</c>), unique within the race.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets the localization key of the option's label.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets the rig role the option drives: palette choices recolour the shapes tagged with it, part choices pick which
    /// part of that role's group the rig shows.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Gets the choices; a stored index is a position in this list.</summary>
    public List<AppearanceChoiceDefinition> Choices { get; set; } = [];

    /// <summary>Gets a value indicating whether the choices are rig parts (otherwise palettes).</summary>
    [JsonIgnore]
    public bool IsPart => this.Choices.Count > 0 && this.Choices[0].Part is not null;
}
