// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Ground rules for monster kill loot (<c>economy.json → groundLoot</c>): how long only the killer may pick it up and how
/// long it lies on the ground. Server-only (ADR-014 D10); clients learn the remaining protection from each loot spawn.
/// </summary>
public sealed record GroundLootRules
{
    /// <summary>Gets the seconds after a kill during which only the killer may pick up its loot (0 = no protection).</summary>
    public int OwnerProtectionSeconds { get; set; } = 60;

    /// <summary>Gets the seconds kill loot (items, gold and diamonds) lies on the ground before it despawns.</summary>
    public int KillLootLifetimeSeconds { get; set; } = 180;
}
