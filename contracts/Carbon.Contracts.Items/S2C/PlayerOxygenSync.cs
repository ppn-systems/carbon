// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// The local player's swim state and oxygen (ADR-032 D12). Sent to the owning connection only: at world entry, when
/// the swim state or the depth tier changes, and when the oxygen changes by a whole percent. Numbers only.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlayerOxygenSync : PacketBase<PlayerOxygenSync>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.PlayerOxygenSync;

    /// <summary>Gets or sets the swim state: 0 walking, 1 wading, 2 swimming, 3 diving.</summary>
    [SerializeOrder(0)]
    public byte SwimState { get; set; }

    /// <summary>Gets or sets the water depth tier under the player: 0 dry, 1 shore … 5 abyss.</summary>
    [SerializeOrder(1)]
    public byte DepthTier { get; set; }

    /// <summary>Gets or sets the oxygen left, in milliseconds of diving.</summary>
    [SerializeOrder(2)]
    public uint OxygenMs { get; set; }

    /// <summary>Gets or sets the full oxygen pool, in milliseconds.</summary>
    [SerializeOrder(3)]
    public uint MaxOxygenMs { get; set; }
}
