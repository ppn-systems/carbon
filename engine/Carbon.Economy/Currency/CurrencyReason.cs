// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Currency;

/// <summary>
/// Audit reason attached to every currency mutation; classifies faucets (credits) and sinks (debits).
/// Values are stable (append, never renumber).
/// </summary>
public enum CurrencyReason : byte
{
    /// <summary>Unspecified.</summary>
    None = 0,

    /// <summary>Faucet: monster kill loot.</summary>
    MonsterLoot = 1,

    /// <summary>Faucet: mining or harvesting loot.</summary>
    GatherLoot = 2,

    /// <summary>Faucet: player sold items to an NPC vendor.</summary>
    VendorSale = 3,

    /// <summary>Sink: player bought items from an NPC vendor.</summary>
    VendorPurchase = 4,

    /// <summary>Sink: equipment repair.</summary>
    Repair = 5,

    /// <summary>Sink: crafting station fee.</summary>
    CraftingFee = 6,

    /// <summary>Neutral: player-to-player transfer.</summary>
    Trade = 7,

    /// <summary>Faucet: quest reward.</summary>
    QuestReward = 8,

    /// <summary>Administrative correction.</summary>
    Admin = 9,

    /// <summary>Refund of a previous debit.</summary>
    Refund = 10,

    /// <summary>Gold fee for a forge enhancement attempt (ADR-024).</summary>
    Enhancement = 11
}
