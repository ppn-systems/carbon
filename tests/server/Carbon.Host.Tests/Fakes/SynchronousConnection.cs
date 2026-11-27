// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Protocols;
using Nalix.Abstractions.Primitives;
using Nalix.Abstractions.Security;

namespace Carbon.Host.Tests.Fakes;

/// <summary>
/// A stream connection whose sends complete synchronously and are only counted: lets the tick allocation guard run the
/// real <c>WorldOutboundSink</c> (audit round 3, ALLOC-1) without a socket. Written by hand rather than with NSubstitute,
/// whose interception allocates on every call and would be measured too. Members the send path does not use throw, so a
/// change in what it uses shows up as a logged send failure rather than silently.
/// </summary>
internal sealed class SynchronousConnection(ulong connectionId) : IConnection, IConnection.ITransport
{
    private uint _sendSequence;

    /// <summary>Gets the number of frames sent.</summary>
    public long Frames { get; private set; }

    /// <summary>Gets the number of bytes sent.</summary>
    public long Bytes { get; private set; }

    public ulong ConnectionId { get; } = connectionId;

    public bool IsDisposed => false;

    public string? UserId { get; set; }

    public long UpTime => 0;

    public long LastPingTime => 0;

    public bool ExcludeFromIdleTimeout { get; set; }

    public CipherSuiteType Algorithm { get; set; } = CipherSuiteType.None;

    public Bytes32 Secret { get; set; }

    public PermissionLevel Level { get; set; }

    public IConnection.ITransport Transport => this;

    public int ErrorCount => 0;

    public IOpCodeExtractor PacketClassifier => throw Unused();

    public INetworkEndpoint NetworkEndpoint => throw Unused();

    public IObjectMap<AttributeKey, object> Attributes => throw Unused();

    public ConcurrentDictionary<ushort, object> RateLimitCache => throw Unused();

    public TransportFraming Framing => default;

    public ISequenceCounter SendSequence => throw Unused();

    public ISequenceCounter ReceiveSequence => throw Unused();

    public uint CurrentSendSequence => _sendSequence;

    public uint CurrentReceiveSequence => 0;

    public event EventHandler<IConnectionEventArgs>? ConnectionClosed
    {
        add { }
        remove { }
    }

    public event EventHandler<IConnectionEventArgs>? MessageProcessed
    {
        add { }
        remove { }
    }

    public event EventHandler<IConnectionEventArgs>? MessageProcessing
    {
        add { }
        remove { }
    }

    public void Send(ReadOnlySpan<byte> message) => this.Count(message.Length);

    public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        this.Count(message.Length);
        return ValueTask.CompletedTask;
    }

    public uint NextSendSequence() => ++_sendSequence;

    public uint NextReceiveSequence() => throw Unused();

    public void BeginReceive(CancellationToken cancellationToken) => throw Unused();

    public void UseFraming(TransportFraming framing) => throw Unused();

    public void Disconnect(string? reason = null) => throw Unused();

    public void UpdateIdleTimeout(int newTimeoutMs) => throw Unused();

    public void IncrementErrorCount() => throw Unused();

    public void Dispose()
    {
    }

    private static NotSupportedException Unused() => new("The tick allocation guard's connection does not support this member.");

    private void Count(int bytes)
    {
        this.Frames++;
        this.Bytes += bytes;
    }
}

/// <summary>A hub over <see cref="SynchronousConnection"/>s, keyed by connection id.</summary>
internal sealed class SynchronousConnectionHub : IConnectionHub
{
    private readonly Dictionary<ulong, IConnection> _connections = [];

    public int Count => _connections.Count;

    public event Action<IConnection>? ConnectionUnregistered
    {
        add { }
        remove { }
    }

    public IConnection? GetConnection(ulong id) => _connections.GetValueOrDefault(id);

    public IConnection? GetConnection(ReadOnlySpan<byte> id) => throw new NotSupportedException();

    public void RegisterConnection(IConnection connection) => _connections[connection.ConnectionId] = connection;

    public void UnregisterConnection(IConnection connection) => _ = _connections.Remove(connection.ConnectionId);

    public IReadOnlyCollection<IConnection> ListConnections() => [.. _connections.Values];

    public IReadOnlyCollection<IConnection> ListConnections(INetworkEndpoint endpoint) => throw new NotSupportedException();

    public string GenerateReport() => throw new NotSupportedException();

    public void WriteReportData(System.Text.Json.Utf8JsonWriter writer) => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
