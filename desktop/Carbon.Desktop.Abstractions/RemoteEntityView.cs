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
public readonly record struct RemoteEntityView(
    ulong NetworkId,
    EntityKind Kind,
    string Name,
    Vector2 Position,
    Vector2 Velocity,
    float Radius,
    float CurrentHealth,
    float MaxHealth);
