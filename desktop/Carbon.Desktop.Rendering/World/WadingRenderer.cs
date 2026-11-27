// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Biomes;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Draws actors (player, NPCs, monsters) half-submerged while they wade through shallow water: the rig sinks
/// by up to <see cref="MaxSinkFraction"/> of its height, the part below the surface is veiled with the local
/// water colour, and an animated waterline ring, spreading ripples and a V wake (when moving) sit on the
/// surface. Everything is drawn in the world pass, so the night ambient darkens it like the water itself.
/// Allocation-free: polygons are built on the stack.
/// </summary>
internal static class WadingRenderer
{
    /// <summary>Fraction of the body height an actor sinks at full wading depth.</summary>
    public const float MaxSinkFraction = 0.36f;

    private const int RingSegments = 16;

    // Depth under which an actor is treated as dry (the very edge of the waterline).
    private const float MinDepth = 0.02f;

    private static readonly Vector3 s_shoreShelf = new(150f, 206f, 204f);

    /// <summary>Fraction of the body height a swimmer sinks: head and shoulders stay above the surface (ADR-032).</summary>
    public const float SwimSinkFraction = 0.62f;

    /// <summary>Fraction of the body height a diver is drawn below the surface (ADR-032).</summary>
    public const float DiveSinkFraction = 0.55f;

    /// <summary>The per-frame wading state of one actor.</summary>
    /// <param name="Depth">Normalised wading depth (0..1); 1 when swimming or diving.</param>
    /// <param name="Sink">How far the rig is drawn below its foot position, in world units.</param>
    /// <param name="HalfWidth">Half width of the body at the waterline, in world units.</param>
    /// <param name="State">Walking or wading, swimming on the surface, or diving under it (ADR-032).</param>
    /// <param name="BodyHeight">The drawn body height, in world units.</param>
    public readonly record struct Wade(float Depth, float Sink, float HalfWidth, SwimState State = SwimState.Wading, float BodyHeight = 35f)
    {
        /// <summary>Gets a value indicating whether the actor is in the water at all.</summary>
        public bool IsWading => this.Depth > MinDepth;
    }

    /// <summary>Resolves the wading state of an actor standing at <paramref name="position"/>.</summary>
    /// <param name="world">The streaming world.</param>
    /// <param name="position">The actor's centre position (world units).</param>
    /// <param name="bodyHeight">The drawn body height (world units).</param>
    /// <param name="radius">The actor's collision radius.</param>
    /// <returns>The wading state (not wading on dry land, bridges and piers).</returns>
    public static Wade Resolve(ChunkWorld2D world, Vector2 position, float bodyHeight, float radius)
    {
        ArgumentNullException.ThrowIfNull(world);
        float halfWidth = MathF.Max(radius * 1.45f, bodyHeight * 0.27f);
        SwimState state = world.GetSwimStateAt(position);
        if (state is SwimState.Swimming or SwimState.Diving)
        {
            float sink = (state == SwimState.Swimming ? SwimSinkFraction : DiveSinkFraction) * bodyHeight;
            return new Wade(1f, sink, halfWidth, state, bodyHeight);
        }

        float depth = world.GetWaterDepth(position);
        return new Wade(depth, depth * MaxSinkFraction * bodyHeight, halfWidth, depth > MinDepth ? SwimState.Wading : SwimState.Walking, bodyHeight);
    }

