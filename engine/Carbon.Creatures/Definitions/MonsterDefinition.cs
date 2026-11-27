// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Data.Abstractions;
using Carbon.Maps.Biomes;

namespace Carbon.Creatures.Definitions;

/// <summary>
/// Static, data-driven monster definition loaded from <c>resources/config/monsters.json</c>.
/// The server never sends sprite data: clients resolve visuals from <see cref="VisualArchetype"/>.
/// </summary>
public sealed class MonsterDefinition : IDataDefinition<string>
{
    /// <summary>Gets the unique snake_case monster identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the protocol archetype id replicated in <c>EntitySpawn</c>; clients resolve the vector rig from it
    /// (see <c>WorldActorCatalog</c>). Elite variants may share their base monster's archetype.
    /// </summary>
    public ushort ArchetypeId { get; set; }

    /// <summary>Gets the client visual archetype key used to pick sprites / animation sets.</summary>
    public string VisualArchetype { get; set; } = string.Empty;

    /// <summary>Gets the loot table identifier (by convention equal to <see cref="Id"/>).</summary>
    public string LootTableId { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether this definition is an elite variant.</summary>
    public bool IsElite { get; set; }

    /// <summary>Gets the optional identifier of the elite variant spawned on an elite roll.</summary>
    public string? EliteVariant { get; set; }

    /// <summary>Gets the collision radius.</summary>
    public float Radius { get; set; } = 8f;

    /// <summary>Gets the level-1 daytime base stats.</summary>
    public MonsterBaseStats Stats { get; set; } = new();

    /// <summary>Gets the target detection radius.</summary>
    public float AggroRange { get; set; }

    /// <summary>Gets the tether radius around the spawn point before leashing home.</summary>
    public float LeashRange { get; set; }

    /// <summary>Gets the distance at which the monster stops chasing and starts fighting.</summary>
    public float EngageRange { get; set; }

    /// <summary>Gets the distance below which the monster backs off (kiting); 0 for melee brawlers.</summary>
    public float RetreatRange { get; set; }

    /// <summary>Gets the idle patrol radius around the spawn point.</summary>
    public float PatrolRadius { get; set; }

    /// <summary>Gets the lowest level at which this monster spawns (aligned with its loot table band).</summary>
    public int MinLevel { get; set; } = 1;

    /// <summary>Gets the highest level at which this monster spawns.</summary>
    public int MaxLevel { get; set; } = 100;

    /// <summary>Gets the biomes this monster naturally inhabits.</summary>
    public IReadOnlyList<BiomeType> PreferredBiomes { get; set; } = [];

    /// <summary>Gets the tactical personality (positioning role, pack, flee, give-up, night aggression).</summary>
    public MonsterBehaviorDefinition Behavior { get; set; } = new();

    /// <summary>Gets the attack profiles in priority order.</summary>
    public IReadOnlyList<AttackProfileDefinition> Attacks { get; set; } = [];

    /// <summary>
    /// Determines whether this monster may spawn at <paramref name="level"/>.
    /// </summary>
    /// <param name="level">Spawn-point level from <see cref="Scaling.MonsterScaling.EvaluateLevel"/>.</param>
    /// <returns><see langword="true"/> inside <c>[MinLevel, MaxLevel]</c>.</returns>
    public bool IsSpawnableAt(int level) => level >= this.MinLevel && level <= this.MaxLevel;
}
