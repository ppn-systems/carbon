// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.C2S;

/// <summary>
/// Client request to pick up an active ground <see cref="EntityKind.LootDrop"/> entity.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityPickupLoot : PacketBase<EntityPickupLoot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.PickupLoot;

    /// <summary>Network entity identifier of the ground loot drop.</summary>
    [SerializeOrder(0)]
    public ulong LootNetworkId { get; set; }
}
