// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Maps.Props;

/// <summary>
/// Unmanaged record struct describing a physical or decorative environmental entity placed within a map chunk.
/// Follows DDD principles with clear domain categorization, destructibility parameters, canopy occlusion,
/// and trunk-vs-canopy physical collision separation (RimWorld 3-layer architecture). Zero heap allocations.
/// </summary>
public readonly record struct MapProp(
    MapPropType Type,
    PropCategory Category,
    PropDestructibility Destructibility,
    int LocalTileX,
    int LocalTileY,
    float WorldPositionX,
    float WorldPositionY,
    float Radius,
    float CollisionRadius,
    float CanopyRadius,
    float CanopyOffsetY,
    bool IsSolid,
    uint ColorPrimary,
    uint ColorSecondary,
    float Scale)
{
    /// <summary>
    /// Gets the world coordinate vector of this prop ground base.
    /// </summary>
    public Vector2 WorldPosition => new(this.WorldPositionX, this.WorldPositionY);

    /// <summary>
    /// Gets a value indicating whether this prop acts as a physical collision obstacle on the ground.
    /// Trees only collide at their trunk base (<see cref="CollisionRadius"/>), while ores collide across their outcrop.
    /// </summary>
    public bool HasCollision => this.IsSolid && this.CollisionRadius > 0.001f && !this.Destructibility.IsDestroyed;

    /// <summary>
    /// Gets a value indicating whether this prop possesses an overhead leafy canopy or roof foliage.
    /// </summary>
    public bool HasCanopy => this.CanopyRadius > 0.001f && !this.Destructibility.IsDestroyed;

    /// <summary>
    /// Gets the world coordinate center of the overhead canopy in 2.5D space.
    /// </summary>
    public Vector2 CanopyCenter => new(this.WorldPositionX, this.WorldPositionY + this.CanopyOffsetY);

    /// <summary>
    /// Evaluates whether an entity position is currently standing underneath the shade/canopy of this tree.
    /// When true, rendering systems should fade the overhead foliage to transparent to reveal the player and the trunk underneath.
    /// </summary>
    /// <param name="entityPosition">World coordinate of the entity/player.</param>
    /// <returns>True if the entity is under the canopy; otherwise false.</returns>
    public bool IsUnderCanopy(Vector2 entityPosition) =>
        this.HasCanopy && Vector2.DistanceSquared(entityPosition, this.CanopyCenter) <= (this.CanopyRadius * this.CanopyRadius);

    /// <summary>
    /// Gets the vertical anchor coordinate in world space used for 2.5D depth sorting (Layer 2).
    /// Represents the bottom-most ground contact footprint where the prop anchors into the terrain.
    /// In top-down / 2.5D rendering, objects with smaller footprints are drawn earlier (north/back),
    /// while objects with larger footprints are drawn later (south/front).
    /// </summary>
    public float GroundFootprintY => this.Type switch
    {
        MapPropType.OakTree => this.WorldPositionY + (14f * this.Scale),
        MapPropType.BirchTree => this.WorldPositionY + (22f * this.Scale),
        MapPropType.PineTree or MapPropType.SnowPine => this.WorldPositionY + (20f * this.Scale),
        MapPropType.PalmTree => this.WorldPositionY + (26f * this.Scale),
        MapPropType.WillowTree => this.WorldPositionY + (20f * this.Scale),
        MapPropType.DeadTree => this.WorldPositionY + (18f * this.Scale),
        MapPropType.Cactus => this.WorldPositionY + (20f * this.Scale),
        MapPropType.AncientShrine => this.WorldPositionY + (20f * this.Scale),
        MapPropType.Bush or MapPropType.MushroomCluster => this.WorldPositionY + this.Radius,
        MapPropType.CopperVein or MapPropType.IronVein or MapPropType.GoldVein or
        MapPropType.CoalDeposit or MapPropType.SilverVein or MapPropType.CrystalCluster or
        MapPropType.Boulder or MapPropType.BasaltPillar => this.WorldPositionY + (this.CollisionRadius > 0.001f ? this.CollisionRadius : this.Radius),
        MapPropType.None => this.WorldPositionY,
        _ => this.WorldPositionY + (this.CollisionRadius > 0.001f ? this.CollisionRadius : this.Radius)
    };

    /// <summary>
    /// Creates a complete domain prop instance with explicit category, destructibility, collision radius, and canopy geometry.
    /// </summary>
    public static MapProp Create(
        MapPropType type,
        PropCategory category,
        PropDestructibility destructibility,
        int localX,
        int localY,
        float worldX,
        float worldY,
        float radius,
        float collisionRadius,
        float canopyRadius,
        float canopyOffsetY,
        bool isSolid,
        uint colorPrimary,
        uint colorSecondary,
        float scale = 1.0f) =>
        new(type, category, destructibility, localX, localY, worldX, worldY, radius, collisionRadius, canopyRadius, canopyOffsetY, isSolid, colorPrimary, colorSecondary, scale);

    /// <summary>
    /// Legacy factory overload providing automatic default inference for category, destructibility, and collision bounds.
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
        float scale = 1.0f)
    {
        bool isTree = type is MapPropType.OakTree or MapPropType.BirchTree or MapPropType.PineTree or MapPropType.SnowPine or MapPropType.PalmTree or MapPropType.WillowTree;
        bool isFlora = isTree || type is MapPropType.DeadTree or MapPropType.Cactus or MapPropType.Bush or MapPropType.MushroomCluster;
        bool isOre = type is MapPropType.CopperVein or MapPropType.IronVein or MapPropType.GoldVein or MapPropType.CoalDeposit or MapPropType.SilverVein or MapPropType.CrystalCluster or MapPropType.Boulder or MapPropType.BasaltPillar;

        PropCategory category = isTree || isFlora ? PropCategory.Flora : (isOre ? PropCategory.Mineral : PropCategory.Structure);
        PropDestructibility destruct = isOre ? PropDestructibility.Create(200f, 1) : (isTree ? PropDestructibility.Create(100f, 1) : PropDestructibility.Create(50f, 0));

        float collisionRadius = isTree ? (8f * scale) : (isSolid ? radius : 0f);
        float canopyRadius = isTree ? radius : 0f;
        float canopyOffsetY = isTree ? (-22f * scale) : 0f;

        return new MapProp(
            type,
            category,
            destruct,
            localX,
            localY,
            worldX,
            worldY,
            radius,
            collisionRadius,
            canopyRadius,
            canopyOffsetY,
            isSolid,
            colorPrimary,
            colorSecondary,
            scale);
    }
}
