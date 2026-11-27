// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Abstractions.Configuration;

/// <summary>
/// Developer-only world overrides used by the screenshot review tool (<c>tools/screenshots</c>). They are read
/// from the host's own environment and only in Debug builds of the host (see
/// <c>HostRuntimeOptions.FromEnvironment</c>); nothing here is ever taken from a client.
/// </summary>
public sealed record WorldDebugOverrides
{
    /// <summary>Gets an explicit world tile every character enters the world at, if any.</summary>
    public (int X, int Y)? SpawnTile { get; init; }

    /// <summary>
    /// Gets the name of a biome (e.g. <c>DuneDesert</c>) whose nearest representative tile becomes the spawn
    /// tile, if any. <see cref="SpawnTile"/> wins when both are set.
    /// </summary>
    public string? SpawnBiome { get; init; }

    /// <summary>
    /// Gets the name of a point of interest type (e.g. <c>DesertVillage</c>): characters enter the world just south of
    /// the well of the nearest one (in front of a village's market stall). A village type may name a building role after a
    /// colon (e.g. <c>RiverVillage:Smithy</c>) to stand in front of that building instead. <see cref="SpawnTile"/> and
    /// <see cref="SpawnBiome"/> win when set.
    /// </summary>
    public string? SpawnPoi { get; init; }

    /// <summary>Gets the offset, in tiles, applied to the resolved spawn tile.</summary>
    public (int X, int Y) SpawnOffset { get; init; }

    /// <summary>Gets the in-game hour (0..24) the world clock starts at, if any.</summary>
    public float? StartHour { get; init; }

    /// <summary>Gets the minimum level characters enter the world at (screenshots of mid-level UI), if any.</summary>
    public ushort? StartLevel { get; init; }

    /// <summary>
    /// Gets a value indicating whether new characters start with a forge review kit (a +8 iron sword, enhancement
    /// materials, protection scrolls and gold), for screenshot review of the forge window (ADR-024).
    /// </summary>
    public bool ForgeKit { get; init; }

    /// <summary>Gets extra items granted to newly created characters (screenshots of enhanced gear, ADR-020).</summary>
    public IReadOnlyList<DebugStarterItem> StarterItems { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether new characters start with their gear worn to 40 % durability, for screenshot
    /// review of the blacksmith's repair mode (ADR-025).
    /// </summary>
    public bool WornGear { get; init; }

    /// <summary>
    /// Gets the durability shares (percent of max) the Debug kits' durable items start at, in slot order with the last
    /// repeating, for screenshot review of the durability warning colours (ADR-035); empty for none.
    /// </summary>
    public IReadOnlyList<int> WornGearPercents { get; init; } = [];

    /// <summary>
    /// Gets status effects every character enters the world with (ADR-029), in the persisted format: <c>v2</c> then
    /// <c>key:remainingMs:stacks:level</c> entries (ADR-036 D5), or level-1 <c>key:remainingMs:stacks</c> entries, joined
    /// by <c>;</c>, for screenshot review of the status strip; empty for none.
    /// </summary>
    public string StatusEffects { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the trees nearest the spawn start at each growth stage in turn (stump, sapling,
    /// young, mature; ADR-040) and stay there, for screenshot review of the stage art.
    /// </summary>
    public bool TreeStages { get; init; }

    /// <summary>
    /// Gets a value indicating whether a grove of planted saplings and young trees starts south of the spawn (ADR-040),
    /// for screenshot review of planting.
    /// </summary>
    public bool PlantedGrove { get; init; }

    /// <summary>Gets a value indicating whether any override is set.</summary>
    public bool IsActive => this.SpawnTile.HasValue || this.SpawnBiome is not null || this.SpawnPoi is not null || this.StartHour.HasValue ||
        this.SpawnOffset != default || this.StartLevel.HasValue || this.ForgeKit || this.StarterItems.Count > 0 || this.WornGear || this.WornGearPercents.Count > 0 ||
        this.StatusEffects.Length > 0 || this.TreeStages || this.PlantedGrove;
}
