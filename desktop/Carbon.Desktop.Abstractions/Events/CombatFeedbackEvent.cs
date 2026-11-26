// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Abstractions.Events;

/// <summary>
/// Ephemeral floating combat or resource-gain feedback emitted by a client world session for HUD/VFX rendering.
/// </summary>
/// <param name="WorldPosition">Anchor position in world coordinates.</param>
/// <param name="Amount">Numeric magnitude (damage, healing, or loot count).</param>
/// <param name="IsCritical">Whether the combat hit was critical.</param>
/// <param name="IsHeal">Whether this event represents a heal or helpful status gain.</param>
/// <param name="Label">Optional preformatted override label (e.g., <c>+3 Wood Log</c>).</param>
public readonly record struct CombatFeedbackEvent(
    Vector2 WorldPosition,
    float Amount,
    bool IsCritical,
    bool IsHeal,
    string? Label = null);
