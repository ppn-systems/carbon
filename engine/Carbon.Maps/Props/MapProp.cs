// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Maps.Props;

/// <summary>
/// Unmanaged record struct describing a physical or decorative object placed within a map chunk.
/// Zero heap allocations on hot loops.
/// </summary>
public readonly record struct MapProp(
    MapPropType Type,
    int LocalTileX,
    int LocalTileY,
    float WorldPositionX,
    float WorldPositionY,
    float Radius,
    bool IsSolid,
    uint ColorPrimary,
    uint ColorSecondary,
    float Scale)
{
    /// <summary>
    /// Gets the world coordinate vector of this prop center.
    /// </summary>
    public Vector2 WorldPosition => new(this.WorldPositionX, this.WorldPositionY);

    /// <summary>
    /// Gets a value indicating whether this prop acts as a physical collision obstacle.
    /// </summary>
    public bool HasCollision => this.IsSolid && this.Radius > 0.001f;

    /// <summary>
    /// Creates a prop instance with physical dimensions and color styling.
    /// </summary>
    public static MapProp Create(
        MapPropType type,
        int localX,
        int localY,
        float worldX,
        float worldY,
        float radius,
        bool isSolid,
        uint colorPrimary,
        uint colorSecondary,
        float scale = 1.0f) =>
        new(type, localX, localY, worldX, worldY, radius, isSolid, colorPrimary, colorSecondary, scale);
}
