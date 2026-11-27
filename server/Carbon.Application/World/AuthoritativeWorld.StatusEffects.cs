// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Combat.Statuses;
using Carbon.Contracts.Common;
using Carbon.Simulation.ECS.Components;

namespace Carbon.Application.World;

/// <summary>
/// Status effect integration (ADR-029): damage pulses become tinted combat hits and route lethal ones through the death
/// path, and the owner's effect list is staged for replication when it changes.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    private void ProcessStatusPulsesLocked()
    {
        ReadOnlySpan<StatusPulseEvent> pulses = _simulation.ReadEvents<StatusPulseEvent>();
        for (int i = 0; i < pulses.Length; i++)
        {
            StatusPulseEvent pulse = pulses[i];
            if (!pulse.IsDamage || pulse.Applied <= 0f ||
                !_players.TryGetByEntity(_simulation, pulse.Target, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity) ||
                !_simulation.Entities.HasHealth(state.Entity))
            {
                continue;
            }

            ulong victimNetId = state.Session.PlayerNetworkId;
            ulong attackerNetId = _simulation.TryGetNetworkIdentity(pulse.Caster, out NetworkIdentity caster) ? caster.NetworkId : victimNetId;
            HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
            state.StatsDirty = true;
            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            this.QueueCombatHitLocked(
                _simulation.Entities.GetTransform(state.Entity).Position,
                attackerNetId,
                victimNetId,
                pulse.Applied,
                hp.Current,
                false,
                false,
                (ushort)pulse.EffectId);

            if (hp.Current <= 0f && !state.IsDead)
            {
                // A lethal pulse (bleeding) enters the same dead state as a hit (ADR-031), which also clears every effect.
                Vector2 deathPos = _simulation.Entities.GetTransform(state.Entity).Position;
                this.KillPlayerLocked(state, attackerNetId, deathPos, DeathCause.Monster);
                this.QueueCombatHitLocked(deathPos, attackerNetId, victimNetId, 0f, 0f, false, true);
            }
        }

        _simulation.ClearEvents<StatusPulseEvent>();
    }

    /// <summary>
    /// Stages the owner's status effect list when the buffer version or the derived needs conditions changed since the last
    /// send (ADR-029 D9). Only the owning connection receives it.
    /// </summary>
    private void QueueStatusEffectSyncLocked(PlayerState state)
    {
        if (!_statusEffects.TryGetBuffer(state.Entity, out StatusEffectBuffer buffer))
        {
            return;
        }

        uint version = buffer.Version;
        int derived = _statusEffects.ComputeDerivedSignature(state.Entity, _needs.Config);
        if (version == state.StatusSyncedVersion && derived == state.StatusSyncedDerived)
        {
            return;
        }

        // A change can add or remove a persisted effect (poison, bleeding, rested), so save the character with it.
        if (version != state.StatusSyncedVersion && state.StatusSyncedVersion != uint.MaxValue)
        {
            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
        }

        state.StatusSyncedVersion = version;
        state.StatusSyncedDerived = derived;
        OutboundBatch pending = _outbound.Pending;
        int offset = pending.StatusEntries.Count;
        _statusEffects.CollectEntries(state.Entity, _needs.Config, pending.StatusEntries);
        pending.StatusSyncs.Add(new OutboundStatusSync(state.Session.ConnectionId, offset, pending.StatusEntries.Count - offset));
    }
}
