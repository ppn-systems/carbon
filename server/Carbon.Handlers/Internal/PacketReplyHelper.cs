// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;

namespace Carbon.Handlers.Internal;

internal static class PacketReplyHelper
{
    internal static ValueTask SendMutationAsync<TReq>(
        IPacketContext<TReq> context,
        OpStatus status,
        ushort requestOpCode,
        uint revision = 0)
        where TReq : IPacket
    {
        PacketScope<MutationResult> lease = PacketFactory<MutationResult>.Acquire();
        try
        {
            MutationResult result = lease.Value;
            result.Status = status;
            result.RequestOpCode = requestOpCode;
            result.Revision = revision;
            return context.Sender.ReplyAsync(result).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
