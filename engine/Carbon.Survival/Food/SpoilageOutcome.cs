// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Food;

/// <summary>
/// Result of one <see cref="FoodSpoilage.Advance"/> step.
/// </summary>
public enum SpoilageOutcome : byte
{
    /// <summary>The slot is not perishable food; nothing changed.</summary>
    Unchanged = 0,

    /// <summary>The countdown was started; the client should be told the new freshness.</summary>
    Stamped = 1,

    /// <summary>The countdown advanced; the server keeps it without a packet (clients count down locally).</summary>
    Aging = 2,

    /// <summary>The stack spoiled and was replaced (or removed); the client must be told.</summary>
    Spoiled = 3,
}
