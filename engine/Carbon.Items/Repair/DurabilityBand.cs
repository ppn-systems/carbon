// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Repair;

/// <summary>How close an item is to breaking (ADR-035), from <c>repair.json → durabilityWarning</c>.</summary>
public enum DurabilityBand : byte
{
    /// <summary>Above the low share: the bar is green.</summary>
    Healthy = 0,

    /// <summary>At or below the low share: the bar is yellow.</summary>
    Low = 1,

    /// <summary>At or below the critical share: the bar is red and the owner was warned.</summary>
    Critical = 2,
}
