// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Players;

/// <summary>
/// Keeps a player's current HP, mana and stamina in step when their maximum pools grow (level-up, attribute points).
/// Only the growth is added: gaining a level or a point never refills vitals, so killing a monster cannot heal.
/// </summary>
internal static class VitalPools
{
    /// <summary>
    /// Adds each pool's growth since <paramref name="previous"/> to its current value, clamped to the new maximum.
    /// Call after the player's derived stats were recomputed.
    /// </summary>
    public static void AddGrowth(WorldSimulation simulation, PlayerState state, (float Health, float Mana, float Stamina) previous)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsDead && simulation.Entities.HasHealth(state.Entity) && state.Stats.MaxHealth > previous.Health)
        {
            HealthComponent hp = simulation.Entities.GetHealth(state.Entity);
            float nextHp = MathF.Min(state.Stats.MaxHealth, hp.Current + (state.Stats.MaxHealth - previous.Health));
            simulation.Entities.SetHealth(state.Entity, new HealthComponent(nextHp, state.Stats.MaxHealth));
            simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
        }

        state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + MathF.Max(0f, state.Stats.MaxMana - previous.Mana));
        state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + MathF.Max(0f, state.Stats.MaxStamina - previous.Stamina));
    }

    /// <summary>Captures the maximum pools before a recompute, for <see cref="AddGrowth"/>.</summary>
    public static (float Health, float Mana, float Stamina) Capture(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return (state.Stats.MaxHealth, state.Stats.MaxMana, state.Stats.MaxStamina);
    }
}
