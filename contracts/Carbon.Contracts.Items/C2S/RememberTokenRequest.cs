// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// A signed-in player ticked "Remember me": the server issues a remember-me token for the account (ADR-037).
/// Answered with <see cref="S2C.RememberTokenResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RememberTokenRequest : PacketBase<RememberTokenRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RememberTokenRequest;
}
