// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Combat.Stats;

/// <summary>
/// Dirty-cached stat aggregator recomputing <c>(Base + Flat) * (1 + SumPercentAdd) * Prod(1 + PercentMult)</c>
/// from base values and tagged <see cref="StatModifier"/> entries without cumulative float drift.
/// </summary>
public sealed class StatSheet
{
    private const int StatCount = (int)StatType.CritMultiplier + 1;

    private readonly float[] _baseValues = new float[StatCount];
    private readonly float[] _cachedValues = new float[StatCount];
    private readonly List<StatModifier> _modifiers = new(16);
    private ulong _dirtyMask = ulong.MaxValue;

    /// <summary>
    /// Sets the base value for <paramref name="stat"/> and marks it dirty for recomputation.
    /// </summary>
    /// <param name="stat">Target stat.</param>
    /// <param name="value">New base value.</param>
    public void SetBase(StatType stat, float value)
    {
        int idx = ValidateIndex(stat);
        _baseValues[idx] = value;
        _dirtyMask |= 1UL << idx;
    }

    /// <summary>
    /// Gets the un-modified base value for <paramref name="stat"/>.
    /// </summary>
    /// <param name="stat">Target stat.</param>
    /// <returns>Base stat value.</returns>
    public float GetBase(StatType stat) => _baseValues[ValidateIndex(stat)];

    /// <summary>
    /// Appends a modifier and marks its target stat dirty.
    /// </summary>
    /// <param name="modifier">Modifier to apply.</param>
    public void AddModifier(in StatModifier modifier)
    {
        int idx = ValidateIndex(modifier.Stat);
        _modifiers.Add(modifier);
        _dirtyMask |= 1UL << idx;
    }

    /// <summary>
    /// Removes all modifiers originating from <paramref name="sourceId"/> and invalidates affected stats.
    /// </summary>
    /// <param name="sourceId">Owning source identifier.</param>
    /// <returns>Number of modifiers removed.</returns>
    public int RemoveModifiersFromSource(uint sourceId)
    {
        int removed = 0;
        for (int i = _modifiers.Count - 1; i >= 0; i--)
        {
            if (_modifiers[i].SourceId == sourceId)
            {
                int idx = ValidateIndex(_modifiers[i].Stat);
                _dirtyMask |= 1UL << idx;
                _modifiers.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Gets the effective aggregated value for <paramref name="stat"/>, recomputing if dirty.
    /// </summary>
    /// <param name="stat">Target stat.</param>
    /// <returns>Effective stat value.</returns>
    public float GetValue(StatType stat)
    {
        int idx = ValidateIndex(stat);
        ulong bit = 1UL << idx;
        if ((_dirtyMask & bit) != 0UL)
        {
            _cachedValues[idx] = this.Recompute(stat, idx);
            _dirtyMask &= ~bit;
        }

        return _cachedValues[idx];
    }

    /// <summary>
    /// Clears every modifier while retaining base values.
    /// </summary>
    public void ClearModifiers()
    {
        _modifiers.Clear();
        _dirtyMask = ulong.MaxValue;
    }

    private float Recompute(StatType stat, int idx)
    {
        float flat = 0f;
        float percentAdd = 0f;
        float percentMult = 1f;

        for (int i = 0; i < _modifiers.Count; i++)
        {
            StatModifier mod = _modifiers[i];
            if (mod.Stat != stat)
            {
                continue;
            }

            switch (mod.Kind)
            {
                case StatModKind.Flat:
                    flat += mod.Value;
                    break;
                case StatModKind.PercentAdd:
                    percentAdd += mod.Value;
                    break;
                case StatModKind.PercentMult:
                    percentMult *= MathF.Max(0f, 1f + mod.Value);
                    break;
                default:
                    break;
            }
        }

        return (_baseValues[idx] + flat) * MathF.Max(0f, 1f + percentAdd) * percentMult;
    }

    private static int ValidateIndex(StatType stat)
    {
        int idx = (int)stat;
        if ((uint)idx >= StatCount)
        {
            throw new ArgumentOutOfRangeException(nameof(stat));
        }

        return idx;
    }
}
