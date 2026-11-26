// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Entities;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Render-friendly snapshot of another player or NPC replicated into the local Area of Interest.
/// </summary>
/// <param name="NetworkId">Authoritative network ID.</param>
/// <param name="Kind">Entity kind.</param>
/// <param name="Name">Display name.</param>
/// <param name="Position">Latest smoothed world position.</param>
/// <param name="Velocity">Latest linear velocity.</param>
/// <param name="Radius">Collider radius in world units.</param>
/// <param name="CurrentHealth">Current health points.</param>
/// <param name="MaxHealth">Maximum health points.</param>
/// <param name="ArchetypeId">Visual archetype or character appearance preset ID.</param>
/// <param name="FacingDirection">Normalized facing direction vector.</param>
/// <param name="AttackAnimRemaining">Remaining attack animation seconds (&gt; 0 while attacking).</param>
/// <param name="AttackAnimDuration">Total attack animation duration in seconds.</param>
/// <param name="HitFlashRemaining">Remaining white damage flash duration in seconds.</param>
public readonly record struct RemoteEntityView(
    ulong NetworkId,
    EntityKind Kind,
    string Name,
    Vector2 Position,
    Vector2 Velocity,
    float Radius,
    float CurrentHealth,
    float MaxHealth,
    ushort ArchetypeId = 0,
    Vector2 FacingDirection = default,
    float AttackAnimRemaining = 0f,
    float AttackAnimDuration = 0.30f,
    float HitFlashRemaining = 0f);
