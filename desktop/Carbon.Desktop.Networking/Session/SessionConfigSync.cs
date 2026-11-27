// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration.Caching;
using Carbon.Configuration.Catalog;
using Carbon.Configuration.Sync;
using Carbon.Contracts.Config.C2S;
using Carbon.Contracts.Config.S2C;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Server-authoritative gameplay config sync (ADR-014). The server pushes a <see cref="ConfigManifest"/> after sign-in and
/// after every hot reload; this client downloads only documents whose hash differs from the on-disk cache, verifies them,
/// and exposes them through <see cref="GameConfig"/>. One pump runs at a time; a manifest that arrives mid-download
/// supersedes the one being processed.
/// </summary>
internal sealed class SessionConfigSync
{
    /// <summary>Timeout for one config download request, and for waiting on the sync before entering the world.</summary>
    internal const int ConfigSyncTimeoutMs = 15_000;

    private readonly ConfigSyncClient _configSync;
    private readonly Func<TcpSession?> _currentTcp;
    private readonly Action<ConfigCatalog> _applyCatalog;
    private readonly Lock _configGate = new();
    private ConfigManifest? _pendingManifest;
    private TaskCompletionSource<ConfigCatalog> _configReady = NewConfigReadySource();
    private volatile ConfigCatalog? _gameConfig;
    private int _configPumpRunning;

    /// <summary>Initializes a new instance of the <see cref="SessionConfigSync"/> class.</summary>
    /// <param name="cacheDirectory">Directory of the on-disk config document cache.</param>
    /// <param name="currentTcp">Returns the session's current TCP transport, or <see langword="null"/> when there is none.</param>
    /// <param name="applyCatalog">Applies a verified catalog to the session's synced catalogs before it is published.</param>
    public SessionConfigSync(string cacheDirectory, Func<TcpSession?> currentTcp, Action<ConfigCatalog> applyCatalog)
    {
        _currentTcp = currentTcp;
        _applyCatalog = applyCatalog;
        _configSync = new ConfigSyncClient(new ConfigDocumentStore(new FileSystemConfigCacheStorage(cacheDirectory)));
        _configSync.Ready += this.OnConfigReady;
        _configSync.Faulted += this.OnConfigFaulted;
    }

    /// <summary>Raised after a new catalog has been applied and published.</summary>
    public event Action<ConfigCatalog>? Changed;

    /// <summary>Gets the last verified catalog, or <see langword="null"/> before the first sync completes.</summary>
    public ConfigCatalog? GameConfig => _gameConfig;

    /// <summary>Completes when the current connection's config has been synced.</summary>
    public Task<ConfigCatalog> WaitForGameConfigAsync(CancellationToken ct)
    {
        Task<ConfigCatalog> ready;
        lock (_configGate)
        {
            ready = _configReady.Task;
        }

        return ready.WaitAsync(ct);
    }

    /// <summary>A new connection must sync against its own server's manifest before entering the world.</summary>
    public void ResetForNewConnection()
    {
        lock (_configGate)
        {
            if (_configReady.Task.IsCompleted)
            {
                _configReady = NewConfigReadySource();
            }

            _pendingManifest?.Dispose();
            _pendingManifest = null;
        }
    }

    /// <summary>Handles a server manifest push: copies it and starts (or supersedes) the download pump.</summary>
    public void OnConfigManifest(ConfigManifest pkt)
    {
        // Copy: the transport may recycle the packet after this callback returns.
        ConfigManifest copy = new()
        {
            ManifestVersion = pkt.ManifestVersion,
            Names = [.. pkt.Names ?? []],
            ContentHashes = [.. pkt.ContentHashes ?? []],
            ByteLengths = [.. pkt.ByteLengths ?? []],
            Versions = [.. pkt.Versions ?? []],
        };

        lock (_configGate)
        {
            _pendingManifest?.Dispose();
            _pendingManifest = copy;
        }

        _ = this.PumpConfigSyncAsync();
    }

    /// <summary>Drops a manifest that has not been processed yet.</summary>
    public void DiscardPendingManifest()
    {
        lock (_configGate)
        {
            _pendingManifest?.Dispose();
            _pendingManifest = null;
        }
    }

    private static TaskCompletionSource<ConfigCatalog> NewConfigReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task PumpConfigSyncAsync()
    {
        if (Interlocked.Exchange(ref _configPumpRunning, 1) != 0)
        {
            return;
        }

        try
        {
            while (this.TakePendingManifest() is ConfigManifest manifest)
            {
                string[] names;
                using (manifest)
                {
                    names = _configSync.HandleManifest(manifest);
                }

                while (names.Length > 0 && !this.HasPendingManifest())
                {
                    if (_currentTcp() is not { IsConnected: true } tcp)
                    {
                        return;
                    }

                    names = await this.DownloadConfigDocumentsAsync(tcp, names).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            this.OnConfigFaulted($"Config download failed: {ex.Message}");
        }
        finally
        {
            _ = Interlocked.Exchange(ref _configPumpRunning, 0);
            if (this.HasPendingManifest())
            {
                _ = this.PumpConfigSyncAsync();
            }
        }
    }

    private async Task<string[]> DownloadConfigDocumentsAsync(TcpSession tcp, string[] names)
    {
        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(ConfigSyncTimeoutMs).WithEncrypt(tcp.State.EncryptionEnabled);
        using PacketScope<ConfigRequest> lease = PacketFactory<ConfigRequest>.Acquire();
        lease.Value.ManifestVersion = _configSync.ManifestVersion;
        lease.Value.Names = names;

        string[] retry = [];
        bool sawEndOfStream = false;
        await foreach (ConfigDocument chunk in tcp.StreamAsync<ConfigDocument>(lease.Value, reqOpts, CancellationToken.None).ConfigureAwait(false))
        {
            sawEndOfStream |= chunk.IsEndOfStream;
            string[] again = _configSync.HandleDocument(chunk);
            if (again.Length > 0)
            {
                retry = again;
            }
        }

        if (!sawEndOfStream && _configSync.State == ConfigSyncState.Downloading)
        {
            // The transport completed the stream without surfacing the terminator; close it ourselves.
            using ConfigDocument end = new() { IsEndOfStream = true, IsTerminator = true };
            retry = _configSync.HandleDocument(end);
        }

        return retry;
    }

    private ConfigManifest? TakePendingManifest()
    {
        lock (_configGate)
        {
            ConfigManifest? manifest = _pendingManifest;
            _pendingManifest = null;
            return manifest;
        }
    }

    private bool HasPendingManifest()
    {
        lock (_configGate)
        {
            return _pendingManifest is not null;
        }
    }

    private void OnConfigReady(ConfigCatalog catalog)
    {
        _applyCatalog(catalog);
        _gameConfig = catalog;
        lock (_configGate)
        {
            if (!_configReady.TrySetResult(catalog))
            {
                _configReady = NewConfigReadySource();
                _ = _configReady.TrySetResult(catalog);
            }
        }

        Changed?.Invoke(catalog);
    }

    private void OnConfigFaulted(string reason)
    {
        lock (_configGate)
        {
            _ = _configReady.TrySetException(new InvalidOperationException(reason));
        }
    }
}
