// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Fixed layout of the forge window (<see cref="ForgeAnvilModalWidget"/>): panel size, gear-list and material rows,
/// and the rectangles shared by drawing and hit testing. Extracted from the widget (audit round 2, SRP); values are
/// unchanged.
/// </summary>
internal static class ForgeAnvilLayout
{
    internal const float PanelW = 684f;
    internal const float PanelH = 480f;
    internal const int MaxVisibleGearRows = 7;
    internal const float RowH = 48f;
    internal const int MaxMaterialRows = 3;
    internal const float MaterialRowY = 216f;
    internal const float MaterialRowH = 22f;
    internal const float OutcomeBarY = 302f;

    internal static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    internal static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    internal static RectangleF GetScrollUpRect(float ox, float oy) => new(ox + 232f, oy + 50f, 18f, 18f);

    internal static RectangleF GetScrollDownRect(float ox, float oy) => new(ox + 254f, oy + 50f, 18f, 18f);

    internal static RectangleF GetStageRect(float ox, float oy) => new(ox + 306f, oy + 74f, 120f, 112f);

    /// <summary>Protection picker button <paramref name="index"/> (<c>-1</c> = no scroll) of <paramref name="count"/> scrolls.</summary>
    internal static RectangleF GetProtectionRect(float ox, float oy, int index, int count)
    {
        float w = (336f - (count * 6f)) / (count + 1);
        return new RectangleF(ox + 310f + ((index + 1) * (w + 6f)), oy + 362f, w, 26f);
    }

    internal static RectangleF GetForgeButtonRect(float ox, float oy) => new(ox + 310f, oy + 404f, 336f, 40f);
}
