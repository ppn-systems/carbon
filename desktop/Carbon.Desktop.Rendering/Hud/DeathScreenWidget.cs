// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// The death screen (ADR-031): darkens and drains colour from the world, then shows the localized "You died" title,
/// the killer or cause, and a Respawn button (Enter also respawns). The gold the server drops at the death spot is not
/// mentioned. The server alone decides the death and the respawn; this only draws <see cref="DeathView"/>.
/// </summary>
internal sealed class DeathScreenWidget
{
    /// <summary>Panel width.</summary>
    public const float PanelWidth = 520f;

    /// <summary>Panel height.</summary>
    public const float PanelHeight = 170f;

    private const float ButtonWidth = 180f;
    private const float ButtonHeight = 40f;

    private static readonly ColorRgba s_veil = new(18, 18, 22, 168);
    private static readonly ColorRgba s_bloodTint = new(70, 8, 12, 64);
    private static readonly ColorRgba s_title = new(232, 86, 86, 255);

    private readonly CachedText _killedBy = new();

    /// <summary>Returns the Respawn button's screen rectangle for a viewport.</summary>
    public static RectangleF GetRespawnButtonRect(int viewportWidth, int viewportHeight)
    {
        (float x, float y) = Origin(viewportWidth, viewportHeight);
        return new RectangleF(x + ((PanelWidth - ButtonWidth) * 0.5f), y + PanelHeight - ButtonHeight - 22f, ButtonWidth, ButtonHeight);
    }

    public static void DrawVeil(GeometryBatch batch, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        RectangleF screen = new(0f, 0f, viewportWidth, viewportHeight);
        batch.FillRectangle(screen, s_veil);
        batch.FillRectangle(screen, s_bloodTint);
    }

    public static void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, bool pending, int viewportWidth, int viewportHeight, Vector2? mouse)
    {
        ArgumentNullException.ThrowIfNull(skin);
        (float x, float y) = Origin(viewportWidth, viewportHeight);
        skin.DrawWindow(spriteBatch, new RectangleF(x, y, PanelWidth, PanelHeight), CarbonUiPalette.DangerBorder, bodyAlpha: 240);
        RectangleF button = GetRespawnButtonRect(viewportWidth, viewportHeight);
        bool hover = !pending && mouse is Vector2 m && button.Contains(in m);
        ColorRgba fill = pending ? CarbonUiPalette.CardFill : (hover ? new ColorRgba(48, 128, 90, 250) : CarbonUiPalette.ConfirmFill);
        skin.DrawButton(spriteBatch, in button, fill, CarbonUiPalette.ConfirmBorder);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, in DeathView death, bool pending, int viewportWidth, int viewportHeight)
    {
        (float x, float y) = Origin(viewportWidth, viewportHeight);
        ColorRgba outline = CarbonUiPalette.TextOutline;
        DeathScreenText text = this.GetText(in death, pending);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, text.Title, new RectangleF(x, y + 14f, PanelWidth, 44f), s_title, outline, 2f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, text.Cause, new RectangleF(x, y + 66f, PanelWidth, 28f), CarbonUiPalette.TextPrimary, outline, 1f);
        RectangleF button = GetRespawnButtonRect(viewportWidth, viewportHeight);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, text.Button, in button, CarbonUiPalette.TextPrimary, outline, 1f);
        CarbonUiSkin.DrawCenteredText(
            spriteBatch, font, text.Hint, new RectangleF(x, y + PanelHeight + 8f, PanelWidth, 22f), CarbonUiPalette.TextWarm, outline, 1f);
    }

    /// <summary>Gets every line <see cref="DrawText"/> draws for <paramref name="death"/>, in drawing order.</summary>
    internal DeathScreenText GetText(in DeathView death, bool pending) => new(
        GameText.Get("death.title"),
        this.DescribeCause(in death),
        GameText.Get(pending ? "death.respawning" : "death.respawn"),
        GameText.Get("death.respawn_hint"));

    private static (float X, float Y) Origin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelWidth) * 0.5f), MathF.Floor((viewportHeight - PanelHeight) * 0.14f));

    internal string DescribeCause(in DeathView death) => death.Cause switch
    {
        DeathCause.Monster when death.KillerNameKey is { Length: > 0 } key => _killedBy.Format("death.killed_by", new TextArg("name", GameText.Get(key))),
        DeathCause.Starvation => GameText.Get("death.cause.starvation"),
        DeathCause.Drowning => GameText.Get("death.cause.drowning"),
        DeathCause.Unknown or DeathCause.Monster => GameText.Get("death.cause.unknown"),
        _ => GameText.Get("death.cause.unknown"),
    };
}
