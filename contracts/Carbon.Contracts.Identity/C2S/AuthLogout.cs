// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.C2S;

/// <summary>
/// Client notification requesting orderly session logout and character despawn.
/// Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AuthLogout : PacketBase<AuthLogout>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.Logout;
}
