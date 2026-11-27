// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Actors;
using Carbon.Application.World.Commands;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Application.World.Water;
using Carbon.Contracts.Common;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;
using Carbon.Survival.Events;
using Carbon.Survival.Systems;

namespace Carbon.Application.World;

/// <summary>
/// Player death and respawn (ADR-031): a killed player stays dead at 0 HP where they fell, drops part of their gold, and
/// refuses every action until they ask to respawn; the respawn moves them to the sanctuary with full vitals.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitRespawnAsync(ulong connectionId) =>
        _commands.Submit(WorldCommandKind.Respawn, connectionId, 0);

    /// <summary>
    /// Respawns a dead player at the sanctuary with full health, mana and stamina and refilled needs. Synchronous form used
    /// by the tick and tests; handlers use <see cref="SubmitRespawnAsync"/> (ADR-017).
    /// </summary>
    /// <returns><see cref="OpStatus.Ok"/>, or <see cref="OpStatus.Forbidden"/> when not in the world or not dead.</returns>
    public OpStatus TryRespawn(ulong connectionId)
    {
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !state.IsDead ||
                !_simulation.Entities.IsAlive(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            this.RespawnLocked(state);
            return OpStatus.Ok;
        }
    }

    /// <summary>Gets whether the connection's player is dead and waiting to respawn.</summary>
    public bool IsPlayerDead(ulong connectionId)
    {
        lock (_gate)
        {
            return _players.ByConnection.TryGetValue(connectionId, out PlayerState? state) && state.IsDead;
        }
    }

    /// <summary>
    /// Kills a player: health stays at 0, movement and queued actions stop, monsters drop them as a target, the death
    /// penalty is dropped as gold piles at <paramref name="deathPos"/>, and the player is told who killed them.
    /// </summary>
    private void KillPlayerLocked(PlayerState victim, ulong killerNetworkId, Vector2 deathPos, DeathCause cause)
    {
        if (victim.IsDead)
        {
            return;
        }

        victim.IsDead = true;
        HealthComponent hp = _simulation.Entities.GetHealth(victim.Entity);
        _simulation.Entities.SetHealth(victim.Entity, new HealthComponent(0f, hp.Max));
        _simulation.MarkDirty(victim.Entity, EntityDirtyMask.Health);
        if (_simulation.Entities.HasVelocity(victim.Entity))
        {
            _simulation.Entities.SetVelocity(victim.Entity, new Velocity2D(0f, 0f, 5f));
        }

        victim.HasPendingInput = false;
        victim.HasPendingAttack = false;
        victim.PendingPickupLootId = 0UL;
        _ = EatActionSystem.CancelEat(_simulation, victim.Entity, MealInterruptReason.Died);
        victim.EatingSlot = -1;
        this.WakeLocked(victim);
        this.ClearAggroAgainstTargetLocked(victim.Entity, deathPos);
        _statusEffects.Clear(victim.Entity); // ADR-029: death clears every status effect

        ushort killerArchetype = 0;
        if (cause == DeathCause.Monster && _actorsByNetworkId.TryGetValue(killerNetworkId, out NpcActorMeta killer))
        {
            killerArchetype = killer.ArchetypeId;
        }
        else if (cause == DeathCause.Monster)
        {
            cause = DeathCause.Unknown;
        }

        long goldDropped = _commerce.DropDeathGold(victim, deathPos);
        _outbound.Pending.PlayerDeaths.Add(new OutboundPlayerDeath(
            victim.Session.ConnectionId,
            new PlayerDeathSnapshot(cause, killerArchetype, cause == DeathCause.Unknown ? 0UL : killerNetworkId, goldDropped)));

        victim.StatsDirty = true;
        this.MarkCharacterDirtyLocked(victim, inventoryChanged: false);
    }

    /// <summary>Moves a dead player to the sanctuary and refills health, mana, stamina and needs.</summary>
    private void RespawnLocked(PlayerState state)
    {
        Vector2 sanctuaryPos = _chunkWorld.SpawnWorldPosition;
        _simulation.Entities.SetHealth(state.Entity, new HealthComponent(state.Stats.MaxHealth, state.Stats.MaxHealth));
        _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
        _simulation.EnqueueCommand(SimulationCommand.Teleport(state.Entity, sanctuaryPos));
        if (_simulation.Entities.HasVelocity(state.Entity))
        {
            _simulation.Entities.SetVelocity(state.Entity, new Velocity2D(0f, 0f, 5f));
        }

        state.IsDead = false;
        OxygenService.RefillBreath(state); // a full breath at the sanctuary
        this.ResetNeedsAfterDeathLocked(state);
        state.CurrentMana = state.Stats.MaxMana;
        state.Stamina.Refill(state.Stats.MaxStamina);
        state.StatsDirty = true;
        this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
    }
}
