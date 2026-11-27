// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client asks to trade with a nearby trader NPC. The server answers with <see cref="S2C.TraderStock"/>, or with a
/// <see cref="Common.MutationResult"/> failure when the trader is unknown or out of reach (ADR-025).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderOpenRequest : PacketBase<TraderOpenRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderOpen;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }
}
