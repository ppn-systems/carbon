// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Stamina;

/// <summary>Default stamina cost of one primary swing per <see cref="StaminaSwingKind"/> (<c>stamina.json</c> <c>swingCosts</c>).</summary>
public sealed class StaminaSwingCosts
{
    /// <summary>Gets the cost of a bare-hand swing.</summary>
    public float BareHand { get; init; }

    /// <summary>Gets the cost of a melee weapon swing.</summary>
    public float Melee { get; init; }

    /// <summary>Gets the cost of a tool swing.</summary>
    public float Tool { get; init; }

    /// <summary>Gets the cost of a bow shot.</summary>
    public float Ranged { get; init; }

    /// <summary>Gets the cost of a staff cast.</summary>
    public float Spell { get; init; }

    /// <summary>Gets the cost for <paramref name="kind"/>.</summary>
    /// <param name="kind">Swing kind.</param>
    /// <returns>The configured cost.</returns>
    public float For(StaminaSwingKind kind) => kind switch
    {
        StaminaSwingKind.Melee => this.Melee,
        StaminaSwingKind.Tool => this.Tool,
        StaminaSwingKind.Ranged => this.Ranged,
        StaminaSwingKind.Spell => this.Spell,
        StaminaSwingKind.BareHand => this.BareHand,
        _ => this.BareHand,
    };

    internal void Validate()
    {
        StaminaConfig.RequireRange(this.BareHand, 0f, 1_000f, "swingCosts.bareHand");
        StaminaConfig.RequireRange(this.Melee, 0f, 1_000f, "swingCosts.melee");
        StaminaConfig.RequireRange(this.Tool, 0f, 1_000f, "swingCosts.tool");
        StaminaConfig.RequireRange(this.Ranged, 0f, 1_000f, "swingCosts.ranged");
        StaminaConfig.RequireRange(this.Spell, 0f, 1_000f, "swingCosts.spell");
    }
}
