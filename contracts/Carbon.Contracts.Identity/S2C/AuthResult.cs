// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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

    // [SerializeOrder(2)] was SessionId, never read; removed by ADR-038.

    // [SerializeOrder(3)] was ServerProtocolVersion, never read; removed by ADR-038.
}
