// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Primitives.Random;

/// <summary>
/// Domain-separation tags for <see cref="GameplayKey"/> (ADR-026): each roll kind hashes its own tag first, so two kinds
/// never share a stream even when their other inputs collide. Values are 8-byte ASCII tags read little endian; never reuse or renumber one.
/// </summary>
public static class GameplayDomain
{
    /// <summary>Per-epoch subkey derivation.</summary>
    public const ulong Epoch = 0x48434F50455F4B47UL; // "GK_EPOCH"

    /// <summary>Trader stock subkey derivation (stable across restarts).</summary>
    public const ulong TraderStock = 0x45444152545F4B47UL; // "GK_TRADE"

    /// <summary>Forge enhancement attempt.</summary>
    public const ulong Forge = 0x4547524F465F4B47UL; // "GK_FORGE"

    /// <summary>Loot table roll of a kill or a gathered node.</summary>
    public const ulong Loot = 0x5F544F4F4C5F4B47UL; // "GK_LOOT_"

    /// <summary>Legacy catalog secondary and rare monster drops.</summary>
    public const ulong MonsterDrop = 0x504F52444D5F4B47UL; // "GK_MDROP"

    /// <summary>Critical hit roll.</summary>
    public const ulong Critical = 0x5F544952435F4B47UL; // "GK_CRIT_"

    /// <summary>Evasion (dodge) roll.</summary>
    public const ulong Evasion = 0x45444156455F4B47UL; // "GK_EVADE"

    /// <summary>Food poisoning roll.</summary>
    public const ulong FoodPoisoning = 0x4E53494F505F4B47UL; // "GK_POISN"

    /// <summary>Monster spawn placement, species and elite rolls.</summary>
    public const ulong Spawn = 0x4E574150535F4B47UL; // "GK_SPAWN"

    /// <summary>Split and scatter of the gold a dying player drops (ADR-031).</summary>
    public const ulong DeathDrop = 0x48544145445F4B47UL; // "GK_DEATH"

    /// <summary>Monster hit status effect proc roll (ADR-029 D7).</summary>
    public const ulong StatusProc = 0x53544154535F4B47UL; // "GK_STATS"

    /// <summary><see cref="GameplayKey.GetHashCode"/> (never a roll).</summary>
    public const ulong HashCode = 0x5F485341485F4B47UL; // "GK_HASH_"

    /// <summary>Sapling items a felled tree or a dug-up stump drops (ADR-040 D1).</summary>
    public const ulong SaplingDrop = 0x4E4C5041535F4B47UL; // "GK_SAPLN"
}
