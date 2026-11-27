// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Phase of the in-game day shown by the HUD clock.</summary>
internal enum DayPhase : byte
{
    /// <summary>From the end of the night to 07:00.</summary>
    Dawn,

    /// <summary>07:00 to 17:00.</summary>
    Day,

    /// <summary>From 17:00 to the start of the night.</summary>
    Dusk,

    /// <summary>The night window the session reports (<c>IWorldReadModel.AuthoritativeIsNight</c>): monsters are empowered.</summary>
    Night,
}
