// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Simulation.ECS;

/// <summary>
/// Represents a lightweight generational entity handle.
/// Generational indexing guarantees safe entity recycling without the ABA problem.
/// Unmanaged 64-bit struct (32-bit ID, 32-bit Generation).
/// </summary>
/// <param name="Id">The unique zero-based or one-based entity slot index.</param>
/// <param name="Generation">The generational cycle index of this slot.</param>
public readonly record struct Entity(int Id, int Generation)
{
    /// <summary>
    /// Represents an invalid or uninitialized entity.
    /// </summary>
    public static readonly Entity Null = new(0, 0);

    /// <summary>
    /// Gets a value indicating whether this entity handle is valid.
    /// </summary>
    public bool IsValid => this.Id > 0 && this.Generation > 0;
}
