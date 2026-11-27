// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Pure reading of the server's world time for the HUD clock. The hour always comes from
/// <c>IWorldReadModel.AuthoritativeTimeHour</c> (server tick and tick rate) and night from
/// <c>IWorldReadModel.AuthoritativeIsNight</c>; the clock never advances on its own and holds no gameplay numbers.
/// Dawn and dusk are cosmetic bands around the night the session reports.
/// </summary>
internal static class WorldClock
{
    /// <summary>Hour full day begins (before it, outside the night, is dawn).</summary>
    internal const float DayStartHour = 7f;

    /// <summary>Hour dusk begins (from it until the night, is dusk).</summary>
    internal const float DuskStartHour = 17f;

    /// <summary>Wraps any hour into <c>[0, 24)</c>.</summary>
    internal static float Wrap(float hour)
    {
        float h = ((hour % 24f) + 24f) % 24f;
        return h >= 24f ? 0f : h;
    }

    /// <summary>Returns the phase of <paramref name="hour"/> given whether the session reports night.</summary>
    internal static DayPhase GetPhase(float hour, bool isNight)
    {
        if (isNight)
        {
            return DayPhase.Night;
        }

        float h = Wrap(hour);
        return h < 12f
            ? (h < DayStartHour ? DayPhase.Dawn : DayPhase.Day)
            : (h >= DuskStartHour ? DayPhase.Dusk : DayPhase.Day);
    }

    /// <summary>Formats <paramref name="hour"/> as 24-hour <c>HH:MM</c> into <paramref name="buffer"/> (at least 5 chars).</summary>
    internal static ReadOnlySpan<char> FormatTime(float hour, Span<char> buffer)
    {
        // The epsilon absorbs float error (6.4h * 60 is 383.99997): a displayed minute never lags its own hour value.
        int totalMinutes = (int)MathF.Floor((Wrap(hour) * 60f) + 1e-3f) % (24 * 60);
        int hh = totalMinutes / 60;
        int mm = totalMinutes % 60;
        buffer[0] = (char)('0' + (hh / 10));
        buffer[1] = (char)('0' + (hh % 10));
        buffer[2] = ':';
        buffer[3] = (char)('0' + (mm / 10));
        buffer[4] = (char)('0' + (mm % 10));
        return buffer[..5];
    }

    /// <summary>Gets the sun/moon angle on the dial in radians: <c>0</c> at noon (top), <c>π</c> at midnight (bottom).</summary>
    internal static float DialAngle(float hour) => (Wrap(hour) - 12f) / 24f * MathF.Tau;
}
