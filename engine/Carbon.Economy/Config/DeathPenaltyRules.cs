// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Gold a player drops when they die (<c>economy.json → deathPenalty</c>, ADR-031). The amount is
/// <c>clamp(carried × dropPercent / 100, floorGoldPerLevel × level, capGoldPerLevel × level)</c>, never more than carried,
/// and nothing below <see cref="MinLevel"/>. Diamonds are never dropped. Server-only (ADR-014 D10).
/// </summary>
public sealed record DeathPenaltyRules
{
    /// <summary>Gets the lowest level that drops gold on death; younger characters drop nothing (newbie protection).</summary>
    public int MinLevel { get; set; } = 10;

    /// <summary>Gets the share of carried gold dropped, in percent.</summary>
    public int DropPercent { get; set; } = 10;

    /// <summary>Gets the minimum drop per character level (limited by what the player carries).</summary>
    public int FloorGoldPerLevel { get; set; } = 2;

    /// <summary>Gets the maximum drop per character level.</summary>
    public int CapGoldPerLevel { get; set; } = 30;

    /// <summary>Gets the gold per pile above which the drop is split into more piles.</summary>
    public int GoldPerPile { get; set; } = 250;

    /// <summary>Gets the most piles one death drops.</summary>
    public int MaxPiles { get; set; } = 5;

    /// <summary>Gets the seconds during which only the dead player may pick their gold up.</summary>
    public int OwnerProtectionSeconds { get; set; } = 120;

    /// <summary>Gets the seconds the dropped gold lies on the ground before it despawns.</summary>
    public int LootLifetimeSeconds { get; set; } = 300;
}
