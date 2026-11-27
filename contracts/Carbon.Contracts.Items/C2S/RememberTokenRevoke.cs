// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Primitives;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Log out on this device: the server deletes the remember-me token so it can never sign in again (ADR-037).
/// Holding the token is the authority, so the connection need not be signed in. Answered with a <c>MutationResult</c>
/// (<c>Ok</c> even when the token was already gone, so the reply says nothing about which tokens exist).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RememberTokenRevoke : PacketBase<RememberTokenRevoke>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RememberTokenRevoke;

    /// <summary>The token to revoke.</summary>
    [SerializeOrder(0)]
    public Bytes32 Token { get; set; }
}
