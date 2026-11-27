// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.ObjectModel;

namespace Carbon.Economy.Trading;

/// <summary>A kind of trader (<c>traders.json → profiles</c>): what it stocks, how prices vary and how often it restocks.</summary>
public sealed class TraderProfile
{
    /// <summary>Gets the stable id (<c>merchant</c>, <c>general</c>, <c>blacksmith</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the number of stock slots rolled per restock.</summary>
    public required int Slots { get; init; }

    /// <summary>Gets the ± percent a stocked item's price may differ from its list price.</summary>
    public required int PriceVariancePercent { get; init; }

    /// <summary>Gets the restock period in seconds.</summary>
    public required int RestockSeconds { get; init; }

    /// <summary>Gets the most seconds a trader's restock is shifted by (deterministic per trader).</summary>
    public required int RestockJitterSeconds { get; init; }

    /// <summary>Gets the items it may stock.</summary>
    public required ReadOnlyCollection<TraderStockEntry> Entries { get; init; }
}
