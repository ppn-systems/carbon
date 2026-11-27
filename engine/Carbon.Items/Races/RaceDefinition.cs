// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Items.Progression;

namespace Carbon.Items.Races;

/// <summary>One playable race (<c>races.json</c> <c>races[]</c>, ADR-042).</summary>
public sealed class RaceDefinition
{
    /// <summary>Gets the race id (<c>1..65535</c>). It is what characters store and the wire carries.</summary>
    public ushort Id { get; set; }

    /// <summary>Gets the stable code (<c>human</c>), for logs and tools.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets the localization key of the race's name.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Gets the body types the race allows; the first is the creator's default.</summary>
    public List<BodyTypeDefinition> Bodies { get; set; } = [];

    /// <summary>Gets the appearance options, in stored order.</summary>
    public List<AppearanceOptionDefinition> Options { get; set; } = [];

    /// <summary>Gets the flat stat changes the race applies on top of <c>progression.json</c> (may be negative).</summary>
    public StatBlock StatModifiers { get; set; } = new();

    /// <summary>Finds a body type by id.</summary>
    /// <param name="bodyType">Body type id.</param>
    /// <returns>The body type, or <see langword="null"/> when the race does not allow it.</returns>
    public BodyTypeDefinition? FindBody(byte bodyType)
    {
        for (int i = 0; i < this.Bodies.Count; i++)
        {
            if (this.Bodies[i].Id == bodyType)
            {
                return this.Bodies[i];
            }
        }

        return null;
    }
}
