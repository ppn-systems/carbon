// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.S2C;

/// <summary>
/// Server reply to <see cref="C2S.AuthRegister"/> or <see cref="C2S.AuthLogin"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AuthResult : PacketBase<AuthResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.AuthResult;

    /// <summary>Authentication result status.</summary>
    [SerializeOrder(0)]
    public AuthStatus Status { get; set; }

    /// <summary>Authenticated account identifier, or <see cref="Guid.Empty"/> on failure.</summary>
    [SerializeOrder(1)]
    public Guid AccountId { get; set; }

    /// <summary>Assigned server session identifier.</summary>
    [SerializeOrder(2)]
    public ulong SessionId { get; set; }

    /// <summary>Server protocol version for client verification.</summary>
    [SerializeOrder(3)]
    public ushort ServerProtocolVersion { get; set; } = CarbonProtocol.Version;
}
