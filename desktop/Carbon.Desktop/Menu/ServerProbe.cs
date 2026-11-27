// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;

namespace Carbon.Desktop.Menu;

/// <summary>Checks whether a game server answers, and how quickly.</summary>
internal interface IServerProbe
{
    /// <summary>Returns how long connecting to <paramref name="server"/> took, or <see langword="null"/> when it did not answer.</summary>
    Task<TimeSpan?> ProbeAsync(GameServer server, CancellationToken ct);
}

/// <summary>
/// Times a plain TCP connect to the server's port and closes it at once: no handshake, nothing sent. The Server screen
/// checks each listed server once per visit (never on a timer), so the check stays far below any connection guard.
/// </summary>
internal sealed class TcpServerProbe : IServerProbe
{
    /// <summary>How long a check waits before calling the server unreachable.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    public async Task<TimeSpan?> ProbeAsync(GameServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
        long start = Stopwatch.GetTimestamp();
        try
        {
            await socket.ConnectAsync(server.Host, server.Port, timeout.Token).ConfigureAwait(false);
            return Stopwatch.GetElapsedTime(start);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }
}
