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
    public const int MaxColliders = 5;

    /// <summary>Building style (high nibble of <see cref="Variant"/>): temperate timber house.</summary>
    public const byte StyleTemperate = 0;

    /// <summary>Building style: desert adobe house with a flat roof.</summary>
    public const byte StyleDesert = 1;

    /// <summary>Building style: snow-covered log cabin.</summary>
    public const byte StyleSnow = 2;

    /// <summary>Building style: swamp house raised on stilts.</summary>
    public const byte StyleSwamp = 3;

    /// <summary>Depth (north-south) of a house body in tiles.</summary>
    public const float HouseDepthTiles = 112f * Px;

    /// <summary>Depth of the warehouse body in tiles.</summary>
    public const float WarehouseDepthTiles = 128f * Px;

    /// <summary>Wall thickness in tiles.</summary>
    public const float WallTiles = 5f * Px;

    // One tile = 32 reference pixels: the art and the footprints below were authored at that scale.
    private const float Px = 1f / 32f;

    /// <summary>Gets the building style (high nibble of <see cref="Variant"/>).</summary>
    public byte Style => (byte)(this.Variant >> 4);

    /// <summary>Gets the decorative variant (low nibble of <see cref="Variant"/>).</summary>
    public byte Look => (byte)(this.Variant & 0x0F);

    /// <summary>Gets whether this structure has an interior a character can walk into.</summary>
    public bool HasInterior => this.Kind is LandmarkPieceKind.House or LandmarkPieceKind.Warehouse;

    /// <summary>Width in tiles of the door opening in the front wall.</summary>
    public float DoorWidth => this.Kind == LandmarkPieceKind.Warehouse ? 48f * Px : 32f * Px;

    /// <summary>Depth in tiles of the building body (0 for structures without an interior).</summary>
    public float Depth => this.Kind switch
    {
        LandmarkPieceKind.House => HouseDepthTiles,
        LandmarkPieceKind.Warehouse => WarehouseDepthTiles,
        LandmarkPieceKind.Well or LandmarkPieceKind.Tent or LandmarkPieceKind.Campfire or LandmarkPieceKind.ShrineGate or
        LandmarkPieceKind.StoneLantern or LandmarkPieceKind.Altar or LandmarkPieceKind.Barrel => 0f,
        _ => 0f,
    };

    /// <summary>Returns whether a point (in tiles) is inside the walls of a building.</summary>
    /// <param name="x">X in tiles.</param>
    /// <param name="y">Y in tiles.</param>
    /// <returns><c>true</c> when the point is in the interior.</returns>
    public bool IsInside(float x, float y)
    {
        if (!this.HasInterior)
        {
            return false;
        }

        float hw = this.Width * 0.5f;
        return x > this.X - hw + WallTiles && x < this.X + hw - WallTiles &&
               y > this.Y - this.Depth + WallTiles && y < this.Y;
    }

    /// <summary>
    /// Writes the footprint used for navigation: a building is one solid block, other structures use their
    /// colliders.
    /// </summary>
    /// <param name="boxes">Destination, at least <see cref="MaxColliders"/> long.</param>
    /// <returns>Number of boxes written.</returns>
    public int GetNavFootprint(Span<TileBox> boxes)
    {
        if (this.HasInterior)
        {
            float hw = this.Width * 0.5f;
            boxes[0] = new TileBox(this.X - hw, this.Y - this.Depth, this.X + hw, this.Y);
            return 1;
        }

        return this.GetColliders(boxes);
    }

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
            case LandmarkPieceKind.Warehouse:
                {
                    // Four walls with a door opening in the front wall, so characters can walk inside.
                    float d = this.Depth;
                    float t = WallTiles;
                    float door = this.DoorWidth * 0.5f;
                    boxes[0] = new TileBox(this.X - hw, this.Y - d, this.X + hw, this.Y - d + t);
                    boxes[1] = new TileBox(this.X - hw, this.Y - d, this.X - hw + t, this.Y);
                    boxes[2] = new TileBox(this.X + hw - t, this.Y - d, this.X + hw, this.Y);
                    boxes[3] = new TileBox(this.X - hw, this.Y - t, this.X - door, this.Y);
                    boxes[4] = new TileBox(this.X + door, this.Y - t, this.X + hw, this.Y);
                    return 5;
                }
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
