// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Primitives.Hashing;
using Carbon.Primitives.Math;

namespace Carbon.Maps.Generators;

/// <summary>
/// Provides a deterministic, organically warped MMORPG progression distance field centered around the world origin,
/// driving starter-zone safety blending, progression tiers (<c>0..3</c>), and resource/POI scaling.
/// </summary>
public static class WorldProgression
{
    /// <summary>
    /// Computes an organically warped Euclidean distance (in tile units) from the world origin.
    /// Safe across large world coordinates without integer multiplication overflow.
    /// </summary>
    /// <param name="seed">Deterministic world seed.</param>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Warped non-negative radial distance in tiles.</returns>
    public static float GetWarpedDistance(ulong seed, int worldX, int worldY)
    {
        float fx = worldX;
        float fy = worldY;
        float dist = MathF.Sqrt((fx * fx) + (fy * fy));
        float phase = (SpatialHash.Hash32(seed, 0, 0, 0x5047UL) & 0xFFFF) * (6.2831853f / 65535f);
        float wave = DeterministicMath.Sin((fx * 0.0017f) + (fy * 0.0011f) + phase);
        return dist * (1f + (0.22f * wave));
    }

    /// <summary>
    /// Returns a smoothstep weight in <c>[0, 1]</c> indicating membership in the fertile Starter Heartland near origin
    /// (<c>1.0</c> inside <c>~160</c> warped tiles, tapering to <c>0.0</c> by <c>460</c> tiles).
    /// </summary>
    /// <param name="seed">Deterministic world seed.</param>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Starter heartland weight in <c>[0, 1]</c>.</returns>
    public static float GetStarterWeight(ulong seed, int worldX, int worldY)
    {
        float d = GetWarpedDistance(seed, worldX, worldY);
        float t = 1f - Math.Clamp((d - 160f) / 300f, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    /// Resolves the MMORPG zone progression tier (<c>0</c>: Heartland, <c>1</c>: Frontier,
    /// <c>2</c>: Outer Wilds, <c>3</c>: Endgame).
    /// </summary>
    /// <param name="seed">Deterministic world seed.</param>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Zone progression tier <c>0..3</c>.</returns>
    public static byte GetZoneTier(ulong seed, int worldX, int worldY)
    {
        float d = GetWarpedDistance(seed, worldX, worldY);
        if (d < 550f)
        {
            return 0;
        }

        if (d < 1450f)
        {
            return 1;
        }

        return d < 2600f ? (byte)2 : (byte)3;
    }

    /// <summary>Default starting hour of day (10:00 AM).</summary>
    public const float DefaultStartHour = 10f;

    /// <summary>Real seconds per full 24-hour in-game day cycle.</summary>
    public const float DefaultDayLengthSeconds = 240f;

    /// <summary>
    /// Computes deterministic synchronized in-game hour (<c>0..24</c>) from authoritative server tick.
    /// </summary>
    public static float ComputeTimeOfDayHours(
        ulong serverTick,
        ushort tickRate,
        float startHour = DefaultStartHour,
        float dayLengthSeconds = DefaultDayLengthSeconds)
    {
        if (tickRate == 0 || dayLengthSeconds <= 0f)
        {
            return startHour;
        }

        double elapsedSeconds = serverTick / (double)tickRate;
        double hours = (startHour + (elapsedSeconds * 24.0 / dayLengthSeconds)) % 24.0;
        return hours < 0.0 ? (float)(hours + 24.0) : (float)hours;
    }

    /// <summary>Returns <c>true</c> during nocturnal empowerment hours (<c>19:00..05:00</c>).</summary>
    public static bool IsNightHour(float hour)
    {
        float h = ((hour % 24f) + 24f) % 24f;
        return h >= 19.0f || h < 5.0f;
    }
}
