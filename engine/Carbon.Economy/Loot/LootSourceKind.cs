// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// What a loot table is rolled for. Values are stable (append, never renumber).
/// </summary>
public enum LootSourceKind : byte
{
    /// <summary>Monster kill.</summary>
    Monster = 0,

    /// <summary>Mining, chopping or harvesting a world node.</summary>
    Gather = 1
}
