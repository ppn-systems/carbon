// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Fixed layout of the repair window (<see cref="RepairModalWidget"/>), shared by drawing and hit testing.</summary>
internal static class RepairLayout
{
    internal const float PanelW = 640f;
    internal const float PanelH = 430f;
    internal const int MaxRows = 7;
    internal const float RowH = 44f;
    internal const float InfoX = 400f;
    internal const float RequirementX = 274f;
    internal const float RequirementY = 196f;
    internal const float RequirementH = 24f;
    internal const float ButtonW = 166f;

    internal static (float Ox, float Oy) Origin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    internal static RectangleF CloseRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    internal static RectangleF ListCard(float ox, float oy) => new(ox + 16f, oy + 44f, 236f, 370f);

    internal static RectangleF DetailCard(float ox, float oy) => new(ox + 262f, oy + 44f, 362f, 370f);

    internal static RectangleF ScrollUpRect(float ox, float oy) => new(ox + 206f, oy + 50f, 18f, 18f);

    internal static RectangleF ScrollDownRect(float ox, float oy) => new(ox + 228f, oy + 50f, 18f, 18f);

    internal static RectangleF ItemRow(float ox, float oy, int index) => new(ox + 24f, oy + 76f + (index * (RowH + 4f)), 220f, RowH);

    internal static RectangleF StageRect(float ox, float oy) => new(ox + 274f, oy + 56f, 112f, 104f);

    internal static RectangleF DurabilityBar(float ox, float oy) => new(ox + InfoX, oy + 110f, 212f, 12f);

    internal static RectangleF FullButtonRect(float ox, float oy) => new(ox + 274f, oy + 364f, ButtonW, 36f);

    internal static RectangleF PartialButtonRect(float ox, float oy) => new(ox + 446f, oy + 364f, ButtonW, 36f);
}
