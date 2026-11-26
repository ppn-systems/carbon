// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// Per-character bad-luck protection counter; persist it with the character.
/// </summary>
/// <param name="KillsSinceDiamond">Eligible (non-trivial) kills since the last diamond.</param>
public record struct LootPityState(int KillsSinceDiamond);
