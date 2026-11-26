// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Config;
using Carbon.Contracts.Config.C2S;
using Carbon.Contracts.Config.S2C;
using Carbon.Server.Configuration;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;

namespace Carbon.Handlers.Config;

/// <summary>
/// Server-authoritative config sync (ADR-014): sends the manifest after sign-in and streams the documents
/// a client asks for. Requests are untrusted and validated by <see cref="ConfigRepository"/>.
/// </summary>
[PacketHandler("Carbon.Config")]
public static partial class ConfigHandlers
{
    [Inject] private static ConfigRepository s_config = null!;

    /// <summary>
    /// Opcode <c>0x0201</c>. Streams the requested documents. A rejected request still ends the client's stream with a
    /// terminator; a stale request is first answered with the current manifest so the client re-syncs against it.
    /// </summary>
    [PacketOpcode(ConfigOpCode.Request)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleRequestAsync(IPacketContext<ConfigRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ConfigRequestStatus status = await s_config
            .HandleRequestAsync(context.Packet, sender.ReplyAsync, context.CancellationToken)
            .ConfigureAwait(false);
        if (status == ConfigRequestStatus.Ok)
        {
            return;
        }

        if (status == ConfigRequestStatus.StaleManifest)
        {
            await SendManifestAsync(sender, context.CancellationToken).ConfigureAwait(false);
        }

        using PacketScope<ConfigDocument> lease = PacketFactory<ConfigDocument>.Acquire();
        ConfigDocument terminator = lease.Value;
        terminator.IsEndOfStream = true;
        terminator.IsTerminator = true;
        terminator.Name = string.Empty;
        terminator.ContentHash = 0;
        terminator.Version = 0;
        terminator.TotalLength = 0;
        terminator.Offset = 0;
        terminator.Payload = [];
        await sender.ReplyAsync(terminator, context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends the current config manifest to the client behind <paramref name="sender"/>.</summary>
    /// <param name="sender">Reply channel of a signed-in client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the manifest was sent.</returns>
    internal static async ValueTask SendManifestAsync(IPacketSender sender, CancellationToken cancellationToken)
    {
        using ConfigManifest manifest = s_config.CreateManifest();
        await sender.ReplyAsync(manifest, cancellationToken).ConfigureAwait(false);
    }
}
