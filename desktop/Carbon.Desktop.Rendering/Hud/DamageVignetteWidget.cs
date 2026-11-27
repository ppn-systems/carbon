// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Combat.Statuses.Config;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Abstractions.World;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen-edge feedback (ADR-029 D10): a red vignette that fades after the local player takes damage, a pulsing green
/// edge while poisoned, and a cold blue edge while slowed. Drawn under every HUD widget.
/// </summary>
internal sealed class DamageVignetteWidget
{
    private const int Bands = 8;
    private const float BandThickness = 9f;
    private const float HitSeconds = 0.45f;

    private static readonly ColorRgba s_hit = new(200, 24, 24, 255);
    private static readonly ColorRgba s_poison = new(96, 190, 40, 255);
    private static readonly ColorRgba s_slow = new(120, 180, 240, 255);

    /// <summary>Gets the edge tint and its peak alpha for this frame (alpha 0 = nothing to draw).</summary>
    internal static (ColorRgba Color, float Alpha) Resolve(IWorldReadModel world, float animTime)
    {
        ArgumentNullException.ThrowIfNull(world);
        float hit = Math.Clamp(world.LocalDamageVignetteTimer / HitSeconds, 0f, 1f);
        if (hit > 0f)
        {
            return (s_hit, 150f * hit);
        }

        (bool poisoned, bool slowed) = ActiveFlags(world.StatusEffectDefinitions, world.LocalStatusEffects);
        if (poisoned)
        {
            return (s_poison, 55f + (25f * MathF.Sin(animTime * 3f)));
        }

        return slowed ? (s_slow, 50f) : (default, 0f);
    }

    public void DrawGeometry(GeometryBatch batch, IWorldReadModel world, int viewportWidth, int viewportHeight, float animTime)
    {
        ArgumentNullException.ThrowIfNull(batch);
        (ColorRgba color, float alpha) = Resolve(world, animTime);
        if (alpha <= 0.5f)
        {
            return;
        }

        for (int b = 0; b < Bands; b++)
        {
            // Strongest at the edge, fading inward.
            byte a = (byte)Math.Clamp(alpha * (1f - (b / (float)Bands)) * 0.45f, 0f, 255f);
            ColorRgba band = new(color.R, color.G, color.B, a);
            float inset = b * BandThickness;
            float w = viewportWidth - (inset * 2f);
            float h = viewportHeight - (inset * 2f);
            if (w <= 0f || h <= 0f)
            {
                break;
            }

            batch.FillRectangle(new RectangleF(inset, inset, w, BandThickness), band);
            batch.FillRectangle(new RectangleF(inset, inset + h - BandThickness, w, BandThickness), band);
            batch.FillRectangle(new RectangleF(inset, inset + BandThickness, BandThickness, h - (BandThickness * 2f)), band);
            batch.FillRectangle(new RectangleF(inset + w - BandThickness, inset + BandThickness, BandThickness, h - (BandThickness * 2f)), band);
        }
    }

    private static (bool Poisoned, bool Slowed) ActiveFlags(StatusEffectCatalog catalog, IReadOnlyList<LocalStatusEffect> effects)
    {
        bool poisoned = false;
        bool slowed = false;
        for (int i = 0; i < effects.Count; i++)
        {
            if (!catalog.TryGet(effects[i].EffectId, out StatusEffectDefinition def))
            {
                continue;
            }

            poisoned |= def.Magnitude > 0f && !def.Lethal;
            slowed |= def.MoveSpeedMultiplier < 1f;
        }

        return (poisoned, slowed);
    }
}
