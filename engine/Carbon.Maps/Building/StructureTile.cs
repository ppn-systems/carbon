// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Props;

namespace Carbon.Maps.Building;

/// <summary>
/// Operational state of a constructed door passage.
/// </summary>
public enum DoorState : byte
{
    /// <summary>
    /// Door is closed, blocking entity movement and physical passage.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// Door is open, permitting unhindered movement through the threshold.
    /// </summary>
    Open = 1
}

/// <summary>
/// Unmanaged value object representing a constructed building unit positioned on a world tile.
/// Integrates destructibility, autotiling connectivity, collision, and 3-layer architecture.
/// Zero heap allocations.
/// </summary>
public readonly record struct StructureTile(
    StructureType Type,
    StructureMaterial Material,
    StructureCategory Category,
    PropDestructibility Destructibility,
    DoorState DoorState,
    WallConnectivityMask Connectivity,
    bool HasRoof,
    float MoveSpeedMultiplier)
{
    /// <summary>
    /// Gets an empty structure tile.
    /// </summary>
    public static StructureTile Empty => default;

    /// <summary>
    /// Gets a value indicating whether this tile currently contains an active constructed structure.
    /// </summary>
    public bool IsOccupied => this.Type != StructureType.None;

    /// <summary>
    /// Gets a value indicating whether this structure acts as an impassable physical collision obstacle on Layer 2.
    /// </summary>
    public bool IsSolid => this.Category switch
    {
        StructureCategory.Wall => !this.Destructibility.IsDestroyed,
        StructureCategory.Door => this.DoorState is DoorState.Closed && !this.Destructibility.IsDestroyed,
        StructureCategory.Pillar => !this.Destructibility.IsDestroyed,
        StructureCategory.Station => !this.Destructibility.IsDestroyed,
        StructureCategory.Furniture => !this.Destructibility.IsDestroyed,
        StructureCategory.Floor => false,
        StructureCategory.None => false,
        _ => false
    };

    /// <summary>
    /// Gets a value indicating whether this structure can support overhead roof tiles within its support span.
    /// </summary>
    public bool SupportsRoof => (this.Category is StructureCategory.Wall or StructureCategory.Pillar) && !this.Destructibility.IsDestroyed;

    /// <summary>
    /// Gets a value indicating whether this structure has been reduced to rubble or destroyed.
    /// </summary>
    public bool IsDestroyed => this.Destructibility.IsDestroyed;

    /// <summary>
    /// Gets the current durability health points remaining.
    /// </summary>
    public float CurrentDurability => this.Destructibility.CurrentDurability;

    /// <summary>
    /// Gets the maximum durability health points of this structure.
    /// </summary>
    public float MaxDurability => this.Destructibility.MaxDurability;

    /// <summary>
    /// Gets the 16-state autotiling wall connection shape derived from cardinal connectivity flags.
    /// </summary>
    public WallConnectionShape ConnectionShape => (WallConnectionShape)(byte)this.Connectivity;

    /// <summary>
    /// Inflicts durability damage upon this structure, potentially destroying it into rubble.
    /// </summary>
    /// <param name="damage">Damage amount inflicted.</param>
    /// <returns>A new <see cref="StructureTile"/> instance with updated health.</returns>
    public StructureTile ApplyDamage(float damage) =>
        new(this.Type, this.Material, this.Category, this.Destructibility.ApplyDamage(damage), this.DoorState, this.Connectivity, this.HasRoof, this.MoveSpeedMultiplier);

    /// <summary>
    /// Produces a copy with updated cardinal connectivity flags.
    /// </summary>
    public StructureTile WithConnectivity(WallConnectivityMask connectivity) =>
        new(this.Type, this.Material, this.Category, this.Destructibility, this.DoorState, connectivity, this.HasRoof, this.MoveSpeedMultiplier);

    /// <summary>
    /// Produces a copy with toggled door operational state (Open/Closed).
    /// </summary>
    public StructureTile WithDoorState(DoorState state) =>
        new(this.Type, this.Material, this.Category, this.Destructibility, state, this.Connectivity, this.HasRoof, this.MoveSpeedMultiplier);

    /// <summary>
    /// Produces a copy with updated overhead roof coverage status.
    /// </summary>
    public StructureTile WithRoof(bool hasRoof) =>
        new(this.Type, this.Material, this.Category, this.Destructibility, this.DoorState, this.Connectivity, hasRoof, this.MoveSpeedMultiplier);

    /// <summary>
    /// Creates a constructible wall blueprint instance.
    /// </summary>
    public static StructureTile CreateWall(StructureType type, StructureMaterial material, float durability = 150f, byte harvestTier = 1) =>
        new(type, material, StructureCategory.Wall, PropDestructibility.Create(durability, harvestTier), DoorState.Closed, WallConnectivityMask.None, false, 0f);

    /// <summary>
    /// Creates a constructible door passage blueprint instance.
    /// </summary>
    public static StructureTile CreateDoor(StructureType type, StructureMaterial material, float durability = 100f, byte harvestTier = 1) =>
        new(type, material, StructureCategory.Door, PropDestructibility.Create(durability, harvestTier), DoorState.Closed, WallConnectivityMask.None, false, 0.9f);

    /// <summary>
    /// Creates a constructible floor planking / tiling instance.
    /// </summary>
    public static StructureTile CreateFloor(StructureType type, StructureMaterial material, float moveSpeedMultiplier = 1.15f) =>
        new(type, material, StructureCategory.Floor, PropDestructibility.Create(50f, 0), DoorState.Closed, WallConnectivityMask.None, false, moveSpeedMultiplier);

    /// <summary>
    /// Creates a constructible roof pillar support column instance.
    /// </summary>
    public static StructureTile CreatePillar(StructureMaterial material, float durability = 300f) =>
        new(StructureType.RoofPillar, material, StructureCategory.Pillar, PropDestructibility.Create(durability, 1), DoorState.Closed, WallConnectivityMask.None, true, 0f);

    /// <summary>
    /// Creates an interactive workstation tile instance (e.g. blacksmith forge anvil).
    /// </summary>
    public static StructureTile CreateStation(StructureType type, StructureMaterial material, float durability = 350f, byte harvestTier = 2) =>
        new(type, material, StructureCategory.Station, PropDestructibility.Create(durability, harvestTier), DoorState.Closed, WallConnectivityMask.None, false, 0f);

    /// <summary>
    /// Creates a solid furniture tile instance (e.g. chest, bed).
    /// </summary>
    public static StructureTile CreateFurniture(StructureType type, StructureMaterial material, float durability = 80f, byte harvestTier = 1) =>
        new(type, material, StructureCategory.Furniture, PropDestructibility.Create(durability, harvestTier), DoorState.Closed, WallConnectivityMask.None, false, 0f);
}
