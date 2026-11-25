// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Simulation.ECS.Components;

/// <summary>
/// Stores an entity's <see cref="Transform2D"/> from the start of the most recent fixed simulation tick
/// for smooth visual interpolation across variable render frame rates. Unmanaged struct component.
/// </summary>
/// <param name="Position">Previous tick 2D position in world units.</param>
/// <param name="Rotation">Previous tick rotation angle in radians.</param>
public readonly record struct PreviousTransform2D(Vector2 Position, float Rotation = 0f);
