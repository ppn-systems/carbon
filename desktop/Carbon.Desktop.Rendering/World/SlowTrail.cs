// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// Frosty after-images behind the local player while slowed (ADR-029 D10). Keeps the last few foot positions in a fixed
/// ring, sampled at a steady interval, and forgets them the moment the slow ends. No allocation per frame.
/// </summary>
internal sealed class SlowTrail
{
    /// <summary>Number of after-images kept.</summary>
    public const int Capacity = 6;

    private const float SampleSeconds = 0.07f;

    private readonly Vector2[] _points = new Vector2[Capacity];
    private int _head;
    private float _sinceSample;

    /// <summary>Gets the number of after-images to draw.</summary>
    public int Count { get; private set; }

    /// <summary>Samples <paramref name="footPos"/> while <paramref name="slowed"/>; clears the trail otherwise.</summary>
    public void Update(Vector2 footPos, float dt, bool slowed)
    {
        if (!slowed)
        {
            this.Count = 0;
            _sinceSample = 0f;
            return;
        }

        _sinceSample += MathF.Max(0f, dt);
        if (this.Count > 0 && _sinceSample < SampleSeconds)
        {
            return;
        }

        _sinceSample = 0f;
        _head = (_head + 1) % Capacity;
        _points[_head] = footPos;
        this.Count = Math.Min(Capacity, this.Count + 1);
    }

    /// <summary>Gets after-image <paramref name="age"/> (0 = newest).</summary>
    public Vector2 Get(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(age, this.Count);
        return _points[(((_head - age) % Capacity) + Capacity) % Capacity];
    }

    /// <summary>Draws the after-images, oldest faintest.</summary>
    public void Draw(GeometryBatch batch, float radius)
    {
        ArgumentNullException.ThrowIfNull(batch);
        for (int age = this.Count - 1; age >= 1; age--)
        {
            byte alpha = (byte)(110 * (1f - (age / (float)Capacity)));
            batch.FillCircle(this.Get(age), radius * (1f - (age * 0.08f)), new ColorRgba(160, 210, 250, alpha), 12);
        }
    }
}
