// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Phase of the in-game day shown by the HUD clock.</summary>
internal enum DayPhase : byte
{
    /// <summary>05:00 to 07:00.</summary>
    Dawn,

    /// <summary>07:00 to 17:00.</summary>
    Day,

    /// <summary>17:00 to 19:00.</summary>
    Dusk,

    /// <summary>19:00 to 05:00, when monsters are empowered (<c>WorldProgression.IsNightHour</c>).</summary>
    Night,
}
