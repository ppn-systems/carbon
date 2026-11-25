// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Simulation.World;

/// <summary>
/// Unified raycast result across both static world geometry and dynamic simulation entities.
/// </summary>
/// <param name="HasHit">Whether an intersection occurred.</param>
/// <param name="Distance">Distance along the ray from its origin to the hit point.</param>
/// <param name="Point">World-space hit coordinate.</param>
/// <param name="Normal">Outward surface normal at the hit coordinate.</param>
/// <param name="Entity">The hit entity handle, or <see cref="Entity.Null"/> when static world geometry was hit.</param>
public readonly record struct WorldRaycastHit(
    bool HasHit,
    float Distance,
    Vector2 Point,
    Vector2 Normal,
    Entity Entity)
{
    /// <summary>
    /// Represents an empty unified raycast result indicating no intersection.
    /// </summary>
    public static readonly WorldRaycastHit None = new(false, 0f, Vector2.Zero, Vector2.Zero, Entity.Null);
}
