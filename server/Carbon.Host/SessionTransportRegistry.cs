// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Carbon.Abstractions.World;
using Nalix.Abstractions.Networking;
using Nalix.Framework.Injection;
using Nalix.Network.Connections;

namespace Carbon.Host;

/// <summary>
/// Tracks live mappings between stream connections (TCP/WebSocket) and their paired
/// authenticated UDP <see cref="DatagramConnection"/> sessions so handlers and world pushes
/// share one canonical logical session identifier.
/// </summary>
internal sealed class SessionTransportRegistry : ISessionTransportRegistry
{
    private readonly ConcurrentDictionary<ulong, DatagramConnection> _datagramByStreamId = new();
    private readonly ConcurrentDictionary<ulong, ulong> _streamByDatagramId = new();
    private volatile IConnectionHub? _hub;
    private Action<ulong>? _onStreamClosed;
    private int _hubSubscribed;

    /// <summary>Configures the callback invoked when a logical stream connection closes.</summary>
    public void ConfigureStreamClosedCallback(Action<ulong> onStreamClosed) =>
        _onStreamClosed = onStreamClosed ?? throw new ArgumentNullException(nameof(onStreamClosed));

    /// <summary>Binds the active connection hub.</summary>
    public void AttachHub(IConnectionHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
        if (Interlocked.Exchange(ref _hubSubscribed, 1) == 0)
        {
            hub.ConnectionUnregistered += this.OnConnectionUnregistered;
        }
    }

    /// <summary>Lazily resolves the live connection hub created during <c>ActivateAsync</c>.</summary>
    internal IConnectionHub? ResolveHub()
    {
        IConnectionHub? hub = _hub;
        if (hub is not null)
        {
            return hub;
        }

        hub = InstanceManager.Instance.GetExistingInstance<IConnectionHub>();
        if (hub is not null)
        {
            this.AttachHub(hub);
        }

        return hub;
    }

    /// <inheritdoc/>
    public ulong ResolveLogicalConnectionId(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _ = this.ResolveHub();

        if (connection is DatagramConnection datagram)
        {
            _datagramByStreamId[datagram.StreamConnectionId] = datagram;
            _streamByDatagramId[datagram.ConnectionId] = datagram.StreamConnectionId;
            return datagram.StreamConnectionId;
        }

        return connection.ConnectionId;
    }

    /// <summary>
    /// Attempts to find an active authenticated datagram connection associated with <paramref name="streamConnectionId"/>.
    /// </summary>
    public bool TryGetDatagramConnection(ulong streamConnectionId, out DatagramConnection? datagramConnection)
    {
        if (_datagramByStreamId.TryGetValue(streamConnectionId, out DatagramConnection? cached))
        {
            if (!cached.IsDisposed)
            {
                datagramConnection = cached;
                return true;
            }

            _ = _datagramByStreamId.TryRemove(streamConnectionId, out _);
            _ = _streamByDatagramId.TryRemove(cached.ConnectionId, out _);
        }

        datagramConnection = null;
        return false;
    }

    /// <inheritdoc/>
    public void DisconnectPairedDatagram(ulong streamConnectionId)
    {
        if (_datagramByStreamId.TryGetValue(streamConnectionId, out DatagramConnection? cached))
        {
            _ = _datagramByStreamId.TryRemove(streamConnectionId, out _);
            _ = _streamByDatagramId.TryRemove(cached.ConnectionId, out _);
            if (!cached.IsDisposed)
            {
                cached.Disconnect("stream_closed");
            }
        }

        IConnectionHub? hub = this.ResolveHub();
        if (hub is null)
        {
            return;
        }

        foreach (IConnection candidate in hub.ListConnections())
        {
            if (candidate is DatagramConnection dg && dg.StreamConnectionId == streamConnectionId)
            {
                _ = _datagramByStreamId.TryRemove(streamConnectionId, out _);
                _ = _streamByDatagramId.TryRemove(dg.ConnectionId, out _);
                if (!dg.IsDisposed)
                {
                    dg.Disconnect("stream_closed");
                }
            }
        }
    }

    /// <summary>
    /// Handles connection teardown notifications from <see cref="IConnectionHub.ConnectionUnregistered"/>.
    /// Returns <see langword="true"/> when the closed connection was a stream session whose world player
    /// should be cleaned up; returns <see langword="false"/> when only the UDP channel closed.
    /// </summary>
    public bool HandleConnectionUnregistered(IConnection closedConnection)
    {
        ArgumentNullException.ThrowIfNull(closedConnection);

        if (closedConnection is DatagramConnection dg)
        {
            _ = _streamByDatagramId.TryRemove(dg.ConnectionId, out _);
            if (_datagramByStreamId.TryGetValue(dg.StreamConnectionId, out DatagramConnection? mapped) &&
                ReferenceEquals(mapped, dg))
            {
                _ = _datagramByStreamId.TryRemove(dg.StreamConnectionId, out _);
            }

            return false;
        }

        this.DisconnectPairedDatagram(closedConnection.ConnectionId);
        return true;
    }

    private void OnConnectionUnregistered(IConnection closedConnection)
    {
        if (this.HandleConnectionUnregistered(closedConnection))
        {
            _onStreamClosed?.Invoke(closedConnection.ConnectionId);
        }
    }
}
