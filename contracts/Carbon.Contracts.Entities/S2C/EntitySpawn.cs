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

    /// <summary>Archetype or visual preset identifier.</summary>
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
}
