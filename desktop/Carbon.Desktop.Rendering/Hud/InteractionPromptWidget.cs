// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Draws the interact-key prompt ("[E] Open") above the one target the key acts on. Text comes from the
/// <c>[interact]</c> locale section; nothing is allocated per frame.
/// </summary>
internal static class InteractionPromptWidget
{
    private const float Height = 24f;
    private const float Padding = 6f;
    private const float KeyCapSize = 18f;
    private const float Gap = 6f;

    /// <summary>Gets the locale key of the action label for <paramref name="kind"/>, or <c>null</c> for none.</summary>
    internal static string? LabelKey(InteractionKind kind) => kind switch
    {
        InteractionKind.PickUp => "interact.pick_up",
        InteractionKind.Trade => "interact.trade",
        InteractionKind.OpenDoor => "interact.open_door",
        InteractionKind.CloseDoor => "interact.close_door",
        InteractionKind.Forge => "interact.forge",
        InteractionKind.None => null,
        _ => null,
    };

    public static void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, BitmapFont font, in InteractionPrompt prompt)
    {
        if (!TryLayout(font, in prompt, out RectangleF box, out RectangleF keyCap, out _, out _))
        {
            return;
        }

        skin.DrawInset(spriteBatch, in box, CarbonUiPalette.WindowFill, CarbonUiPalette.BorderOuter);
        skin.DrawInset(spriteBatch, in keyCap, CarbonUiPalette.SlotSelectedFill, CarbonUiPalette.BorderActive);
    }

    public static void DrawText(SpriteBatch spriteBatch, BitmapFont font, in InteractionPrompt prompt)
    {
        if (!TryLayout(font, in prompt, out _, out RectangleF keyCap, out RectangleF labelBounds, out string label))
        {
            return;
        }

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("interact.key"), in keyCap, CarbonUiPalette.TextWarm, CarbonUiPalette.TextOutline);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, label, in labelBounds, CarbonUiPalette.TextPrimary, CarbonUiPalette.TextOutline);
    }

    private static bool TryLayout(BitmapFont font, in InteractionPrompt prompt, out RectangleF box, out RectangleF keyCap, out RectangleF labelBounds, out string label)
    {
        string? key = LabelKey(prompt.Kind);
        if (key is null)
        {
            box = keyCap = labelBounds = default;
            label = string.Empty;
            return false;
        }

        label = GameText.Get(key);
        float labelWidth = font.MeasureString(label, TextLayoutOptions.Default, 1f).Width;
        float width = Padding + KeyCapSize + Gap + labelWidth + Padding;
        float x = prompt.ScreenAnchor.X - (width * 0.5f);
        float y = prompt.ScreenAnchor.Y - Height;
        box = new RectangleF(x, y, width, Height);
        keyCap = new RectangleF(x + Padding, y + ((Height - KeyCapSize) * 0.5f), KeyCapSize, KeyCapSize);
        labelBounds = new RectangleF(keyCap.Right + Gap, y, labelWidth, Height);
        return true;
    }
}
