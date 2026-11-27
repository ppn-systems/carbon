// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>The one thing the interact key acts on, and where its prompt is drawn.</summary>
/// <param name="Kind">What the key does.</param>
/// <param name="Position">World position of the target (entity position or tile centre), in world pixels.</param>
/// <param name="NetworkId">Network id of an entity target (loot drop, NPC), else 0.</param>
/// <param name="TileX">World tile X of a structure target (door, anvil).</param>
/// <param name="TileY">World tile Y of a structure target (door, anvil).</param>
public readonly record struct InteractionTarget(InteractionKind Kind, Vector2 Position, ulong NetworkId, int TileX, int TileY)
{
    /// <summary>Gets the empty target (nothing in reach).</summary>
    public static InteractionTarget None => default;

    /// <summary>Gets whether there is something to interact with.</summary>
    public bool Exists => this.Kind != InteractionKind.None;
}

/// <summary>A possible target and the distance within which the interact key may use it.</summary>
/// <param name="Target">The target.</param>
/// <param name="Radius">How far from the player, in world pixels, the target can still be used.</param>
public readonly record struct InteractionCandidate(InteractionTarget Target, float Radius);
