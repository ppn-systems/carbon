// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Items.Definitions;

/// <summary>
/// Root JSON document of <c>resources/config/items.json</c>.
/// </summary>
public sealed record ItemCatalogDocument
{
    /// <summary>Gets the schema version.</summary>
    public int Version { get; set; }

    /// <summary>Gets every item definition.</summary>
    public IReadOnlyList<ItemCatalogEntry> Items { get; set; } = [];
}
