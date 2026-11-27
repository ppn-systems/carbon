// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Pushed to a client when a networked entity enters its Area of Interest.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntitySpawn : PacketBase<EntitySpawn>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.EntitySpawn;

    /// <summary>Unique authoritative network identifier.</summary>
    [SerializeOrder(0)]
    public ulong NetworkId { get; set; }

    /// <summary>Entity kind.</summary>
    [SerializeOrder(1)]
    public EntityKind Kind { get; set; }

    /// <summary>Monster, NPC or item archetype id (0 for players, whose look is <see cref="RaceId"/> onward).</summary>
    [SerializeOrder(2)]
    public ushort ArchetypeId { get; set; }

    /// <summary>Display name.</summary>
    [SerializeOrder(3)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Initial world X position.</summary>
    [SerializeOrder(4)]
    public float PositionX { get; set; }

    /// <summary>Initial world Y position.</summary>
    [SerializeOrder(5)]
    public float PositionY { get; set; }

    /// <summary>Initial linear X velocity.</summary>
    [SerializeOrder(6)]
    public float VelocityX { get; set; }

    /// <summary>Initial linear Y velocity.</summary>
    [SerializeOrder(7)]
    public float VelocityY { get; set; }

    /// <summary>Collider radius in world units.</summary>
    [SerializeOrder(8)]
    public float Radius { get; set; }

    /// <summary>Current health points.</summary>
    [SerializeOrder(9)]
    public float CurrentHealth { get; set; }

    /// <summary>Maximum health points.</summary>
    [SerializeOrder(10)]
    public float MaxHealth { get; set; }

    /// <summary>Equipped head armor item ID (0 when none).</summary>
    [SerializeOrder(11)]
    public ushort EquippedHeadId { get; set; }

    /// <summary>Equipped chest armor item ID (0 when none).</summary>
    [SerializeOrder(12)]
    public ushort EquippedChestId { get; set; }

    /// <summary>Equipped leg armor item ID (0 when none).</summary>
    [SerializeOrder(13)]
    public ushort EquippedLegsId { get; set; }

    /// <summary>Equipped footwear armor item ID (0 when none).</summary>
    [SerializeOrder(14)]
    public ushort EquippedFeetId { get; set; }

    /// <summary>Active held weapon/tool item ID (0 when none).</summary>
    [SerializeOrder(15)]
    public ushort EquippedWeaponId { get; set; }

    /// <summary>Character/actor display level.</summary>
    [SerializeOrder(16)]
    public ushort Level { get; set; } = 1;

    /// <summary>Stack quantity carried when <see cref="Kind"/> is <see cref="EntityKind.LootDrop"/>.</summary>
    [SerializeOrder(17)]
    public int LootCount { get; set; }

    /// <summary>Enhancement level (<c>0..10</c>) of the equipped weapon or dropped item.</summary>
    [SerializeOrder(18)]
    public byte WeaponUpgradeLevel { get; set; }

    /// <summary>
    /// What a <see cref="EntityKind.LootDrop"/> carries: an item stack, or a gold or diamond pile whose amount is
    /// <see cref="LootCount"/>. ADR-019.
    /// </summary>
    [SerializeOrder(19)]
    public LootCurrency LootCurrency { get; set; }

    /// <summary>Network id of the only player who may pick this loot up while protected (0 = anyone). ADR-019.</summary>
    [SerializeOrder(20)]
    public ulong LootOwnerNetworkId { get; set; }

    /// <summary>Milliseconds of owner protection left when this spawn was sent (0 = anyone may pick it up). ADR-019.</summary>
    [SerializeOrder(21)]
    public uint LootProtectionMs { get; set; }

    /// <summary>Race id of a player (0 for non-players) (<c>races.json</c>, ADR-042).</summary>
    [SerializeOrder(22)]
    public ushort RaceId { get; set; }

    /// <summary>Body type id within the race (ADR-042).</summary>
    [SerializeOrder(23)]
    public byte BodyType { get; set; }

    /// <summary>One choice index per appearance option of the race, in the race's option order; empty for non-players (ADR-042).</summary>
    [SerializeOrder(24)]
    public byte[] AppearanceOptions { get; set; } = [];
}
