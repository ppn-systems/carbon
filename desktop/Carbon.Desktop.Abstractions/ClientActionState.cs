// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Semantic visual action kind used by the client renderer to select sprite clips and VFX trails.
/// </summary>
public enum ClientActionKind : byte
{
    /// <summary>No active action.</summary>
    None = 0,

    /// <summary>Mele weapon cleave/slash.</summary>
    SwordSlash = 1,

    /// <summary>Woodcutting axe chop against flora.</summary>
    AxeChop = 2,

    /// <summary>Mining pickaxe strike against ore or stone.</summary>
    PickaxeMine = 3,

    /// <summary>Enemy strike lunge.</summary>
    MonsterStrike = 4,

    /// <summary>Consumable use animation.</summary>
    ConsumeItem = 5,

    /// <summary>Structure placement animation.</summary>
    PlaceStructure = 6,
}

/// <summary>
/// Captures the active client-side action animation state for the local player.
/// </summary>
/// <param name="Kind">Action kind being animated.</param>
/// <param name="DurationSeconds">Total animation duration in seconds.</param>
/// <param name="RemainingSeconds">Remaining seconds before completion.</param>
/// <param name="AimDirection">Normalized direction of the swing/strike.</param>
/// <param name="EquippedItemId">Equipped item ID at the moment the action began.</param>
/// <param name="ComboStep">Alternating swing cadence bit (0 or 1).</param>
public readonly record struct ClientActionState(
    ClientActionKind Kind,
    float DurationSeconds,
    float RemainingSeconds,
    Vector2 AimDirection,
    uint EquippedItemId,
    byte ComboStep)
{
    /// <summary>Gets an empty idle action state.</summary>
    public static ClientActionState None { get; } = new(ClientActionKind.None, 0f, 0f, Vector2.UnitY, 0, 0);

    /// <summary>Gets normalized [0..1] progress across the current action.</summary>
    public float Progress => this.DurationSeconds <= 0.0001f || this.RemainingSeconds <= 0f
        ? 1f
        : System.Math.Clamp(1f - (this.RemainingSeconds / this.DurationSeconds), 0f, 1f);

    /// <summary>Gets whether the action animation is currently active.</summary>
    public bool IsActive => this.Kind != ClientActionKind.None && this.RemainingSeconds > 0f;
}
