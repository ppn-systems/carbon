// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.C2S;
using Carbon.Desktop.Abstractions.World;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

/// <summary>Death screen state and the respawn request (ADR-031); the rules live in <see cref="DeathSession"/>.</summary>
public sealed partial class MultiplayerSession
{
    /// <inheritdoc/>
    public DeathView? LocalDeath => _death.Death;

    /// <inheritdoc/>
    public bool IsRespawnPending => _death.IsRespawnPending;

    /// <inheritdoc/>
    public bool TryRequestRespawn() => this.IsReady && _tcpSession is { IsConnected: true } && _death.TryRequestRespawn();

    private async Task<OpStatus> SendRespawnRequestAsync()
    {
        if (_tcpSession is not TcpSession tcp)
        {
            return OpStatus.Invalid;
        }

        RequestOptions options = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);
        using PacketScope<RespawnRequest> lease = PacketFactory<RespawnRequest>.Acquire();
        using MutationResult reply = await tcp.RequestAsync<MutationResult>(lease.Value, options, MutationReplies.Respawn, CancellationToken.None).ConfigureAwait(false);
        return reply.Status;
    }
}
