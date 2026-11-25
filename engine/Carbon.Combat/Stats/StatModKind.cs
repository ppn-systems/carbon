// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Stats;

/// <summary>
/// Stacking rule applied by a <see cref="StatModifier"/> during stat aggregation.
/// </summary>
public enum StatModKind : byte
{
    /// <summary>Added directly to the base value before percentage modifiers.</summary>
    Flat = 0,

    /// <summary>Summed into a single additive percentage multiplier (e.g. +0.20 = +20%).</summary>
    PercentAdd = 1,

    /// <summary>Applied multiplicatively as <c>(1 + Value)</c> after additive percentages.</summary>
    PercentMult = 2
}
