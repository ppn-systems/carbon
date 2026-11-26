// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Broadcasts updated visible armor, held weapon, enhancement glow, and level for an AoI player entity.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityEquipmentChanged : PacketBase<EntityEquipmentChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.EntityEquipmentChanged;

    /// <summary>Target player network entity ID.</summary>
    [SerializeOrder(0)]
    public ulong NetworkId { get; set; }

    /// <summary>Equipped head armor item ID.</summary>
    [SerializeOrder(1)]
    public ushort EquippedHeadId { get; set; }

    /// <summary>Equipped chest armor item ID.</summary>
    [SerializeOrder(2)]
    public ushort EquippedChestId { get; set; }

    /// <summary>Equipped leg armor item ID.</summary>
    [SerializeOrder(3)]
    public ushort EquippedLegsId { get; set; }

    /// <summary>Equipped footwear armor item ID.</summary>
    [SerializeOrder(4)]
    public ushort EquippedFeetId { get; set; }

    /// <summary>Active held weapon/tool item ID.</summary>
    [SerializeOrder(5)]
    public ushort EquippedWeaponId { get; set; }

    /// <summary>Active weapon enhancement level (<c>0..10</c>).</summary>
    [SerializeOrder(6)]
    public byte WeaponUpgradeLevel { get; set; }

    /// <summary>Current character level.</summary>
    [SerializeOrder(7)]
    public ushort Level { get; set; } = 1;

    /// <summary>Updated derived maximum health points (0 when unchanged).</summary>
    [SerializeOrder(8)]
    public float MaxHealth { get; set; }
}
