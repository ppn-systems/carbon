// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Economy.Loot;

/// <summary>
/// Root JSON document of <c>resources/config/loot.json</c>.
/// </summary>
public sealed record LootTableDocument
{
    /// <summary>Gets the schema version.</summary>
    public int Version { get; set; }

    /// <summary>Gets every loot table.</summary>
    public IReadOnlyList<LootTableDefinition> Tables { get; set; } = [];
}
