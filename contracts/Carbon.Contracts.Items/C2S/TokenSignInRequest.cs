// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Signs in with a remember-me token instead of a password (ADR-037). Answered with the identity module's
/// <c>AuthResult</c>: <c>Ok</c>, or <c>InvalidCredentials</c> for an unknown, expired or revoked token.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TokenSignInRequest : PacketBase<TokenSignInRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TokenSignInRequest;

    /// <summary>The token from <see cref="S2C.RememberTokenResult"/>, sent over the encrypted session.</summary>
    [SerializeOrder(0)]
    public byte[] Token { get; set; } = [];

    /// <summary>Client protocol version (<see cref="CarbonProtocol.Version"/>).</summary>
    [SerializeOrder(1)]
    public ushort ClientProtocolVersion { get; set; } = CarbonProtocol.Version;
}
