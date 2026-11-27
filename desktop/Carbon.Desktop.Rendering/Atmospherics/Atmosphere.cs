// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>A point light: drawn additively after the ambient darkening, so lights glow at night.</summary>
/// <param name="Position">World position.</param>
/// <param name="Radius">Reach in world pixels.</param>
/// <param name="Color">Light colour.</param>
/// <param name="Intensity">0..1 brightness.</param>
internal readonly record struct PointLight(Vector2 Position, float Radius, ColorRgba Color, float Intensity);

/// <summary>
/// Time of day, weather and the passes that render them: ambient light (a multiply pass tinting the
/// whole view), point lights (an additive pass), drifting clouds and their shadows, rain and night
/// fireflies.
/// </summary>
internal sealed class Atmosphere
{
    /// <summary>Multiplies the destination by the source colour: ambient tinting.</summary>
    public static readonly BlendState Multiply = new(
        BlendingFactor.DstColor,
        BlendingFactor.Zero,
        BlendingFactor.Zero,
        BlendingFactor.One);

    // Ambient light keyframes over a 24 h day (hour, rgb multiplier). Nights are moonlit blue rather than
    // black, so the world stays readable; lights and fireflies still glow because Night saturates early.
    private static readonly (float Hour, Vector3 Light)[] s_ambient =
    [
        (0f, new Vector3(0.36f, 0.42f, 0.64f)),
        (4.5f, new Vector3(0.38f, 0.43f, 0.65f)),
        (6.0f, new Vector3(0.78f, 0.56f, 0.58f)), // dawn
        (7.5f, new Vector3(1.00f, 0.88f, 0.78f)),
        (9.0f, new Vector3(1.00f, 1.00f, 1.00f)),
        (16.5f, new Vector3(1.00f, 0.98f, 0.94f)),
        (18.3f, new Vector3(1.00f, 0.70f, 0.50f)), // sunset
        (19.6f, new Vector3(0.60f, 0.49f, 0.66f)), // dusk: violet, but with enough green left that grass keeps its hue
        (21.0f, new Vector3(0.37f, 0.42f, 0.63f)),
        (24f, new Vector3(0.36f, 0.42f, 0.64f)),
    ];


    /// <summary>Initializes a new instance of the <see cref="Atmosphere"/> class.</summary>
    /// <param name="startHour">Hour of day at start (0..24).</param>
    /// <param name="dayLengthSeconds">Real seconds per in-game day.</param>
    /// <param name="forcedRain">Force rain on (true), off (false) or follow the weather cycle (null).</param>
    public Atmosphere(float startHour, float dayLengthSeconds, bool? forcedRain)
    {
        this.Hour = ((startHour % 24f) + 24f) % 24f;
        this.DayLength = dayLengthSeconds;
        _forcedRain = forcedRain;
    }

    private readonly bool? _forcedRain;
    private readonly CloudSystem _clouds = new();
    private float _time;

    /// <summary>Gets the current hour of day (0..24).</summary>
    public float Hour { get; private set; }

    /// <summary>Gets the real seconds per in-game day.</summary>
    public float DayLength { get; }

    /// <summary>Gets how dark it is: 0 at noon, 1 at midnight.</summary>
    public float Night { get; private set; }

    /// <summary>Synchronizes the local clock hour to an authoritative server hour.</summary>
    public void SyncToHour(float hour) =>
        this.Hour = ((hour % 24f) + 24f) % 24f;

    /// <summary>Gets the rain intensity 0..1.</summary>
    public float Rain { get; private set; }

    /// <summary>Gets the ambient light multiplier for the current hour and weather.</summary>
    public Vector3 Ambient { get; private set; } = Vector3.One;

    /// <summary>Advances the clock and the weather.</summary>
    public void Update(float dt)
    {
        _time += dt;
        this.Hour = (this.Hour + (dt * 24f / this.DayLength)) % 24f;

        // Weather: a slow, smooth cycle; showers take about a quarter of the time.
        float w = (MathF.Sin(_time * 0.011f) * 0.6f) + (MathF.Sin((_time * 0.027f) + 1.3f) * 0.4f);
        float rain = Math.Clamp((w - 0.45f) / 0.25f, 0f, 1f);
        this.Rain = _forcedRain switch
        {
            true => 1f,
            false => 0f,
            _ => rain,
        };

        Vector3 light = SampleAmbient(this.Hour);
        light *= 1f - (0.28f * this.Rain);                                // overcast
        light = Vector3.Lerp(light, light * new Vector3(0.9f, 0.95f, 1.05f), this.Rain); // cooler
        this.Ambient = light;
        float luminance = (light.X * 0.3f) + (light.Y * 0.55f) + (light.Z * 0.15f);
        this.Night = Math.Clamp((0.95f - luminance) / 0.5f, 0f, 1f);
    }

