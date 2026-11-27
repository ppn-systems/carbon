// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>What a minimap marker stands for.</summary>
internal enum MinimapIconKind : byte
{
    /// <summary>A village (from the seed-generated points of interest the client already renders).</summary>
    Village,

    /// <summary>A trading NPC (merchant or general trader) inside the area of interest.</summary>
    Trader,

    /// <summary>A blacksmith NPC (forge artisan) inside the area of interest.</summary>
    Blacksmith,

    /// <summary>Any other friendly NPC inside the area of interest.</summary>
    Npc,

    /// <summary>Another player inside the area of interest.</summary>
    Player,

    /// <summary>A monster inside the area of interest.</summary>
    Monster,
}
