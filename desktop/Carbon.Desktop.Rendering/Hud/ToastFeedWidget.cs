// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
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

    public void DrawGeometry(GeometryBatch batch, BitmapFont font, int viewportWidth, int viewportHeight)
    {
        for (int i = 0; i < _toasts.Count; i++)
        {
            NotificationToast toast = _toasts[i];
            byte alpha = (byte)(Math.Clamp(toast.RemainingSeconds / 0.8f, 0f, 1f) * 215f);
            TextMeasureResult size = font.MeasureString(toast.Message, TextLayoutOptions.Default, 1.5f);
            float w = size.Width + 16f;
            float x = viewportWidth - w - 16f;
            float y = viewportHeight - 104f - (i * 24f);
            batch.FillRectangle(new RectangleF(x, y, w, 20f), new ColorRgba(10, 15, 28, alpha));
            batch.DrawRectangle(new RectangleF(x, y, w, 20f), new ColorRgba(71, 85, 105, alpha), 1f);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, int viewportHeight)
    {
        for (int i = 0; i < _toasts.Count; i++)
        {
            NotificationToast toast = _toasts[i];
            byte alpha = (byte)(Math.Clamp(toast.RemainingSeconds / 0.8f, 0f, 1f) * 255f);
            TextMeasureResult size = font.MeasureString(toast.Message, TextLayoutOptions.Default, 1.5f);
            float x = MathF.Floor(viewportWidth - size.Width - 24f);
            float y = MathF.Floor(viewportHeight - 100f - (i * 24f));
            ColorRgba textColor = toast.Message.StartsWith('+')
                ? new ColorRgba(74, 222, 128, alpha)
                : new ColorRgba(251, 191, 36, alpha);
            spriteBatch.DrawOutlinedString(font, toast.Message, new Vector2(x, y), textColor, new ColorRgba(10, 12, 18, alpha), 1, 1.5f);
        }
    }

    private readonly record struct NotificationToast(string Message, float RemainingSeconds);
}
