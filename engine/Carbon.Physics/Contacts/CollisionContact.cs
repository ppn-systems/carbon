// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Physics.Contacts;

/// <summary>
/// Contains narrowphase collision detection results and penetration resolution data.
/// Zero allocation unmanaged struct.
/// </summary>
/// <param name="HasCollision">Whether a collision/overlap occurred.</param>
/// <param name="Normal">Unit contact normal pointing from object A to object B.</param>
/// <param name="PenetrationDepth">Overlap penetration depth along the normal.</param>
public readonly record struct CollisionContact(bool HasCollision, Vector2 Normal, float PenetrationDepth)
{
    /// <summary>
    /// Represents no collision contact.
    /// </summary>
    public static readonly CollisionContact None = new(false, Vector2.Zero, 0f);
}
