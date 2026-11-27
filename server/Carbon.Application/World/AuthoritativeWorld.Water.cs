// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Maps.Hydrology;

namespace Carbon.Application.World;

/// <summary>
/// Swimming, diving and oxygen (ADR-032 D10–D12). The depth tier under a player sets their swim state; diving uses
/// up the oxygen pool and anything else refills it. With the pool empty the player drowns: the <c>drowning</c> status
/// effect (ADR-029) pulses lethal damage until they surface, and a lethal pulse takes the normal death path.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    /// <summary>Gets the water movement and oxygen rules the world runs on (<c>water.json</c>).</summary>
    public WaterRules WaterRules => _chunkWorld.WaterRules;

    /// <summary>Gets a player's oxygen and swim state, for tests and tools.</summary>
    /// <param name="connectionId">The player's connection.</param>
    /// <param name="oxygenSeconds">Oxygen left in seconds.</param>
    /// <param name="swimState">The swim state as of the last tick.</param>
    /// <returns><c>false</c> when no such player is in the world.</returns>
    public bool TryGetOxygen(ulong connectionId, out float oxygenSeconds, out SwimState swimState)
    {
        lock (_gate)
        {
            if (_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                oxygenSeconds = float.IsNaN(state.OxygenSeconds) ? _chunkWorld.WaterRules.MaxOxygenSeconds : state.OxygenSeconds;
                swimState = state.SwimState;
                return true;
            }

            oxygenSeconds = 0f;
            swimState = SwimState.Walking;
            return false;
        }
    }

    private void StepWaterLocked(float dt)
    {
        WaterRules rules = _chunkWorld.WaterRules;
        foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
        {
            PlayerState state = kvp.Value;
            if (!_simulation.Entities.HasTransform(state.Entity))
            {
                continue;
            }

            Vector2 position = _simulation.Entities.GetTransform(state.Entity).Position;
            WaterDepthTier tier = _chunkWorld.GetWaterTierAt(position);
            state.WaterTier = tier;
            state.SwimState = WaterRules.SwimStateOf(tier);
            _ = _statusEffects.ExtinguishInWater(state.Entity, tier); // ADR-036 D9: water puts out burning.

            float oxygen = float.IsNaN(state.OxygenSeconds) ? rules.MaxOxygenSeconds : state.OxygenSeconds;
            float drain = state.IsDead ? 0f : rules.DrainPerSecond(tier);
            oxygen = drain > 0f
                ? MathF.Max(0f, oxygen - (drain * dt))
                : MathF.Min(rules.MaxOxygenSeconds, oxygen + (rules.RefillPerSecond * dt));
            state.OxygenSeconds = MathF.Min(oxygen, rules.MaxOxygenSeconds);

            this.UpdateDrowningLocked(state, drowning: drain > 0f && state.OxygenSeconds <= 0f, rules);
        }
    }

    // The drowning effect is added once when the breath runs out and removed once on surfacing, so the owner's status
    // list changes twice per drowning, not every tick.
    private void UpdateDrowningLocked(PlayerState state, bool drowning, WaterRules rules)
    {
        if (!_statusEffects.Catalog.TryGetByKey(rules.DrowningEffect, out StatusEffectDefinition definition) ||
            !_statusEffects.TryGetBuffer(state.Entity, out StatusEffectBuffer buffer))
        {
            return;
        }

        bool active = buffer.TryGet(definition.Id, out _);
        if (drowning && !active)
        {
            _ = _statusEffects.Apply(state.Entity, definition, Carbon.Simulation.ECS.Entity.Null, StatusSource.None, sourceId: 0);
        }
        else if (!drowning && active)
        {
            _ = buffer.RemoveByEffectId(definition.Id);
        }
    }

    private bool IsDrowningEffect(ushort effectId) =>
        _statusEffects.Catalog.TryGetByKey(_chunkWorld.WaterRules.DrowningEffect, out StatusEffectDefinition definition) &&
        definition.Id == effectId;

    // Sends the owner's swim state and oxygen when the state or tier changes or the oxygen crosses a whole percent.
    private void QueueOxygenSyncLocked(PlayerState state)
    {
        WaterRules rules = _chunkWorld.WaterRules;
        float oxygen = float.IsNaN(state.OxygenSeconds) ? rules.MaxOxygenSeconds : state.OxygenSeconds;
        int percent = (int)MathF.Ceiling(oxygen / rules.MaxOxygenSeconds * 100f);
        int water = ((int)state.SwimState << 8) | (int)state.WaterTier;
        if (percent == state.OxygenSyncedPercent && water == state.OxygenSyncedWater)
        {
            return;
        }

        state.OxygenSyncedPercent = percent;
        state.OxygenSyncedWater = water;
        _outbound.Pending.OxygenSyncs.Add(new OutboundOxygenSync(
            state.Session.ConnectionId,
            new PlayerOxygenSnapshot(
                (byte)state.SwimState,
                (byte)state.WaterTier,
                (uint)MathF.Round(oxygen * 1000f),
                (uint)MathF.Round(rules.MaxOxygenSeconds * 1000f))));
    }
}