    /// <summary>
    /// Updates the clouds for this frame and draws their shadows on the ground (call right after the
    /// terrain, before props).
    /// </summary>
    public void DrawCloudShadows(GeometryBatch batch, float minX, float minY, float maxX, float maxY)
    {
        _clouds.Update(_time, this.Rain, minX, minY, maxX, maxY);
        _clouds.DrawShadows(batch, this.Hour, this.Night, this.Rain);
    }

    /// <summary>The ambient pass: multiplies the whole view by the light colour.</summary>
    public void DrawAmbient(GeometryBatch batch, float minX, float minY, float maxX, float maxY)
    {
        Vector3 l = this.Ambient;
        if (l.X > 0.995f && l.Y > 0.995f && l.Z > 0.995f)
        {
            return;
        }

        ColorRgba c = new((byte)(Math.Clamp(l.X, 0f, 1f) * 255f), (byte)(Math.Clamp(l.Y, 0f, 1f) * 255f), (byte)(Math.Clamp(l.Z, 0f, 1f) * 255f), 255);
        batch.FillRectangle(new RectangleF(minX - 64f, minY - 64f, maxX - minX + 128f, maxY - minY + 128f), c);
    }

    /// <summary>
    /// The Low-preset light pass (additive over the darkened scene): two radial-gradient fans per light give a soft,
    /// peaked falloff, stronger the darker it is.
    /// </summary>
    public void DrawLights(GeometryBatch batch, List<PointLight> lights)
    {
        // Lights only matter once the day dims; at noon they vanish entirely.
        float strength = MathF.Pow(this.Night, 1.3f);
        if (strength < 0.02f)
        {
            return;
        }

        DrawGlows(batch, lights, strength, 0.25f, 0.20f);
    }

    /// <summary>
    /// The light-map pass (ADR-041): lights added into a map cleared to the ambient, which the composite multiplies
    /// over the scene. The map stores half the light, so these peaks (0.7 × intensity) light the scene up to about 1.4×.
    /// </summary>
    public void DrawLightMapLights(GeometryBatch batch, List<PointLight> lights)
    {
        float strength = MathF.Pow(this.Night, 1.1f);
        if (strength < 0.02f)
        {
            return;
        }

        DrawGlows(batch, lights, strength, 0.40f, 0.30f);
    }

    /// <summary>
    /// Adds the night fireflies near <paramref name="player"/> as small lights, so they light the ground in the light
    /// map. Same positions and pulse as the dots <see cref="DrawOverlay"/> draws.
    /// </summary>
    public void CollectFireflyLights(List<PointLight> lights, Vector2 player)
    {
        float fade = this.FireflyFade();
        if (fade <= 0f)
        {
            return;
        }

        for (int i = 0; i < FireflyCount; i++)
        {
            (Vector2 p, float glow) = this.Firefly(i, player);
            lights.Add(new PointLight(p, 34f, new ColorRgba(214, 240, 120), 0.55f * glow * fade));
        }
    }

