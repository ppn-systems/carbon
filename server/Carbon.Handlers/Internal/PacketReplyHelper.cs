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
        ushort requestOpCode)
        where TReq : IPacket => SendMutationAsync(context.Sender, status, requestOpCode);

    /// <summary>
    /// Sends a <see cref="MutationResult"/> through a sender captured before an <c>await</c>, so the pooled
    /// <see cref="IPacketContext{TPacket}"/> is never read after it may have been recycled.
    /// </summary>
    internal static ValueTask SendMutationAsync(
        IPacketSender sender,
        OpStatus status,
        ushort requestOpCode)
    {
        PacketScope<MutationResult> lease = PacketFactory<MutationResult>.Acquire();
        try
        {
            MutationResult result = lease.Value;
            result.Status = status;
            result.RequestOpCode = requestOpCode;
            return sender.ReplyAsync(result).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
