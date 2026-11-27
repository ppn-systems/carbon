// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Desktop.Menu;

/// <summary>
/// Time and camera path of the menu backdrop (docs/art/menu-ui.md rule 15). The camera drifts on a slow closed loop
/// (an ellipse traced once every <see cref="LoopSeconds"/>, a few world pixels per second) around a centre, so after one
/// loop every chunk it can see has been seen and nothing new is generated. Time only moves while the clock runs: the
/// active menu scene advances it, so a paused or hidden scene leaves the view still. Pure arithmetic, no allocation.
/// </summary>
internal sealed class MenuBackdropClock
{
    /// <summary>Seconds for one full loop of the camera path.</summary>
    public const float LoopSeconds = 360f;

    /// <summary>Horizontal radius of the loop in world pixels (about 0.75 of a 1024 px chunk).</summary>
    public const float RadiusX = 760f;

    /// <summary>Vertical radius of the loop in world pixels.</summary>
    public const float RadiusY = 420f;

    /// <summary>Seconds of a full day/night tint cycle.</summary>
    public const float DaySeconds = 180f;

    /// <summary>Starts the loop a little way round so the first frame is not on an axis.</summary>
    private const float StartPhase = 0.15f;

    public MenuBackdropClock(Vector2 centre, bool animate)
    {
        this.Centre = centre;
        this.Animates = animate;
    }

    /// <summary>Gets or sets the world point the camera loops around (set once the world is generated).</summary>
    public Vector2 Centre { get; set; }

    /// <summary>Gets whether motion is on (<c>CARBON_MENU_MOTION=0</c> turns it off: a still frame).</summary>
    public bool Animates { get; }

    /// <summary>Gets whether the clock is paused (no menu scene is active).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Gets the backdrop's own elapsed seconds (frozen while paused or with motion off).</summary>
    public float Time { get; private set; }

    /// <summary>Gets the camera centre for the current time.</summary>
    public Vector2 Camera => this.CameraAt(this.Time);

    /// <summary>Gets the day/night phase, 0 (dusk) to 1 (deep night) and back, for the tint.</summary>
    public float NightAmount => 0.5f - (0.5f * MathF.Cos(this.Time / DaySeconds * MathF.Tau));

    /// <summary>Advances the clock by <paramref name="dt"/> seconds unless paused or still.</summary>
    public void Advance(float dt)
    {
        if (this.IsPaused || !this.Animates || !(dt > 0f))
        {
            return;
        }

        // Clamp a long hitch (a scene load) so the view never jumps.
        this.Time += MathF.Min(dt, 0.1f);
    }

    /// <summary>Stops time (the menu scene left).</summary>
    public void Pause() => this.IsPaused = true;

    /// <summary>Starts time again (a menu scene entered).</summary>
    public void Resume() => this.IsPaused = false;

    /// <summary>Returns the camera centre at <paramref name="time"/> seconds along the loop.</summary>
    public Vector2 CameraAt(float time)
    {
        float angle = ((time / LoopSeconds) + StartPhase) * MathF.Tau;
        return this.Centre + new Vector2(MathF.Cos(angle) * RadiusX, MathF.Sin(angle) * RadiusY);
    }

    /// <summary>
    /// Returns the inclusive chunk range any view of <paramref name="viewWidth"/> × <paramref name="viewHeight"/> world
    /// pixels along the loop can touch, for <paramref name="chunkWorldSize"/>-pixel chunks.
    /// </summary>
    public (int MinX, int MinY, int MaxX, int MaxY) ChunkRange(float viewWidth, float viewHeight, float chunkWorldSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkWorldSize);
        float halfW = (viewWidth * 0.5f) + RadiusX;
        float halfH = (viewHeight * 0.5f) + RadiusY;
        return (
            (int)MathF.Floor((this.Centre.X - halfW) / chunkWorldSize),
            (int)MathF.Floor((this.Centre.Y - halfH) / chunkWorldSize),
            (int)MathF.Floor((this.Centre.X + halfW) / chunkWorldSize),
            (int)MathF.Floor((this.Centre.Y + halfH) / chunkWorldSize));
    }
}
