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
        float baseY = showMinimap ? 218f : 24f;
        for (int i = 0; i < _toasts.Count; i++)
        {
            NotificationToast toast = _toasts[i];
            byte alpha = (byte)(Math.Clamp(toast.RemainingSeconds / 0.8f, 0f, 1f) * 230f);
            TextMeasureResult size = font.MeasureString(toast.Message, TextLayoutOptions.Default, 1f);
            float w = size.Width + 20f;
            float x = viewportWidth - w - 16f;
            float y = baseY + (i * 25f);
            ColorRgba border = toast.Message.StartsWith('+') ? new ColorRgba(52, 211, 153, alpha) : new ColorRgba(250, 204, 21, alpha);
            skin.DrawInset(spriteBatch, new RectangleF(x, y, w, 22f), new ColorRgba(10, 15, 28, alpha), border);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, bool showMinimap)
    {
        float baseY = showMinimap ? 218f : 24f;
        for (int i = 0; i < _toasts.Count; i++)
        {
            NotificationToast toast = _toasts[i];
            byte alpha = (byte)(Math.Clamp(toast.RemainingSeconds / 0.8f, 0f, 1f) * 255f);
            TextMeasureResult size = font.MeasureString(toast.Message, TextLayoutOptions.Default, 1f);
            float w = size.Width + 20f;
            float x = viewportWidth - w - 16f;
            float y = baseY + (i * 25f);
            ColorRgba textColor = toast.Message.StartsWith('+')
                ? new ColorRgba(74, 222, 128, alpha)
                : new ColorRgba(251, 191, 36, alpha);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, toast.Message, new RectangleF(x, y, w, 22f), textColor, new ColorRgba(10, 12, 18, alpha), 1f);
        }
    }

    private readonly record struct NotificationToast(string Message, float RemainingSeconds);
}
