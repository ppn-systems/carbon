// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Landmarks;

/// <summary>Kind of an upright structure inside a point of interest. Append only.</summary>
public enum LandmarkPieceKind : byte
{
    /// <summary>Timber-framed house.</summary>
    House = 0,

    /// <summary>Harbour warehouse.</summary>
    Warehouse = 1,

    /// <summary>Roofed village well.</summary>
    Well = 2,

    /// <summary>Canvas tent.</summary>
    Tent = 3,

    /// <summary>Campfire ringed by stones.</summary>
    Campfire = 4,

    /// <summary>Shrine gate (two posts and lintels; only the posts block).</summary>
    ShrineGate = 5,

    /// <summary>Stone lantern.</summary>
    StoneLantern = 6,

    /// <summary>Shrine altar.</summary>
    Altar = 7,

    /// <summary>Stack of barrels.</summary>
    Barrel = 8,
}

/// <summary>An axis-aligned collision box in world tile units.</summary>
/// <param name="MinX">Left edge.</param>
/// <param name="MinY">Top edge.</param>
/// <param name="MaxX">Right edge.</param>
/// <param name="MaxY">Bottom edge.</param>
public readonly record struct TileBox(float MinX, float MinY, float MaxX, float MaxY);

/// <summary>
/// An upright structure of a point of interest. Coordinates are in world tile units; (<see cref="X"/>,
/// <see cref="Y"/>) is the ground contact point at the middle of its front (south) face.
/// </summary>
/// <param name="Kind">Kind of structure.</param>
/// <param name="X">Ground point X in tiles.</param>
/// <param name="Y">Ground point Y in tiles.</param>
/// <param name="Width">Width in tiles (where the kind has a variable width).</param>
/// <param name="Variant">Visual variant.</param>
public readonly record struct LandmarkPiece(LandmarkPieceKind Kind, float X, float Y, float Width, byte Variant)
{
    /// <summary>Maximum number of boxes <see cref="GetColliders"/> writes.</summary>
    public const int MaxColliders = 2;

    // One tile = 32 reference pixels: the art and the footprints below were authored at that scale.
    private const float Px = 1f / 32f;

    /// <summary>
    /// Writes the solid ground footprint of the structure: the body of the building behind its front
    /// wall (not the overhanging roof), so characters stop at the walls and can walk in front of them.
    /// </summary>
    /// <param name="boxes">Destination, at least <see cref="MaxColliders"/> long.</param>
    /// <returns>Number of boxes written.</returns>
    public int GetColliders(Span<TileBox> boxes)
    {
        float hw = this.Width * 0.5f;
        switch (this.Kind)
        {
            case LandmarkPieceKind.House:
                boxes[0] = new TileBox(this.X - hw, this.Y - (40f * Px), this.X + hw, this.Y);
                return 1;
            case LandmarkPieceKind.Warehouse:
                boxes[0] = new TileBox(this.X - hw, this.Y - (48f * Px), this.X + hw, this.Y);
                return 1;
            case LandmarkPieceKind.Well:
                boxes[0] = new TileBox(this.X - (16f * Px), this.Y - (30f * Px), this.X + (16f * Px), this.Y);
                return 1;
            case LandmarkPieceKind.Tent:
                boxes[0] = new TileBox(this.X - hw, this.Y - (18f * Px), this.X + hw, this.Y);
                return 1;
            case LandmarkPieceKind.Campfire:
                boxes[0] = new TileBox(this.X - (14f * Px), this.Y - (10f * Px), this.X + (14f * Px), this.Y + (10f * Px));
                return 1;
            case LandmarkPieceKind.ShrineGate:
                boxes[0] = new TileBox(this.X - hw + (8f * Px), this.Y - (8f * Px), this.X - hw + (17f * Px), this.Y);
                boxes[1] = new TileBox(this.X + hw - (17f * Px), this.Y - (8f * Px), this.X + hw - (8f * Px), this.Y);
                return 2;
            case LandmarkPieceKind.StoneLantern:
                boxes[0] = new TileBox(this.X - (8f * Px), this.Y - (8f * Px), this.X + (8f * Px), this.Y);
                return 1;
            case LandmarkPieceKind.Altar:
                boxes[0] = new TileBox(this.X - (22f * Px), this.Y - (18f * Px), this.X + (22f * Px), this.Y);
                return 1;
            case LandmarkPieceKind.Barrel:
                boxes[0] = new TileBox(this.X - (20f * Px), this.Y - (10f * Px), this.X + (20f * Px), this.Y);
                return 1;
            default:
                return 0;
        }
    }
}

/// <summary>A fenced crop field (walkable, flat) in world tile units.</summary>
/// <param name="MinX">Left edge.</param>
/// <param name="MinY">Top edge.</param>
/// <param name="MaxX">Right edge.</param>
/// <param name="MaxY">Bottom edge.</param>
/// <param name="Crop">Crop variant.</param>
public readonly record struct LandmarkField(float MinX, float MinY, float MaxX, float MaxY, byte Crop);
