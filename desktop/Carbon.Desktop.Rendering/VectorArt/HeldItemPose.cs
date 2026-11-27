// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>Where a held item sits in the hand this frame (world units).</summary>
/// <param name="Hand">Hand (grip) position.</param>
/// <param name="Center">Item art origin.</param>
/// <param name="Rotation">Item rotation in radians.</param>
/// <param name="Scale">Item art scale.</param>
internal readonly record struct HeldItemPose(Vector2 Hand, Vector2 Center, float Rotation, float Scale)
{
    /// <summary>Gets the world position of an item-local point.</summary>
    /// <param name="local">Point in item art space.</param>
    /// <returns>World position.</returns>
    public Vector2 ToWorld(Vector2 local)
    {
        float c = System.MathF.Cos(this.Rotation);
        float s = System.MathF.Sin(this.Rotation);
        Vector2 p = local * this.Scale;
        return this.Center + new Vector2((p.X * c) - (p.Y * s), (p.X * s) + (p.Y * c));
    }
}
