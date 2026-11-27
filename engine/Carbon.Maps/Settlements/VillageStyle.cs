// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Settlements;

/// <summary>Architectural style of a generated village (matches the landmark house styles). Append only.</summary>
public enum VillageStyle : byte
{
    /// <summary>Timber houses of temperate river valleys.</summary>
    Temperate = 0,

    /// <summary>Sandstone houses of deserts and mesas.</summary>
    Desert = 1,

    /// <summary>Stone cabins of the frozen tundra.</summary>
    Snow = 2,

    /// <summary>Timber houses on raised plank decks in misty swamps.</summary>
    Swamp = 3,
}
