// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Server push of the local player's gold and diamond balances, sent on world entry and after every change.
/// Balances are server-authoritative (ADR-011); clients only display them.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class WalletSync : PacketBase<WalletSync>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.WalletSync;

    /// <summary>Gold balance.</summary>
    [SerializeOrder(0)]
    public long Gold { get; set; }

    /// <summary>Diamond balance.</summary>
    [SerializeOrder(1)]
    public long Diamonds { get; set; }

    /// <summary>Wallet mutation sequence; clients ignore pushes older than the last one applied.</summary>
    [SerializeOrder(2)]
    public ulong Sequence { get; set; }
}