    private static void DrawGlows(GeometryBatch batch, List<PointLight> lights, float strength, float outerShare, float coreShare)
    {
        Span<PointLight> span = CollectionsMarshal.AsSpan(lights);
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly PointLight light = ref span[i];
            float k = light.Intensity * strength;
            if (k < 0.02f)
            {
                continue;
            }

            ColorRgba rim = new(light.Color.R, light.Color.G, light.Color.B, 0);
            byte outer = (byte)Math.Clamp(255f * outerShare * k, 0f, 255f);
            byte core = (byte)Math.Clamp(255f * coreShare * k, 0f, 255f);
            batch.FillRadialGradient(light.Position, light.Radius, new ColorRgba(light.Color.R, light.Color.G, light.Color.B, outer), rim, 24);
            batch.FillRadialGradient(light.Position, light.Radius * 0.45f, new ColorRgba(light.Color.R, light.Color.G, light.Color.B, core), rim, 16);
        }
    }

    /// <summary>Weather and life drawn on top: thin clouds, rain, splashes and night fireflies.</summary>
    public void DrawOverlay(GeometryBatch batch, float minX, float minY, float maxX, float maxY, Vector2 player)
    {
        _clouds.DrawClouds(batch, this.Hour, this.Night, this.Rain, player);

        if (this.Rain > 0.02f)
        {
            this.DrawRain(batch, minX, minY, maxX, maxY);
        }

        float fade = this.FireflyFade();
        if (fade > 0f)
        {
            // Fireflies around the player at night
            for (int i = 0; i < FireflyCount; i++)
            {
                (Vector2 p, float glow) = this.Firefly(i, player);
                batch.FillCircle(p, 8f, new ColorRgba(253, 230, 138, (byte)(45 * glow * fade)), 10);
                batch.FillCircle(p, 1.8f, new ColorRgba(255, 250, 200, (byte)((120 + (130 * glow)) * fade)), 6);
            }
        }
    }

    private const int FireflyCount = 26;

    // 0 by day and in heavy rain, rising to 1 deep in the night.
    private float FireflyFade() =>
        this.Night > 0.4f && this.Rain < 0.5f ? (this.Night - 0.4f) / 0.6f * (1f - (this.Rain * 2f)) : 0f;

    private (Vector2 Position, float Glow) Firefly(int i, Vector2 player)
    {
        float t = _time * (0.18f + (AmbientAnimator.Hash01(51u, i) * 0.2f));
        Vector2 home = new(
            (MathF.Floor(player.X / 600f) * 600f) + (AmbientAnimator.Hash01(52u, i) * 1200f) - 300f,
            (MathF.Floor(player.Y / 600f) * 600f) + (AmbientAnimator.Hash01(53u, i) * 900f) - 200f);
        Vector2 p = home + new Vector2(MathF.Sin(t + i) * 60f, MathF.Cos((t * 1.4f) + (i * 0.7f)) * 40f);
        float glow = 0.5f + (0.5f * MathF.Sin((_time * 3.5f) + (i * 2.1f)));
        return (p, glow);
    }

    private void DrawRain(GeometryBatch batch, float minX, float minY, float maxX, float maxY)
    {
        float w = maxX - minX;
        float h = maxY - minY;
        int drops = (int)(260 * this.Rain);
        ColorRgba streak = new(200, 214, 236, (byte)(150 * this.Rain));
        for (int i = 0; i < drops; i++)
        {
            float speed = 0.9f + (AmbientAnimator.Hash01(61u, i) * 0.5f);
            float phase = AmbientAnimator.Frac((_time * speed * 1.6f) + AmbientAnimator.Hash01(62u, i));
            float x = minX + (AmbientAnimator.Frac(AmbientAnimator.Hash01(63u, i) + (phase * 0.12f)) * w);
            float y = minY + (phase * h);
            batch.DrawLine(new Vector2(x, y), new Vector2(x - 4f, y + 16f), streak, 1.3f);
        }

        // Splash rings on the ground
        int splashes = (int)(70 * this.Rain);
        for (int i = 0; i < splashes; i++)
        {
            float phase = AmbientAnimator.Frac((_time * 2.2f) + AmbientAnimator.Hash01(64u, i));
            int cycle = (int)((_time * 2.2f) + AmbientAnimator.Hash01(64u, i));
            float x = minX + (AmbientAnimator.Hash01(65u, i + (cycle * 97)) * w);
            float y = minY + (AmbientAnimator.Hash01(66u, i + (cycle * 89)) * h);
            float r = 2f + (phase * 7f);
            batch.DrawRectangle(new RectangleF(x - r, y - (r * 0.4f), r * 2f, r * 0.8f), new ColorRgba(220, 232, 250, (byte)(120 * (1f - phase) * this.Rain)), 1f);
        }
    }

    private static Vector3 SampleAmbient(float hour)
    {
        for (int i = 0; i + 1 < s_ambient.Length; i++)
        {
            (float h0, Vector3 l0) = s_ambient[i];
            (float h1, Vector3 l1) = s_ambient[i + 1];
            if (hour >= h0 && hour <= h1)
            {
                float t = (hour - h0) / (h1 - h0);
                t = t * t * (3f - (2f * t));
                return Vector3.Lerp(l0, l1, t);
            }
        }

        return Vector3.One;
    }
}
