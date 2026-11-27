// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// What a replicated <see cref="EntityKind.LootDrop"/> carries (ADR-019). Append-only values for wire compatibility;
/// the values match the server wallet's currency ids.
/// </summary>
public enum LootCurrency : byte
{
    /// <summary>An item stack (<c>ArchetypeId</c> is the item id).</summary>
    None = 0,

    /// <summary>A pile of gold (<c>LootCount</c> is the amount).</summary>
    Gold = 1,

    /// <summary>A pile of diamonds (<c>LootCount</c> is the amount).</summary>
    Diamond = 2,
}
