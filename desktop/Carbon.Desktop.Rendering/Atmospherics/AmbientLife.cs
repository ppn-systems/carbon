// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>
/// Ambient life over the world (docs/art/graphics-quality.md, ambient life): small flocks of birds crossing the sky by
/// day, their shadows sliding over the ground below them, and pollen motes drifting in the sun. Everything is a pure
/// function of time and the view, so nothing is stored or allocated and a frozen frame is reproducible. Cosmetic only.
/// </summary>
internal static class AmbientLife
{
    /// <summary>Flock slots; each crosses the view on its own long cycle and only some cycles carry a flock.</summary>
    public const int FlockSlots = 3;

    /// <summary>Birds per flock.</summary>
    public const int BirdsPerFlock = 5;

    private const float FlockCycleSeconds = 26f;
    private const float CrossSeconds = 16f;
    private const int Motes = 22;

    /// <summary>A bird at a moment.</summary>
    /// <param name="Position">Where the bird is drawn (in the sky).</param>
    /// <param name="Shadow">Where its shadow falls on the ground.</param>
    /// <param name="Heading">Unit flight direction.</param>
    /// <param name="Flap">Wing beat, −1 (down) to 1 (up).</param>
    public readonly record struct Bird(Vector2 Position, Vector2 Shadow, Vector2 Heading, float Flap);

    /// <summary>Gets how much birdlife the hour and weather allow: 0 at night and in heavy rain, 1 on a clear day.</summary>
    /// <param name="night">Darkness, 0 (noon) to 1 (midnight).</param>
    /// <param name="rain">Rain intensity, 0..1.</param>
    /// <returns>The activity, 0..1.</returns>
    public static float DayActivity(float night, float rain) =>
        Math.Clamp(1f - (night * 1.6f), 0f, 1f) * Math.Clamp(1f - (rain * 1.5f), 0f, 1f);

    /// <summary>
    /// Gets bird <paramref name="index"/> of flock <paramref name="slot"/> at <paramref name="time"/>, or
    /// <see langword="false"/> when that slot carries no flock across the view right now.
    /// </summary>
    /// <param name="slot">The flock slot.</param>
    /// <param name="index">The bird within the flock.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="viewMin">View top-left in world pixels.</param>
    /// <param name="viewMax">View bottom-right in world pixels.</param>
    /// <param name="bird">The bird.</param>
    /// <returns><see langword="true"/> if the bird is in flight.</returns>
    public static bool TryGetBird(int slot, int index, float time, Vector2 viewMin, Vector2 viewMax, out Bird bird)
    {
        bird = default;
        float shifted = time + (slot * (FlockCycleSeconds / FlockSlots)) + (AmbientAnimator.Hash01(201u, slot) * 5f);
        int cycle = (int)MathF.Floor(shifted / FlockCycleSeconds);
        float age = shifted - (cycle * FlockCycleSeconds);
        uint seed = (uint)((cycle * 7919) + (slot * 104729));
        if (age > CrossSeconds || AmbientAnimator.Hash01(seed, 1) > 0.55f)
        {
            return false;
        }

        // Each flock crosses the view along its own heading, entering on one side and leaving on the other.
        float angle = AmbientAnimator.Hash01(seed, 2) * MathF.Tau;
        Vector2 heading = new(MathF.Cos(angle), MathF.Sin(angle) * 0.6f);
        heading = Vector2.Normalize(heading);
        Vector2 centre = (viewMin + viewMax) * 0.5f;
        float span = Vector2.Distance(viewMin, viewMax) * 0.65f;
        Vector2 lead = centre - (heading * span) + (new Vector2(-heading.Y, heading.X) * ((AmbientAnimator.Hash01(seed, 3) - 0.5f) * span * 0.6f));
        lead += heading * (age / CrossSeconds) * span * 2f;

        // A loose V behind the leader.
        Vector2 side = new(-heading.Y, heading.X);
        int row = (index + 1) / 2;
        float wing = index == 0 ? 0f : ((index & 1) == 0 ? 1f : -1f);
        Vector2 pos = lead - (heading * (row * 13f)) + (side * (wing * row * 11f));
        pos += side * (MathF.Sin((time * 1.3f) + index) * 2.5f);

        // Shadows fall well below the bird (it flies high).
        Vector2 shadow = pos + new Vector2(18f, 46f);
        float flap = MathF.Sin((time * 11f) + (index * 1.9f));
        bird = new Bird(pos, shadow, heading, flap);
        return true;
    }

