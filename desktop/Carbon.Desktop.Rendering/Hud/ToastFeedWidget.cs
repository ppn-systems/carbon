// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Manages and draws fading right-aligned loot, craft, and warning toast notifications (SRP).</summary>
internal sealed class ToastFeedWidget
{
    private readonly List<NotificationToast> _toasts = new(8);

    public void Push(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _toasts.Add(new NotificationToast(message.Trim(), 2.8f));
        if (_toasts.Count > 5)
        {
            _toasts.RemoveAt(0);
        }
    }

    public void Step(float frameDt)
    {
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            float rem = _toasts[i].RemainingSeconds - frameDt;
            if (rem <= 0f)
            {
                _toasts.RemoveAt(i);
            }
            else
            {
                _toasts[i] = _toasts[i] with { RemainingSeconds = rem };
            }
        }
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, BitmapFont font, int viewportWidth, bool showMinimap)
    {
        float baseY = showMinimap ? 218f : 18f;
        for (int i = 0; i < _toasts.Count; i++)
        {
            NotificationToast toast = _toasts[i];
            byte alpha = (byte)(Math.Clamp(toast.RemainingSeconds / 0.8f, 0f, 1f) * 235f);
            TextMeasureResult size = font.MeasureString(toast.Message, TextLayoutOptions.Default, 1f);
            float w = size.Width + 24f;
            float x = viewportWidth - w - 14f;
            float y = baseY + (i * 29f);
            ColorRgba border = toast.Message.StartsWith('+')
                ? new ColorRgba(CarbonUiPalette.ConfirmBorder.R, CarbonUiPalette.ConfirmBorder.G, CarbonUiPalette.ConfirmBorder.B, alpha)
                : new ColorRgba(CarbonUiPalette.BorderOuter.R, CarbonUiPalette.BorderOuter.G, CarbonUiPalette.BorderOuter.B, alpha);
            skin.DrawInset(
                spriteBatch,
                new RectangleF(x, y, w, 25f),
                new ColorRgba(CarbonUiPalette.WindowFill.R, CarbonUiPalette.WindowFill.G, CarbonUiPalette.WindowFill.B, alpha),
                border);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, bool showMinimap)
    {
        float baseY = showMinimap ? 218f : 18f;
        for (int i = 0; i < _toasts.Count; i++)
        {
            NotificationToast toast = _toasts[i];
            byte alpha = (byte)(Math.Clamp(toast.RemainingSeconds / 0.8f, 0f, 1f) * 255f);
            TextMeasureResult size = font.MeasureString(toast.Message, TextLayoutOptions.Default, 1f);
            float w = size.Width + 24f;
            float x = viewportWidth - w - 14f;
            float y = baseY + (i * 29f);
            ColorRgba textColor = toast.Message.StartsWith('+')
                ? new ColorRgba(168, 226, 186, alpha)
                : new ColorRgba(CarbonUiPalette.TextPrimary.R, CarbonUiPalette.TextPrimary.G, CarbonUiPalette.TextPrimary.B, alpha);
            CarbonUiSkin.DrawCenteredText(
                spriteBatch,
                font,
                toast.Message,
                new RectangleF(x, y, w, 25f),
                textColor,
                new ColorRgba(CarbonUiPalette.TextOutline.R, CarbonUiPalette.TextOutline.G, CarbonUiPalette.TextOutline.B, alpha),
                1f);
        }
    }

    private readonly record struct NotificationToast(string Message, float RemainingSeconds);
}
