// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Stats;

/// <summary>
/// Tagged attribute modifier linked to a source identifier (equipment piece, buff, or passive trait) for clean removal.
/// </summary>
/// <param name="SourceId">Identifier of the equipment, aura, or buff that owns this modifier.</param>
/// <param name="Stat">Target stat modified.</param>
/// <param name="Kind">Stacking aggregation mode.</param>
/// <param name="Value">Numeric modifier value.</param>
public readonly record struct StatModifier(
    uint SourceId,
    StatType Stat,
    StatModKind Kind,
    float Value);
