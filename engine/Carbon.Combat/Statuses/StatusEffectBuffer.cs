// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Per-entity container managing active buffs, debuffs, stack capping, crowd-control flags, and periodic pulses.
/// </summary>
public sealed class StatusEffectBuffer
{
    private ActiveStatusEffect[] _effects = new ActiveStatusEffect[8];
    private int _count;

    /// <summary>
    /// Gets the number of active status effects currently on this actor.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Computes the combined <see cref="CrowdControlMask"/> across all active status effects.
    /// </summary>
    public CrowdControlMask ActiveControlMask
    {
        get
        {
            CrowdControlMask mask = CrowdControlMask.None;
            for (int i = 0; i < _count; i++)
            {
                mask |= _effects[i].ControlMask;
            }

            return mask;
        }
    }

    /// <summary>
    /// Gets a read-only span over active status effects.
    /// </summary>
    public ReadOnlySpan<ActiveStatusEffect> ActiveEffects => _effects.AsSpan(0, _count);

    /// <summary>
    /// Applies a new status effect or refreshes/stacks an existing entry with matching <see cref="ActiveStatusEffect.EffectId"/>.
    /// </summary>
    /// <param name="effect">Status effect specification to apply.</param>
    public void ApplyOrRefresh(in ActiveStatusEffect effect)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_effects[i].EffectId == effect.EffectId)
            {
                ActiveStatusEffect current = _effects[i];
                byte maxStacks = Math.Max((byte)1, effect.MaxStacks);
                byte nextStacks = (byte)Math.Min(maxStacks, current.Stacks + Math.Max((byte)1, effect.Stacks));

                _effects[i] = current with
                {
                    Caster = effect.Caster,
                    ControlMask = effect.ControlMask,
                    MagnitudePerStack = effect.MagnitudePerStack,
                    RemainingTicks = Math.Max(current.RemainingTicks, effect.RemainingTicks),
                    PulseIntervalTicks = effect.PulseIntervalTicks,
                    Stacks = nextStacks,
                    MaxStacks = maxStacks,
                    IsDamage = effect.IsDamage
                };
                return;
            }
        }

        if (_count >= _effects.Length)
        {
            Array.Resize(ref _effects, _effects.Length * 2);
        }

        byte initialStacks = Math.Clamp(effect.Stacks, (byte)1, Math.Max((byte)1, effect.MaxStacks));
        uint pulseCountdown = effect.PulseIntervalTicks > 0 && effect.TicksUntilNextPulse == 0
            ? effect.PulseIntervalTicks
            : effect.TicksUntilNextPulse;

        _effects[_count++] = effect with
        {
            Stacks = initialStacks,
            MaxStacks = Math.Max((byte)1, effect.MaxStacks),
            TicksUntilNextPulse = pulseCountdown
        };
    }

    /// <summary>
    /// Removes the status effect matching <paramref name="effectId"/> if active.
    /// </summary>
    /// <param name="effectId">Status effect identifier.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveByEffectId(uint effectId)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_effects[i].EffectId == effectId)
            {
                this.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Advances all active status effects by one fixed simulation tick, emitting periodic pulses into
    /// <paramref name="pulseBuffer"/> and expiring completed effects.
    /// </summary>
    /// <param name="owner">Entity owning this status buffer.</param>
    /// <param name="pulseBuffer">Caller-supplied span receiving periodic pulses triggered on this tick.</param>
    /// <param name="pulseCount">Output number of pulses written to <paramref name="pulseBuffer"/>.</param>
    public void AdvanceTick(Entity owner, Span<StatusPulseEvent> pulseBuffer, out int pulseCount)
    {
        pulseCount = 0;

        for (int i = _count - 1; i >= 0; i--)
        {
            ActiveStatusEffect effect = _effects[i];
            uint remaining = effect.RemainingTicks > 0 ? effect.RemainingTicks - 1 : 0;
            uint pulseTimer = effect.TicksUntilNextPulse;

            if (effect.PulseIntervalTicks > 0)
            {
                if (pulseTimer <= 1)
                {
                    if (pulseCount < pulseBuffer.Length)
                    {
                        pulseBuffer[pulseCount++] = new StatusPulseEvent(
                            effect.Caster,
                            owner,
                            effect.EffectId,
                            effect.MagnitudePerStack * effect.Stacks,
                            effect.IsDamage);
                    }

                    pulseTimer = effect.PulseIntervalTicks;
                }
                else
                {
                    pulseTimer--;
                }
            }

            if (remaining == 0)
            {
                this.RemoveAt(i);
            }
            else
            {
                _effects[i] = effect with
                {
                    RemainingTicks = remaining,
                    TicksUntilNextPulse = pulseTimer
                };
            }
        }
    }

    /// <summary>
    /// Removes all active status effects.
    /// </summary>
    public void Clear() => _count = 0;

    private void RemoveAt(int index)
    {
        int last = _count - 1;
        if (index != last)
        {
            _effects[index] = _effects[last];
        }

        _count--;
    }
}
