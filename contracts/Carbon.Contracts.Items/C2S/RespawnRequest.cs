// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// A dead player asks to respawn (ADR-031). The server accepts it only while the player is dead, then moves them to the
/// sanctuary with full vitals. Answered with a <c>MutationResult</c> (<c>Forbidden</c> when the player is alive).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RespawnRequest : PacketBase<RespawnRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RespawnRequest;
}