    /// <summary>Draws the far half of the waterline ring (call before the rig).</summary>
    /// <param name="batch">The world geometry batch.</param>
    /// <param name="foot">The actor's foot position on the water surface.</param>
    /// <param name="wade">The wading state.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="seed">Per-actor phase seed.</param>
    public static void DrawBehind(GeometryBatch batch, Vector2 foot, in Wade wade, float time, uint seed)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!wade.IsWading || wade.State == SwimState.Diving)
        {
            return;
        }

        float pulse = 1f + (0.05f * MathF.Sin((time * 3.1f) + (seed * 0.37f)));
        float rx = wade.HalfWidth * 1.15f * pulse;
        DrawEllipseArc(batch, foot, rx, rx * 0.36f, MathF.PI, MathF.Tau, new ColorRgba(236, 250, 255, (byte)(55 + (50 * wade.Depth))), 1.0f);
    }

    /// <summary>
    /// Draws the surface over the submerged body (call right after the rig): the water veil, the near half of
    /// the waterline ring, two spreading ripples and, when moving, a V wake trailing behind.
    /// </summary>
    /// <param name="batch">The world geometry batch.</param>
    /// <param name="art">Vector art (for the water colour), or <c>null</c>.</param>
    /// <param name="tileSize">World tile size.</param>
    /// <param name="foot">The actor's foot position on the water surface.</param>
    /// <param name="wade">The wading state.</param>
    /// <param name="velocity">The actor's velocity (world units per second).</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="seed">Per-actor phase seed.</param>
    public static void DrawFront(
        GeometryBatch batch,
        IVectorArtRepository? art,
        float tileSize,
        Vector2 foot,
        in Wade wade,
        Vector2 velocity,
        float time,
        uint seed)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!wade.IsWading)
        {
            return;
        }

        float hw = wade.HalfWidth;
        float phase = seed * 0.37f;
        if (wade.State == SwimState.Diving)
        {
            DrawDiver(batch, art, tileSize, foot, in wade, time, seed);
            return;
        }

        // Water veil over everything below the surface: a band from the waterline down past the sunk feet with
        // a flattened round bottom, in the colour of the water here. Not fully opaque: the legs show faintly.
        Vector3 water = WaterColor(art, foot, tileSize, wade.Depth);
        float drop = wade.Sink + 5f;
        Span<Vector2> veil = stackalloc Vector2[12];
        veil[0] = new Vector2(foot.X - hw, foot.Y);
        veil[1] = new Vector2(foot.X + hw, foot.Y);
        veil[2] = new Vector2(foot.X + hw, foot.Y + (drop * 0.55f));
        for (int i = 1; i <= 8; i++)
        {
            float a = i / 9f * MathF.PI;
            veil[i + 2] = new Vector2(foot.X + (MathF.Cos(a) * hw), foot.Y + (drop * 0.55f) + (MathF.Sin(a) * drop * 0.45f));
        }

        veil[11] = new Vector2(foot.X - hw, foot.Y + (drop * 0.55f));
        batch.FillConvexPolygon(veil, TerrainSurface.ToColor(water, (byte)(170 + (75 * wade.Depth))));

        // Near half of the waterline ring, gently breathing, plus a soft highlight hugging the body.
        float pulse = 1f + (0.05f * MathF.Sin((time * 3.1f) + phase));
        float rx = hw * 1.12f * pulse;
        DrawEllipseArc(batch, foot, rx, rx * 0.34f, 0f, MathF.PI, new ColorRgba(236, 250, 255, (byte)(80 + (70 * wade.Depth))), 1.1f);
        DrawEllipseArc(batch, foot + new Vector2(0f, 0.6f), hw * 0.78f, hw * 0.2f, 0.25f, MathF.PI - 0.25f, new ColorRgba(210, 240, 252, 90), 0.9f);

        // Two ripples spreading out and fading, half a cycle apart.
        for (int k = 0; k < 2; k++)
        {
            float t = Frac((time * 0.55f) + (k * 0.5f) + (seed * 0.013f));
            float rr = hw * (1.25f + (t * 1.6f));
            byte alpha = (byte)(75 * (1f - t) * (0.4f + (0.6f * wade.Depth)));
            DrawEllipseArc(batch, foot, rr, rr * 0.34f, 0f, MathF.Tau, new ColorRgba(255, 255, 255, alpha), 0.9f);
        }

        // V wake: two short foam arcs opening behind the actor, a little longer the faster it goes.
        float speed = velocity.Length();
        if (speed > 6f)
        {
            Vector2 dir = velocity / speed;
            Vector2 back = new(-dir.X, -dir.Y * 0.5f);
            Vector2 side = new(-dir.Y, dir.X * 0.5f);
            float len = hw * (1.0f + MathF.Min(speed / 80f, 0.8f));
            byte foam = (byte)(130 * MathF.Min(speed / 40f, 1f));
            Vector2 stern = foot + (back * (hw * 0.6f));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 prev = stern + (side * (hw * 0.45f * s));
                for (int seg = 1; seg <= 4; seg++)
                {
                    float t = seg / 4f;
                    float wobble = MathF.Sin((time * 8f) + (seg * 1.3f) + phase) * 0.6f;
                    Vector2 next = stern + (back * (len * t)) + (side * s * ((hw * 0.45f) + (len * 0.75f * t) + wobble));
                    batch.DrawLine(prev, next, new ColorRgba(255, 255, 255, (byte)(foam * (1f - (t * 0.7f)))), 1.3f - (t * 0.5f));
                    prev = next;
                }
            }
        }
    }

    // A diver is under the surface from head to foot: one translucent veil of deep water over the whole body, and a
    // trail of bubbles rising to the surface above it.
    private static void DrawDiver(GeometryBatch batch, IVectorArtRepository? art, float tileSize, Vector2 foot, in Wade wade, float time, uint seed)
    {
        float hw = wade.HalfWidth * 1.15f;
        float top = foot.Y + wade.Sink - wade.BodyHeight - 4f;
        float bottom = foot.Y + wade.Sink + 4f;
        Vector3 water = WaterColor(art, foot, tileSize, 1f) * 0.62f;
        // A capsule hugging the body: round at the head and the feet, so it reads as water, not a box.
        Span<Vector2> veil = stackalloc Vector2[16];
        float cap = MathF.Min(hw, (bottom - top) * 0.5f);
        for (int i = 0; i < 8; i++)
        {
            float a = MathF.PI + (i / 7f * MathF.PI);
            veil[i] = new Vector2(foot.X + (MathF.Cos(a) * hw), top + cap + (MathF.Sin(a) * cap));
            float b = i / 7f * MathF.PI;
            veil[8 + i] = new Vector2(foot.X + (MathF.Cos(b) * hw), bottom - cap + (MathF.Sin(b) * cap));
        }

        batch.FillConvexPolygon(veil, TerrainSurface.ToColor(water, 135));

        for (int i = 0; i < 4; i++)
        {
            float t = Frac((time * 0.6f) + (i * 0.25f) + (seed * 0.017f));
            Vector2 p = new(foot.X + (MathF.Sin((time * 3f) + i + seed) * 3f), top - (t * wade.BodyHeight * 0.6f));
            batch.DrawCircle(p, 1.2f + (i % 2) + (t * 0.8f), new ColorRgba(225, 250, 255, (byte)(160 * (1f - t))), 8, 0.9f);
        }
    }

    // Water colour under an actor: the shallow-water ground field, lifted towards the sandy shelf where the
    // water is shallow (the same shading the terrain bake gives the shore).
    private static Vector3 WaterColor(IVectorArtRepository? art, Vector2 foot, float tileSize, float depth)
    {
        BiomeSurfaceStyle style = art is not null && art.TryGetBiome((byte)BiomeType.ShallowWater, out CompiledBiomeArt? water)
            ? water.Surface
            : s_fallbackWater;
        TerrainSurface.Field field = TerrainSurface.Sample(foot.X / tileSize, foot.Y / tileSize);
        Vector3 c = TerrainSurface.Ground(style, 0, in field);
        float shelf = Math.Clamp((0.6f - depth) * 0.6f, 0f, 0.36f);
        return Vector3.Lerp(c, s_shoreShelf, shelf);
    }

    private static readonly BiomeSurfaceStyle s_fallbackWater = BiomeSurfaceStyle.FromGround(new Vector3(56f, 155f, 184f));

    private static void DrawEllipseArc(GeometryBatch batch, Vector2 c, float rx, float ry, float from, float to, ColorRgba color, float thickness)
    {
        if (color.A == 0)
        {
            return;
        }

        float step = (to - from) / RingSegments;
        Vector2 prev = c + new Vector2(MathF.Cos(from) * rx, MathF.Sin(from) * ry);
        for (int i = 1; i <= RingSegments; i++)
        {
            float a = from + (i * step);
            Vector2 next = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            batch.DrawLine(prev, next, color, thickness);
            prev = next;
        }
    }

    private static float Frac(float v) => v - MathF.Floor(v);
}
