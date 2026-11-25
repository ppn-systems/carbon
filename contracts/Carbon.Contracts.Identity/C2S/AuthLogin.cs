// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.C2S;

/// <summary>
/// Client request to sign into an existing account. Server responds with <see cref="S2C.AuthResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AuthLogin : PacketBase<AuthLogin>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.Login;

    /// <summary>Account username.</summary>
    [SerializeOrder(0)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Account password over encrypted Nalix transport.</summary>
    [SerializeOrder(1)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Client protocol version (<see cref="CarbonProtocol.Version"/>).</summary>
    [SerializeOrder(2)]
    public ushort ClientProtocolVersion { get; set; } = CarbonProtocol.Version;
}
