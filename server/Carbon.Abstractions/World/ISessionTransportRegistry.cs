// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking;

namespace Carbon.Abstractions.World;

/// <summary>
/// Resolves the canonical stream session connection identifier for packets arriving over either
/// a reliable stream transport (TCP/WebSocket) or a paired authenticated UDP datagram session.
/// </summary>
public interface ISessionTransportRegistry
{
    /// <summary>
    /// Returns the paired <c>StreamConnectionId</c> when <paramref name="connection"/> is a datagram session;
    /// otherwise returns <see cref="IConnection.ConnectionId"/>.
    /// </summary>
    ulong ResolveLogicalConnectionId(IConnection connection);

    /// <summary>
    /// Disconnects any active paired datagram session bound to <paramref name="streamConnectionId"/>.
    /// </summary>
    void DisconnectPairedDatagram(ulong streamConnectionId);
}
