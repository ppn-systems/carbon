// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Maps.Data;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Maps world tiles to minimap pixels: north is up, the view is centred on the player's (fractional) tile so the map
/// scrolls smoothly, and <see cref="RadiusTiles"/> tiles fit between the centre and each edge.
/// </summary>
internal readonly record struct MinimapView(float CenterTileX, float CenterTileY, float RadiusTiles, RectangleF MapRect)
{
    /// <summary>Zoom presets: tiles from the centre to an edge (index <c>0</c> is the closest).</summary>
    private static readonly float[] s_zoomRadii = [20f, 32f, 52f];

    /// <summary>Gets the number of zoom presets.</summary>
    internal static int ZoomLevelCount => s_zoomRadii.Length;

    /// <summary>Gets the default zoom preset.</summary>
    internal const int DefaultZoomLevel = 1;

    /// <summary>Gets screen pixels per world tile.</summary>
    public float PixelsPerTile => this.MapRect.Width / (this.RadiusTiles * 2f);

    /// <summary>Gets the first world tile column visible on the map.</summary>
    public float MinTileX => this.CenterTileX - this.RadiusTiles;

    /// <summary>Gets the first world tile row visible on the map.</summary>
    public float MinTileY => this.CenterTileY - this.RadiusTiles;

    /// <summary>Returns the radius of zoom preset <paramref name="level"/> (clamped).</summary>
    internal static float RadiusForZoom(int level) => s_zoomRadii[Math.Clamp(level, 0, s_zoomRadii.Length - 1)];

    /// <summary>Builds the view around a world position.</summary>
    internal static MinimapView Create(Vector2 worldPosition, float tileSize, int zoomLevel, RectangleF mapRect) =>
        new(worldPosition.X / tileSize, worldPosition.Y / tileSize, RadiusForZoom(zoomLevel), mapRect);

    /// <summary>Projects a world tile coordinate (fractional) to a screen point.</summary>
    public Vector2 Project(float tileX, float tileY) =>
        new(
            this.MapRect.X + ((tileX - this.MinTileX) * this.PixelsPerTile),
            this.MapRect.Y + ((tileY - this.MinTileY) * this.PixelsPerTile));

    /// <summary>Projects a world position (world units) to a screen point.</summary>
    public Vector2 ProjectWorld(Vector2 worldPosition, float tileSize) => this.Project(worldPosition.X / tileSize, worldPosition.Y / tileSize);

    /// <summary>Returns whether <paramref name="screenPoint"/> lies inside the map, shrunk by <paramref name="inset"/> pixels.</summary>
    public bool IsInside(Vector2 screenPoint, float inset = 0f) =>
        screenPoint.X >= this.MapRect.X + inset && screenPoint.X <= this.MapRect.Right - inset &&
        screenPoint.Y >= this.MapRect.Y + inset && screenPoint.Y <= this.MapRect.Bottom - inset;

    /// <summary>Gets the inclusive chunk range the view touches.</summary>
    public (int MinX, int MinY, int MaxX, int MaxY) VisibleChunks()
    {
        int minX = Chunk2D.ToChunkCoord((int)MathF.Floor(this.MinTileX));
        int minY = Chunk2D.ToChunkCoord((int)MathF.Floor(this.MinTileY));
        int maxX = Chunk2D.ToChunkCoord((int)MathF.Floor(this.CenterTileX + this.RadiusTiles));
        int maxY = Chunk2D.ToChunkCoord((int)MathF.Floor(this.CenterTileY + this.RadiusTiles));
        return (minX, minY, maxX, maxY);
    }
}
