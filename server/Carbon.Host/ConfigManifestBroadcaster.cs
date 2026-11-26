// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Config.S2C;
using Carbon.Handlers.Session;
using Carbon.Server.Configuration;
using Microsoft.Extensions.Logging;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Security;
using Nalix.Network.Connections;
using Nalix.Runtime.Extensions;

namespace Carbon.Host;

/// <summary>
/// Pushes the new config manifest to every signed-in stream connection after a reload changes it, so clients
/// re-download exactly the documents whose hash changed (ADR-014 D7).
/// </summary>
internal sealed class ConfigManifestBroadcaster
{
    private readonly ConfigRepository _config;
    private readonly SessionTransportRegistry _transportRegistry;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="ConfigManifestBroadcaster"/> class.</summary>
    public ConfigManifestBroadcaster(ConfigRepository config, SessionTransportRegistry transportRegistry, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _transportRegistry = transportRegistry ?? throw new ArgumentNullException(nameof(transportRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Handles <see cref="ConfigRepository.ManifestChanged"/>.</summary>
    /// <param name="result">Reload outcome.</param>
    public void OnManifestChanged(ConfigReloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Config manifest v{Version}: changed [{Changed}], removed [{Removed}]; pushing to signed-in clients.",
                result.ManifestVersion,
                string.Join(", ", result.Changed),
                string.Join(", ", result.Removed));
        }

        IConnectionHub? hub = _transportRegistry.ResolveHub();
        if (hub is null)
        {
            return;
        }

        foreach (IConnection connection in hub.ListConnections())
        {
            if (connection is DatagramConnection
                || connection.Level < PermissionLevel.USER
                || !ConnectionAuthRegistry.TryGetAccountId(connection.ConnectionId, out _))
            {
                continue;
            }

            _ = this.PushAsync(connection);
        }
    }

    private async Task PushAsync(IConnection connection)
    {
        try
        {
            using ConfigManifest manifest = _config.CreateManifest();
            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(manifest, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Config manifest push to connection {ConnectionId} failed.", connection.ConnectionId);
        }
    }
}
