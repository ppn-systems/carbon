// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Enhancement;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// How forge enhancement outcomes look: the outcome colours, the stacked chance bar, chance labels and the outcome
/// caption. Extracted from <see cref="ForgeAnvilModalWidget"/> (audit round 2, SRP); output is unchanged.
/// </summary>
internal static class ForgeOutcomeStyle
{
    internal static readonly ColorRgba Success = new(16, 185, 129, 240);
    internal static readonly ColorRgba NoChange = new(148, 163, 184, 240);
    internal static readonly ColorRgba Downgrade = new(249, 115, 22, 240);
    internal static readonly ColorRgba Destroy = new(225, 29, 72, 240);

    /// <summary>Draws the four outcome chances of <paramref name="step"/> as one stacked bar (success, no change, downgrade, destroy).</summary>
    internal static void DrawOutcomeBar(GeometryBatch batch, RectangleF bar, EnhancementStep step)
    {
        batch.FillRectangle(bar, new ColorRgba(12, 18, 34, 240));
        float x = bar.X;
        x = FillSegment(batch, bar, x, step.SuccessPermille, Success);
        x = FillSegment(batch, bar, x, step.NoChangePermille, NoChange);
        x = FillSegment(batch, bar, x, step.DowngradePermille, Downgrade);
        _ = FillSegment(batch, bar, x, step.DestroyPermille, Destroy);
        batch.DrawRectangle(bar, new ColorRgba(100, 116, 139, 220), 1f);
    }

    internal static float FillSegment(GeometryBatch batch, RectangleF bar, float x, int permille, ColorRgba color)
    {
        float w = bar.Width * permille / EnhancementTable.WeightTotal;
        if (w > 0f)
        {
            batch.FillRectangle(new RectangleF(x, bar.Y, w, bar.Height), color);
        }

        return x + w;
    }

    internal static ColorRgba OutcomeTextColor(EnhancementOutcome outcome) => outcome switch
    {
        EnhancementOutcome.Success => Success,
        EnhancementOutcome.Downgrade => Downgrade,
        EnhancementOutcome.Destroyed => Destroy,
        EnhancementOutcome.NoChange or EnhancementOutcome.None => CarbonUiPalette.TextMuted,
        _ => CarbonUiPalette.TextMuted,
    };

    internal static void DrawChance(SpriteBatch spriteBatch, BitmapFont font, string label, int permille, Vector2 pos, ColorRgba color) =>
        spriteBatch.DrawOutlinedString(
            font,
            $"{label} {permille / 10}.{permille % 10}%",
            pos,
            permille > 0 ? color : CarbonUiPalette.TextMuted,
            CarbonUiPalette.TextOutline,
            1,
            1f);

    internal static string DescribeOutcome(EnhancementOutcome outcome, byte newLevel) => outcome switch
    {
        EnhancementOutcome.Success => GameText.Format("forge.outcome.success", new TextArg("level", newLevel)),
        EnhancementOutcome.NoChange => GameText.Get("forge.outcome.no_change"),
        EnhancementOutcome.Downgrade => GameText.Format("forge.outcome.downgrade", new TextArg("level", newLevel)),
        EnhancementOutcome.Destroyed => GameText.Get("forge.outcome.destroyed"),
        EnhancementOutcome.None => string.Empty,
        _ => string.Empty,
    };
}
