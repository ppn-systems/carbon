// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Application.World.StatusEffects;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Water;

/// <summary>
/// Swimming, diving and oxygen (ADR-032 D10–D12, audit SRP-8, extracted from <see cref="AuthoritativeWorld"/>). The depth
/// tier under a player sets their swim state; diving uses up the oxygen pool and anything else refills it. With the pool
/// empty the player drowns: the <c>drowning</c> status effect (ADR-029) pulses lethal damage until they surface, and a
/// lethal pulse takes the normal death path. The rules are the world's current <c>water.json</c>
/// (<see cref="ChunkWorld2D.WaterRules"/>, hot-reloadable). Called only under the world lock.
/// </summary>
internal sealed class OxygenService
{
    private readonly WorldSimulation _simulation;
    private readonly ChunkWorld2D _chunkWorld;
    private readonly StatusEffectService _statusEffects;
    private readonly OutboundTickBuffer _outbound;

    /// <summary>Initializes a new instance of the <see cref="OxygenService"/> class.</summary>
    /// <param name="simulation">World simulation (player transforms).</param>
    /// <param name="chunkWorld">World terrain: water depth and the current water rules.</param>
    /// <param name="statusEffects">Status effects (drowning, water putting out burning).</param>
    /// <param name="outbound">Outbound buffer for the owner's oxygen sync.</param>
    public OxygenService(WorldSimulation simulation, ChunkWorld2D chunkWorld, StatusEffectService statusEffects, OutboundTickBuffer outbound)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _chunkWorld = chunkWorld ?? throw new ArgumentNullException(nameof(chunkWorld));
        _statusEffects = statusEffects ?? throw new ArgumentNullException(nameof(statusEffects));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
    }

    /// <summary>Gets the water movement and oxygen rules the world runs on (<c>water.json</c>).</summary>
    public WaterRules Rules => _chunkWorld.WaterRules;

    /// <summary>Gets a player's oxygen in seconds; a player who has not breathed since entering or respawning has a full breath.</summary>
    /// <param name="state">Player.</param>
    public float OxygenOf(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return float.IsNaN(state.OxygenSeconds) ? _chunkWorld.WaterRules.MaxOxygenSeconds : state.OxygenSeconds;
    }

    /// <summary>Gives a player a full breath (a respawn at the sanctuary).</summary>
    /// <param name="state">Player.</param>
    public static void RefillBreath(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.OxygenSeconds = float.NaN;
    }

    /// <summary>Updates every player's swim state, oxygen and drowning for one tick.</summary>
    /// <param name="players">Players by connection.</param>
    /// <param name="dt">Tick length in seconds.</param>
    public void Step(Dictionary<ulong, PlayerState> players, float dt)
    {
        ArgumentNullException.ThrowIfNull(players);
        WaterRules rules = _chunkWorld.WaterRules;
        foreach (KeyValuePair<ulong, PlayerState> kvp in players)
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

            this.UpdateDrowning(state, drowning: drain > 0f && state.OxygenSeconds <= 0f, rules);
        }
    }

    /// <summary>Returns whether <paramref name="effectId"/> is the drowning effect (a death by it is a drowning).</summary>
    /// <param name="effectId">Status effect id.</param>
    public bool IsDrowningEffect(ushort effectId) =>
        _statusEffects.Catalog.TryGetByKey(_chunkWorld.WaterRules.DrowningEffect, out StatusEffectDefinition definition) &&
        definition.Id == effectId;

    /// <summary>Sends the owner's swim state and oxygen when the state or tier changes or the oxygen crosses a whole percent.</summary>
    /// <param name="state">Player.</param>
    public void QueueSync(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WaterRules rules = _chunkWorld.WaterRules;
        float oxygen = this.OxygenOf(state);
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

    // The drowning effect is added once when the breath runs out and removed once on surfacing, so the owner's status
    // list changes twice per drowning, not every tick.
    private void UpdateDrowning(PlayerState state, bool drowning, WaterRules rules)
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
}
