// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Fixed-capacity unmanaged set of entities already struck by the current attack, guaranteeing that a multi-tick
/// active window damages each target at most once. Holds up to <see cref="Capacity"/> distinct targets.
/// </summary>
/// <param name="Count">Number of recorded targets.</param>
/// <param name="Hit0">First recorded target.</param>
/// <param name="Hit1">Second recorded target.</param>
/// <param name="Hit2">Third recorded target.</param>
/// <param name="Hit3">Fourth recorded target.</param>
public readonly record struct AttackHitList(byte Count, Entity Hit0, Entity Hit1, Entity Hit2, Entity Hit3)
{
    /// <summary>Maximum distinct targets one attack can strike.</summary>
    public const int Capacity = 4;

    /// <summary>Gets a value indicating whether no more targets can be recorded.</summary>
    public bool IsFull => this.Count >= Capacity;

    /// <summary>
    /// Determines whether <paramref name="entity"/> was already struck.
    /// </summary>
    /// <param name="entity">Candidate target.</param>
    /// <returns><see langword="true"/> if already recorded.</returns>
    public bool Contains(Entity entity) =>
        (this.Count > 0 && this.Hit0 == entity) ||
        (this.Count > 1 && this.Hit1 == entity) ||
        (this.Count > 2 && this.Hit2 == entity) ||
        (this.Count > 3 && this.Hit3 == entity);

    /// <summary>
    /// Returns a copy with <paramref name="entity"/> appended (unchanged when full).
    /// </summary>
    /// <param name="entity">Target to record.</param>
    /// <returns>Updated hit list.</returns>
    public AttackHitList With(Entity entity) => this.Count switch
    {
        0 => this with { Count = 1, Hit0 = entity },
        1 => this with { Count = 2, Hit1 = entity },
        2 => this with { Count = 3, Hit2 = entity },
        3 => this with { Count = 4, Hit3 = entity },
        _ => this
    };
}
