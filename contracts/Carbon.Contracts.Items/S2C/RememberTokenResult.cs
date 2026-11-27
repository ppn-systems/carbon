// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Primitives;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Reply to <see cref="C2S.RememberTokenRequest"/> (ADR-037). The token is a bearer secret: the client stores it in the
/// player's own profile and sends it back only in <see cref="C2S.TokenSignInRequest"/> or <see cref="C2S.RememberTokenRevoke"/>.
/// The server keeps only its SHA-256 hash.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RememberTokenResult : PacketBase<RememberTokenResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RememberTokenResult;

    /// <summary><see cref="OpStatus.Ok"/> with a token, or <see cref="OpStatus.Forbidden"/> when the connection is not signed in.</summary>
    [SerializeOrder(0)]
    public OpStatus Status { get; set; }

    /// <summary>The token: 32 random bytes, <see cref="Bytes32.Zero"/> unless <see cref="Status"/> is <see cref="OpStatus.Ok"/>.</summary>
    [SerializeOrder(1)]
    public Bytes32 Token { get; set; }

    /// <summary>When the token stops working, in Unix seconds (UTC).</summary>
    [SerializeOrder(2)]
    public long ExpiresAtUnixSeconds { get; set; }
}
