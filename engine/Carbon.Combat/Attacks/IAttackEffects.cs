// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Host-supplied side effects of attacks resolved by <see cref="AttackSystem"/>. Lets an authoritative server route hits
/// through its own player damage pipeline (mitigation, death, hit broadcast) and spawn replicated projectiles, while the
/// attack timeline and hit geometry stay in the engine. <see cref="DefaultAttackEffects"/> is used when none is given.
/// </summary>
public interface IAttackEffects
{
    /// <summary>
    /// Applies one resolved hit. Called only during <see cref="AttackPhase.Active"/>, at most once per victim per attack.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="attacker">Attacking entity.</param>
    /// <param name="victim">Struck entity (already filtered by layer mask).</param>
    /// <param name="damage">Pre-mitigation damage.</param>
    /// <returns><see langword="true"/> if the hit landed (the victim is then recorded as struck).</returns>
    bool ApplyHit(WorldSimulation world, Entity attacker, Entity victim, float damage);

    /// <summary>
    /// Spawns the projectile of a released <see cref="AttackKind.Projectile"/> attack.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="request">Launch parameters.</param>
    /// <returns>The spawned projectile entity (or <see cref="Entity.Null"/> if none was spawned).</returns>
    Entity LaunchProjectile(WorldSimulation world, in ProjectileLaunchRequest request);
}
