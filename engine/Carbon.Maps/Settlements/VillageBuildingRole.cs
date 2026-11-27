// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Settlements;

/// <summary>What a village building is for. Decides which NPC (if any) works there. Append only.</summary>
public enum VillageBuildingRole : byte
{
    /// <summary>A dwelling with beds and storage.</summary>
    House = 0,

    /// <summary>The general goods store (general trader NPC).</summary>
    Store = 1,

    /// <summary>The smithy with the forge anvil (blacksmith NPC).</summary>
    Smithy = 2,

    /// <summary>An open market stall on the square (merchant NPC).</summary>
    Stall = 3,
}
