// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Creatures.Definitions;

/// <summary>
/// Root JSON document of <c>resources/config/monsters.json</c>.
/// </summary>
public sealed class MonsterCatalogDocument
{
    /// <summary>Gets the schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Gets the monster definitions.</summary>
    public IReadOnlyList<MonsterDefinition> Monsters { get; set; } = [];
}
