// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.Session;

/// <summary>
/// Shared read-only transport and connection status contract inherited by both
/// authentication (<see cref="Identity.IAuthenticationGateway"/>) and world query (<see cref="World.IWorldReadModel"/>) ports.
/// </summary>
public interface IConnectionStatus
{
    /// <summary>Gets a value indicating whether the reliable TCP control channel is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Gets a value indicating whether the authenticated UDP fast channel is connected.</summary>
    bool IsUdpActive { get; }

    /// <summary>Gets the active transport label (<c>TCP+UDP</c>, <c>TCP</c>, <c>RECONNECTING</c>, or <c>OFFLINE</c>).</summary>
    string TransportLabel { get; }
}
