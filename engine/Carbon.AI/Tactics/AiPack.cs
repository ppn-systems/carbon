// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.AI.Tactics;

/// <summary>
/// Pack / social aggro: actors sharing a non-zero <see cref="AiTacticsComponent.PackId"/> answer each other's calls for
/// help within <see cref="AiTacticsComponent.AssistRadius"/>. Calls never cascade (alerted allies do not re-broadcast),
/// so one pull cannot chain-aggro a whole region. Zero-allocation once threat tables exist.
/// </summary>
public static class AiPack
{
    /// <summary>
    /// Determines whether <paramref name="entity"/> belongs to a pack with a positive assist radius.
    /// </summary>
    /// <param name="entities">Entity manager.</param>
    /// <param name="entity">Actor to test.</param>
    /// <returns><see langword="true"/> when the actor can call for help.</returns>
    public static bool HasPack(EntityManager entities, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return entities.TryGetComponent(entity, out AiTacticsComponent tactics) && tactics.PackId != 0 && tactics.AssistRadius > 0f;
    }

    /// <summary>
    /// Alerts pack allies of <paramref name="caller"/> around <paramref name="callerPos"/>: each living ally gains
    /// <paramref name="threat"/> against <paramref name="target"/>, and idle / patrolling allies start chasing it.
    /// Allies that are fleeing or leashing home are left alone.
    /// </summary>
    /// <param name="world">World simulation.</param>
    /// <param name="caller">Actor calling for help.</param>
    /// <param name="callerPos">Caller position.</param>
    /// <param name="target">Hostile entity the pack should attack.</param>
    /// <param name="threat">Threat added to each ally's table (at least 1).</param>
    /// <param name="radiusMultiplier">Multiplier on the caller's assist radius (e.g. night aggression).</param>
    /// <param name="scratch">Caller-owned scratch buffer for the overlap query.</param>
    /// <returns>The number of allies alerted.</returns>
    public static int CallForHelp(
        WorldSimulation world,
        Entity caller,
        Vector2 callerPos,
        Entity target,
        float threat,
        float radiusMultiplier,
        Span<Entity> scratch)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        if (!target.IsValid || !entities.TryGetComponent(caller, out AiTacticsComponent callerTactics) ||
            callerTactics.PackId == 0 || callerTactics.AssistRadius <= 0f)
        {
            return 0;
        }

        uint layerMask = entities.HasCollider(caller) ? entities.GetCollider(caller).Layer : uint.MaxValue;
        float radius = callerTactics.AssistRadius * MathF.Max(0f, radiusMultiplier);
        world.OverlapCircle(callerPos, radius, scratch, out int count, layerMask == 0 ? uint.MaxValue : layerMask);

        EntityAttachmentStore<ThreatTable> threatStore = entities.GetAttachmentStore<ThreatTable>();
        float added = MathF.Max(1f, threat);
        int alerted = 0;

        for (int i = 0; i < count; i++)
        {
            Entity ally = scratch[i];
            if (ally == caller ||
                ally == target ||
                !entities.IsAlive(ally) ||
                !entities.HasComponent<AiBrainComponent>(ally) ||
                !entities.TryGetComponent(ally, out AiTacticsComponent allyTactics) ||
                allyTactics.PackId != callerTactics.PackId ||
                (entities.HasHealth(ally) && entities.GetHealth(ally).IsDead))
            {
                continue;
            }

            ref AiBrainComponent brain = ref entities.GetComponent<AiBrainComponent>(ally);
            if (brain.State is AiState.Flee or AiState.ReturnHome)
            {
                continue;
            }

            threatStore.GetOrAdd(ally, static () => new ThreatTable()).AddThreat(target, added);
            if (brain.State is AiState.Idle or AiState.Patrol)
            {
                brain = brain with { State = AiState.Chase, TargetEntity = target, RepathCooldownTicks = 0, ChaseTicks = 0 };
            }

            alerted++;
        }

        return alerted;
    }
}
