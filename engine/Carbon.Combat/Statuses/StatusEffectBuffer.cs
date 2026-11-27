// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Per-entity container managing active buffs, debuffs, stacking rules (ADR-029 D3), crowd-control flags, and periodic
/// pulses. Entries are unmanaged structs in one array that only grows past its initial 8 slots.
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
    /// Gets a counter bumped by every change the owner must be told about: an effect added, stacked, refreshed,
    /// replaced, expired or removed. Pulses and the countdown do not bump it.
    /// </summary>
    public uint Version { get; private set; }

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
    /// Gets the movement speed scale: 0 while stunned or rooted, otherwise the product of every active
    /// <see cref="ActiveStatusEffect.MoveSpeedMultiplier"/>.
    /// </summary>
    public float MoveSpeedScale
    {
        get
        {
            float scale = 1f;
            for (int i = 0; i < _count; i++)
            {
                if ((_effects[i].ControlMask & (CrowdControlMask.Stunned | CrowdControlMask.Rooted)) != 0)
                {
                    return 0f;
                }

                scale *= _effects[i].MoveSpeedMultiplier;
            }

            return scale;
        }
    }

    /// <summary>Gets the product of every active <see cref="ActiveStatusEffect.HealthRegenMultiplier"/>.</summary>
    public float HealthRegenMultiplier
    {
        get
        {
            float scale = 1f;
            for (int i = 0; i < _count; i++)
            {
                scale *= _effects[i].HealthRegenMultiplier;
            }

            return scale;
        }
    }

    /// <summary>Gets the product of every active <see cref="ActiveStatusEffect.StaminaRegenMultiplier"/>.</summary>
    public float StaminaRegenMultiplier
    {
        get
        {
            float scale = 1f;
            for (int i = 0; i < _count; i++)
            {
                scale *= _effects[i].StaminaRegenMultiplier;
            }

            return scale;
        }
    }

    /// <summary>
    /// Gets a read-only span over active status effects.
    /// </summary>
    public ReadOnlySpan<ActiveStatusEffect> ActiveEffects => _effects.AsSpan(0, _count);

    /// <summary>
    /// Applies a new status effect, or merges it into the active entry with the same
    /// <see cref="ActiveStatusEffect.EffectId"/> according to <see cref="ActiveStatusEffect.Stacking"/> of the incoming effect.
    /// </summary>
    /// <param name="effect">Status effect specification to apply.</param>
    /// <returns><see langword="true"/> when the buffer changed; <see langword="false"/> when a weaker effect was ignored.</returns>
    public bool ApplyOrRefresh(in ActiveStatusEffect effect)
    {
        byte maxStacks = Math.Max((byte)1, effect.MaxStacks);
        for (int i = 0; i < _count; i++)
        {
            if (_effects[i].EffectId != effect.EffectId)
            {
                continue;
            }

            ActiveStatusEffect current = _effects[i];
            ActiveStatusEffect merged;
            switch (effect.Stacking)
            {
                case StatusStacking.Refresh:
                    merged = WithPulseCountdown(effect with { Stacks = 1, MaxStacks = maxStacks }, current.TicksUntilNextPulse);
                    break;
                case StatusStacking.Strongest:
                    if (effect.Strength > current.Strength)
                    {
                        merged = WithPulseCountdown(effect with { Stacks = 1, MaxStacks = maxStacks }, current.TicksUntilNextPulse);
                    }
                    else if (effect.Strength == current.Strength && effect.RemainingTicks > current.RemainingTicks)
                    {
                        merged = current with { RemainingTicks = effect.RemainingTicks, DurationTicks = effect.DurationTicks };
                    }
                    else
                    {
                        return false;
                    }

                    break;
                case StatusStacking.Stack:
                default:
                    byte nextStacks = (byte)Math.Min(maxStacks, current.Stacks + Math.Max((byte)1, effect.Stacks));
                    uint remaining = Math.Max(current.RemainingTicks, effect.RemainingTicks);
                    merged = current with
                    {
                        Caster = effect.Caster,
                        ControlMask = effect.ControlMask,
                        MagnitudePerStack = effect.MagnitudePerStack,
                        RemainingTicks = remaining,
                        DurationTicks = Math.Max(remaining, effect.DurationTicks),
                        PulseIntervalTicks = effect.PulseIntervalTicks,
                        Stacks = nextStacks,
                        MaxStacks = maxStacks,
                        IsDamage = effect.IsDamage,
                        Lethal = effect.Lethal,
                        MoveSpeedMultiplier = effect.MoveSpeedMultiplier,
                        HealthRegenMultiplier = effect.HealthRegenMultiplier,
                        StaminaRegenMultiplier = effect.StaminaRegenMultiplier,
                        Source = effect.Source,
                        SourceId = effect.SourceId,
                    };
                    break;
            }

            _effects[i] = merged;
            this.Version++;
            return true;
        }

        if (_count >= _effects.Length)
        {
            Array.Resize(ref _effects, _effects.Length * 2);
        }

        byte initialStacks = Math.Clamp(effect.Stacks, (byte)1, maxStacks);
        _effects[_count++] = WithPulseCountdown(effect with { Stacks = initialStacks, MaxStacks = maxStacks }, 0);
        this.Version++;
        return true;
    }

    /// <summary>Gets the active entry for <paramref name="effectId"/>.</summary>
    /// <param name="effectId">Status effect identifier.</param>
    /// <param name="effect">The active entry when found.</param>
    /// <returns><see langword="true"/> when the effect is active.</returns>
    public bool TryGet(uint effectId, out ActiveStatusEffect effect)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_effects[i].EffectId == effectId)
            {
                effect = _effects[i];
                return true;
            }
        }

        effect = default;
        return false;
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
                            effect.IsDamage,
                            effect.Lethal);
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
    public void Clear()
    {
        if (_count > 0)
        {
            _count = 0;
            this.Version++;
        }
    }

    private static ActiveStatusEffect WithPulseCountdown(in ActiveStatusEffect effect, uint runningCountdown)
    {
        uint countdown = effect.TicksUntilNextPulse != 0 ? effect.TicksUntilNextPulse
            : runningCountdown != 0 ? runningCountdown
            : effect.PulseIntervalTicks;
        uint duration = effect.DurationTicks != 0 ? effect.DurationTicks : effect.RemainingTicks;
        return effect with { TicksUntilNextPulse = countdown, DurationTicks = duration };
    }

    private void RemoveAt(int index)
    {
        int last = _count - 1;
        if (index != last)
        {
            _effects[index] = _effects[last];
        }

        _count--;
        this.Version++;
    }
}
