// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Props;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>
/// Leaves and blossom petals drifting down from visible canopies, blown downwind by <see cref="WindField"/>. Every
/// leaf is a pure function of (tree, slot, time, rain): nothing is stored, nothing allocated, and a frozen frame
/// (screenshots) is reproducible.
/// </summary>
internal static class FallingLeaves
{
    /// <summary>Leaf slots per tree; each slot falls on its own cycle, and only some cycles drop a leaf.</summary>
    public const int SlotsPerTree = 2;

    /// <summary>Seconds one leaf takes from the canopy to the ground.</summary>
    public const float FallSeconds = 4.5f;

    private const float CycleSeconds = 7f;

    /// <summary>Gets the leaf colour for a tree type, or <see langword="false"/> for trees that drop nothing visible.</summary>
    /// <param name="type">The tree type.</param>
    /// <param name="variant">A per-leaf random value, 0..1 (mixes in a few turning leaves).</param>
    /// <param name="color">The leaf colour.</param>
    /// <returns><see langword="true"/> if the tree sheds leaves or petals.</returns>
    public static bool TryGetColor(MapPropType type, float variant, out ColorRgba color)
    {
        if (type == MapPropType.CherryBlossomTree)
        {
            color = variant < 0.6f ? new ColorRgba(249, 168, 212) : new ColorRgba(252, 205, 228);
            return true;
        }

        if (type == MapPropType.BirchTree)
        {
            color = variant < 0.5f ? new ColorRgba(234, 200, 90) : new ColorRgba(190, 200, 80);
            return true;
        }

        if (type is MapPropType.OakTree or MapPropType.WillowTree or MapPropType.AcaciaTree)
        {
            color = variant < 0.75f ? new ColorRgba(104, 160, 64) : new ColorRgba(196, 150, 60);
            return true;
        }

        // Pines, palms, dead trees and cacti shed nothing worth drawing.
        color = default;
        return false;
    }

    /// <summary>
    /// Gets one leaf of a tree at <paramref name="time"/>: where it is, how it is turned and how visible it is.
    /// </summary>
    /// <param name="seed">The tree's seed (stable per tree).</param>
    /// <param name="slot">The leaf slot, 0..<see cref="SlotsPerTree"/> − 1.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="canopy">Where leaves leave the canopy (world position).</param>
    /// <param name="fallHeight">How far below the canopy the ground is, in world pixels.</param>
    /// <param name="rain">Rain intensity, 0..1 (more wind, more leaves).</param>
    /// <param name="position">The leaf's world position.</param>
    /// <param name="angle">The leaf's rotation in radians.</param>
    /// <returns>Visibility 0..1; 0 when this slot drops nothing in its current cycle.</returns>
    public static float Sample(uint seed, int slot, float time, Vector2 canopy, float fallHeight, float rain, out Vector2 position, out float angle)
    {
        float offset = AmbientAnimator.Hash01(seed, 70 + slot) * CycleSeconds;
        float cycleTime = time + offset;
        int cycle = (int)MathF.Floor(cycleTime / CycleSeconds);
        float age = cycleTime - (cycle * CycleSeconds);
        uint cycleSeed = seed ^ (uint)(cycle * 0x2C1B3C6D);
        float chance = 0.35f + (0.4f * Math.Clamp(rain, 0f, 1f));
        position = canopy;
        angle = 0f;
        if (age >= FallSeconds || AmbientAnimator.Hash01(cycleSeed, slot) > chance)
        {
            return 0f;
        }

        float t = age / FallSeconds;
        Vector2 start = canopy + new Vector2((AmbientAnimator.Hash01(cycleSeed, 10 + slot) - 0.5f) * 36f, (AmbientAnimator.Hash01(cycleSeed, 20 + slot) - 0.5f) * 14f);
        float wind = WindField.Strength(time, rain);
        float drift = t * t * 46f * wind;                                         // pushed downwind, faster as it falls clear
        float flutter = MathF.Sin((age * 3.1f) + (slot * 1.7f)) * 7f * (1f - (t * 0.5f));
        position = start + new Vector2(drift + flutter, t * fallHeight);
        angle = (age * 2.4f) + (AmbientAnimator.Hash01(cycleSeed, 30 + slot) * MathF.Tau);
        float fadeIn = Math.Clamp(t * 8f, 0f, 1f);
        float fadeOut = Math.Clamp((1f - t) * 5f, 0f, 1f);
        return fadeIn * fadeOut;
    }

    /// <summary>Draws the falling leaves of one tree (after its canopy).</summary>
    /// <param name="batch">The world geometry batch.</param>
    /// <param name="tree">The tree.</param>
    /// <param name="seed">The tree's seed.</param>
    /// <param name="canopy">Where leaves leave the canopy.</param>
    /// <param name="fallHeight">Canopy-to-ground distance in world pixels.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="rain">Rain intensity, 0..1.</param>
    public static void Draw(GeometryBatch batch, in MapProp tree, uint seed, Vector2 canopy, float fallHeight, float time, float rain)
    {
        for (int slot = 0; slot < SlotsPerTree; slot++)
        {
            float visible = Sample(seed, slot, time, canopy, fallHeight, rain, out Vector2 p, out float angle);
            if (visible <= 0.01f || !TryGetColor(tree.Type, AmbientAnimator.Hash01(seed, 90 + slot), out ColorRgba color))
            {
                continue;
            }

            // A small leaf seen tumbling: its width shrinks and grows as it turns.
            Vector2 along = new(MathF.Cos(angle) * 3.6f, MathF.Sin(angle) * 1.6f);
            Vector2 across = new(-along.Y * 0.55f, along.X * 0.55f * MathF.Abs(MathF.Cos(angle * 0.7f)));
            ColorRgba c = color with { A = (byte)(235f * visible) };
            batch.FillTriangle(p - along, p + across, p + along, c);
            batch.FillTriangle(p - along, p + along, p - across, c);
        }
    }
}
