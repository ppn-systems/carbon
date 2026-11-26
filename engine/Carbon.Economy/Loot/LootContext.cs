// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// Server-side facts about a kill or gather action that drive a loot roll.
/// </summary>
/// <param name="SourceLevel">Monster (or node) level.</param>
/// <param name="PlayerLevel">Looting player's level (<c>0</c> disables the trivial-monster penalty).</param>
/// <param name="IsElite">Whether the monster is elite.</param>
/// <param name="IsBoss">Whether the monster is a boss.</param>
/// <param name="DistanceTier">Distance band from the world origin (deeper = richer).</param>
public readonly record struct LootContext(int SourceLevel, int PlayerLevel = 0, bool IsElite = false, bool IsBoss = false, int DistanceTier = 0);
