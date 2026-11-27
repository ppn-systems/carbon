// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Entities;

namespace Carbon.Items.Progression;

/// <summary>The stat contributions of one point in each <see cref="PrimaryAttributeId"/>.</summary>
public sealed class AttributeEffectsConfig
{
    /// <summary>Gets the effect of one Vitality point.</summary>
    public StatBlock Vitality { get; init; } = new();

    /// <summary>Gets the effect of one Wisdom point.</summary>
    public StatBlock Wisdom { get; init; } = new();

    /// <summary>Gets the effect of one Power point.</summary>
    public StatBlock Power { get; init; } = new();

    /// <summary>Gets the effect of one Agility point.</summary>
    public StatBlock Agility { get; init; } = new();

    /// <summary>Gets the effect of one Dexterity point.</summary>
    public StatBlock Dexterity { get; init; } = new();

    /// <summary>Gets the effect block of <paramref name="attribute"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="attribute">Attribute.</param>
    /// <returns>The effect block.</returns>
    public StatBlock? Get(PrimaryAttributeId attribute) => attribute switch
    {
        PrimaryAttributeId.Vitality => this.Vitality,
        PrimaryAttributeId.Wisdom => this.Wisdom,
        PrimaryAttributeId.Power => this.Power,
        PrimaryAttributeId.Agility => this.Agility,
        PrimaryAttributeId.Dexterity => this.Dexterity,
        _ => null,
    };
}