    /// <summary>Draws the birds' ground shadows (call with the ground, before props stand on them).</summary>
    /// <param name="batch">The world batch.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="viewMin">View top-left.</param>
    /// <param name="viewMax">View bottom-right.</param>
    /// <param name="activity">Day activity, 0..1 (<see cref="DayActivity"/>).</param>
    public static void DrawBirdShadows(GeometryBatch batch, float time, Vector2 viewMin, Vector2 viewMax, float activity)
    {
        if (activity <= 0.02f)
        {
            return;
        }

        ColorRgba core = new(20, 24, 40, (byte)(48f * activity));
        ColorRgba rim = new(20, 24, 40, 0);
        for (int slot = 0; slot < FlockSlots; slot++)
        {
            for (int i = 0; i < BirdsPerFlock; i++)
            {
                if (TryGetBird(slot, i, time, viewMin, viewMax, out Bird bird))
                {
                    batch.FillRadialGradient(bird.Shadow, 5.5f, core, rim, 10, 0.5f);
                }
            }
        }
    }

    /// <summary>Draws the birds in the sky and the drifting pollen motes (call with the weather overlay).</summary>
    /// <param name="batch">The world batch.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="viewMin">View top-left.</param>
    /// <param name="viewMax">View bottom-right.</param>
    /// <param name="activity">Day activity, 0..1.</param>
    public static void DrawSky(GeometryBatch batch, float time, Vector2 viewMin, Vector2 viewMax, float activity)
    {
        if (activity <= 0.02f)
        {
            return;
        }

        ColorRgba body = new(46, 42, 52, (byte)(230f * activity));
        for (int slot = 0; slot < FlockSlots; slot++)
        {
            for (int i = 0; i < BirdsPerFlock; i++)
            {
                if (!TryGetBird(slot, i, time, viewMin, viewMax, out Bird bird))
                {
                    continue;
                }

                // A gull-wing chevron: the wing tips rise and fall with the beat.
                Vector2 back = -bird.Heading;
                Vector2 side = new(-bird.Heading.Y, bird.Heading.X);
                Vector2 tipLift = new(0f, -bird.Flap * 2.6f);
                Vector2 left = bird.Position + (back * 3.5f) + (side * 5.5f) + tipLift;
                Vector2 right = bird.Position + (back * 3.5f) - (side * 5.5f) + tipLift;
                batch.DrawLine(left, bird.Position, body, 1.6f);
                batch.DrawLine(bird.Position, right, body, 1.6f);
            }
        }

        // Pollen and dust motes catching the sun, drifting east on the breeze.
        float width = viewMax.X - viewMin.X;
        float height = viewMax.Y - viewMin.Y;
        for (int i = 0; i < Motes; i++)
        {
            float speed = 0.012f + (AmbientAnimator.Hash01(211u, i) * 0.01f);
            float x = viewMin.X + (AmbientAnimator.Frac(AmbientAnimator.Hash01(212u, i) + (time * speed)) * width);
            float y = viewMin.Y + (AmbientAnimator.Frac(AmbientAnimator.Hash01(213u, i) + (MathF.Sin((time * 0.4f) + i) * 0.01f)) * height);
            float twinkle = 0.5f + (0.5f * MathF.Sin((time * 1.9f) + (i * 2.3f)));
            batch.FillCircle(new Vector2(x, y), 1.2f, new ColorRgba(255, 248, 214, (byte)(90f * twinkle * activity)), 5);
        }
    }
}
