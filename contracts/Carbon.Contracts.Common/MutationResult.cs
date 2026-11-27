// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Common;

/// <summary>
/// Canonical result returned by every write handler across all modules.
/// <see cref="Status"/> carries the outcome and <see cref="RequestOpCode"/> the request it answers, so a client can wait for
/// the reply to its own request.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MutationResult : PacketBase<MutationResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => SharedOpCode.MutationResult;

    /// <summary>Outcome of the mutation.</summary>
    [SerializeOrder(0)]
    public OpStatus Status { get; set; }

    /// <summary>The opcode of the request this result answers, so one result type serves every module.</summary>
    [SerializeOrder(1)]
    public ushort RequestOpCode { get; set; }

    // [SerializeOrder(2)] was Revision, never read; removed by ADR-038.
}
