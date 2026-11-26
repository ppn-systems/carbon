// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Role-segregated network connection and account authentication port (ISP).
/// Consumed exclusively by authentication views and lifecycle orchestrators.
/// </summary>
public interface IClientAuthService : IClientConnectionStatus
{
    /// <summary>Gets a value indicating whether the session has authenticated an account.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Connects over encrypted TCP (X25519 AEAD handshake) and authenticates an existing account.</summary>
    Task<ClientAuthResult> LoginAsync(string host, ushort tcpPort, string username, string password, CancellationToken ct = default);

    /// <summary>Connects over encrypted TCP (X25519 AEAD handshake) and registers a new account.</summary>
    Task<ClientAuthResult> RegisterAsync(string host, ushort tcpPort, string username, string password, CancellationToken ct = default);

    /// <summary>Leaves the active world, closes Authenticated UDP, and logs out the authenticated session.</summary>
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>Connects, authenticates, creates/selects the configured character, and enters the world.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}
