// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Compact sun/moon dial with the world time and day phase, at the top of the right-hand HUD column
/// (<see cref="HudLayout.GetClockRect"/>, under the status-effect strip). The hour is the server's
/// (<c>IWorldReadModel.AuthoritativeTimeHour</c>); the widget keeps no clock of its own and hides while offline.
/// The second line counts down to nightfall, and at night warns that monsters are stronger.
/// </summary>
internal sealed class DayNightClockWidget
{
    private const float DialRadius = 17f;

    private static readonly ColorRgba s_ground = new(30, 41, 34, 255);
    private static readonly ColorRgba s_dialRim = new(148, 163, 184, 255);
    private static readonly ColorRgba s_sun = new(253, 224, 71, 255);
    private static readonly ColorRgba s_moon = new(226, 232, 240, 255);
    private static readonly ColorRgba s_nightWarning = new(252, 129, 129, 255);
    private static readonly ColorRgba s_nightBorder = new(129, 98, 196, 255);

    private readonly CachedText _countdown = new();
    private readonly CachedText _duration = new();

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, int viewportWidth, float? hour)
    {
        if (hour is not float h)
        {
            return;
        }

        RectangleF rect = HudLayout.GetClockRect(viewportWidth);
        skin.DrawCard(spriteBatch, in rect, WorldClock.GetPhase(h) == DayPhase.Night ? s_nightBorder : CarbonUiPalette.BorderOuter, bodyAlpha: 236);
    }

    public void DrawGeometry(GeometryBatch batch, int viewportWidth, float? hour)
    {
        if (hour is not float h)
        {
            return;
        }

        Vector2 c = GetDialCenter(viewportWidth);
        DayPhase phase = WorldClock.GetPhase(h);
        batch.FillCircle(c, DialRadius, GetSkyColor(phase), 24);

        // The sun travels the upper half by day; the moon is opposite it, so it rides high at midnight.
        float angle = WorldClock.DialAngle(h);
        Vector2 sun = c + (new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * (DialRadius - 6f));
        Vector2 moon = c - (sun - c);
        if (sun.Y <= c.Y + 2f)
        {
            batch.FillCircle(sun, 4.6f, s_sun, 14);
        }

        if (moon.Y <= c.Y + 2f)
        {
            batch.FillCircle(moon, 4.2f, s_moon, 14);
            batch.FillCircle(moon + new Vector2(1.8f, -1.2f), 3.4f, GetSkyColor(phase), 14);
        }

        // Ground below the horizon, then the rim.
        Span<Vector2> ground = stackalloc Vector2[10];
        for (int i = 0; i < ground.Length; i++)
        {
            float a = MathF.PI * i / (ground.Length - 1);
            ground[i] = c + new Vector2(MathF.Cos(a) * DialRadius, MathF.Sin(a) * DialRadius);
        }

        batch.FillConvexPolygon(ground, s_ground);
        batch.DrawLine(c - new Vector2(DialRadius, 0f), c + new Vector2(DialRadius, 0f), s_dialRim, 1f);
        batch.DrawCircle(c, DialRadius, s_dialRim, 24, 1.4f);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, float? hour)
    {
        if (hour is not float h)
        {
            return;
        }

        RectangleF rect = HudLayout.GetClockRect(viewportWidth);
        ColorRgba outline = CarbonUiPalette.TextOutline;
        DayPhase phase = WorldClock.GetPhase(h);
        float textX = rect.X + 48f;
        float textW = rect.Right - textX - 8f;

        Span<char> time = stackalloc char[5];
        ReadOnlySpan<char> clock = WorldClock.FormatTime(h, time);
        spriteBatch.DrawOutlinedString(font, clock, new Vector2(textX, rect.Y + 8f), CarbonUiPalette.TextPrimary, outline, 1, 1f);
        float clockW = font.MeasureString(clock, scale: 1f).Width;
        string phaseName = GameText.Get(PhaseKey(phase));
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, phaseName, textW - clockW - 8f),
            new Vector2(textX + clockW + 8f, rect.Y + 8f),
            phase == DayPhase.Night ? s_nightBorder : CarbonUiPalette.TextWarm,
            outline,
            1,
            1f);

        string second = phase == DayPhase.Night
            ? GameText.Get("clock.night_hint")
            : this.FormatCountdown(WorldClock.MinutesUntilNight(h));
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, second, textW),
            new Vector2(textX, rect.Y + 28f),
            phase == DayPhase.Night ? s_nightWarning : CarbonUiPalette.TextMuted,
            outline,
            1,
            1f);
    }

    /// <summary>Gets the localization key of a phase name.</summary>
    internal static string PhaseKey(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => "clock.phase.dawn",
        DayPhase.Day => "clock.phase.day",
        DayPhase.Dusk => "clock.phase.dusk",
        DayPhase.Night => "clock.phase.night",
        _ => "clock.phase.day",
    };

    private static Vector2 GetDialCenter(int viewportWidth)
    {
        RectangleF rect = HudLayout.GetClockRect(viewportWidth);
        return new Vector2(rect.X + 25f, rect.Y + (rect.Height * 0.5f));
    }

    private static ColorRgba GetSkyColor(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => new ColorRgba(251, 146, 60, 255),
        DayPhase.Day => new ColorRgba(96, 165, 250, 255),
        DayPhase.Dusk => new ColorRgba(192, 98, 132, 255),
        DayPhase.Night => new ColorRgba(30, 27, 75, 255),
        _ => new ColorRgba(96, 165, 250, 255),
    };

    /// <summary>"Night in 4h 30m": cached, so it re-formats once per in-game minute, not per frame.</summary>
    private string FormatCountdown(int minutes)
    {
        string duration = _duration.Format("time.hours_minutes", new TextArg("hours", minutes / 60), new TextArg("minutes", minutes % 60));
        return _countdown.Format("clock.night_in", new TextArg("time", duration));
    }
}
