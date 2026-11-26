// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Manages smooth player tracking, user zoom bounds, and interior building framing on top of <see cref="Camera2D"/>.
/// </summary>
public sealed class CameraRig2D
{
    private const float InteriorZoomBoost = 1.4f;
    private const float MinZoom = 0.10f;
    private const float MaxZoom = 3.0f;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraRig2D"/> class.
    /// </summary>
    /// <param name="camera">Underlying 2D orthographic camera.</param>
    /// <param name="macroView">Whether to initialize zoomed out for macro biome inspection.</param>
    public CameraRig2D(Camera2D camera, bool macroView)
    {
        this.Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        this.UserZoom = macroView ? 0.15f : 1.0f;
        this.Camera.Zoom = this.UserZoom;
    }

    /// <summary>Gets the underlying orthographic camera.</summary>
    public Camera2D Camera { get; }

    /// <summary>Gets the player-controlled base zoom level.</summary>
    public float UserZoom { get; private set; }

    /// <summary>Gets the smoothed [0..1] interior camera blend factor.</summary>
    public float InteriorBlend { get; private set; }

    /// <summary>Gets the world-space bounds of the currently tracked interior room.</summary>
    public RectangleF InteriorRoom { get; private set; }

    /// <summary>Multiplies the user zoom by <paramref name="factor"/> within allowed bounds.</summary>
    public void AdjustZoom(float factor) =>
        this.UserZoom = Math.Clamp(this.UserZoom * factor, MinZoom, MaxZoom);

    /// <summary>Sets the user base zoom level explicitly within allowed bounds.</summary>
    public void SetZoom(float zoom) =>
        this.UserZoom = Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>Immediately centers the camera on <paramref name="worldPos"/> and clears interior blend.</summary>
    public void SnapTo(Vector2 worldPos)
    {
        this.Camera.Position = worldPos;
        this.InteriorBlend = 0f;
        this.Camera.Zoom = this.UserZoom;
    }

    /// <summary>Updates the camera viewport dimensions after a window resize.</summary>
    public void Resize(int width, int height)
    {
        if (width > 0 && height > 0)
        {
            this.Camera.ViewportSize = new Vector2(width, height);
        }
    }

    /// <summary>
    /// Updates camera focus and zoom, easing smoothly into building interiors when inside a room.
    /// </summary>
    public void Update(Vector2 playerPos, bool insideBuilding, in RectangleF buildingRoom, float rateDt)
    {
        if (insideBuilding)
        {
            this.InteriorRoom = buildingRoom;
        }

        float target = insideBuilding ? 1f : 0f;
        this.InteriorBlend += (target - this.InteriorBlend) * Math.Min(1f, rateDt * 4f);
        if (!insideBuilding && this.InteriorBlend < 0.002f)
        {
            this.InteriorBlend = 0f;
        }

        Vector2 roomCentre = new(
            this.InteriorRoom.X + (this.InteriorRoom.Width * 0.5f),
            this.InteriorRoom.Y + (this.InteriorRoom.Height * 0.5f));
        this.Camera.Position = Vector2.Lerp(playerPos, roomCentre, this.InteriorBlend * 0.75f);
        this.Camera.Zoom = this.UserZoom * (1f + (InteriorZoomBoost * this.InteriorBlend));
    }
}
