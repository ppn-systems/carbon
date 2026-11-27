// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
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
/// Opens the server's document stream for one config request, or returns <see langword="null"/> when there is no
/// connected transport to ask (the sync then waits for the next connection's manifest).
/// </summary>
/// <param name="manifestVersion">The manifest version the requested documents belong to.</param>
/// <param name="names">The documents to download.</param>
internal delegate IAsyncEnumerable<ConfigDocument>? ConfigDocumentStreamFactory(uint manifestVersion, string[] names);

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
    private readonly ConfigDocumentStreamFactory _openDocumentStream;
    private readonly Action<ConfigCatalog> _applyCatalog;
    private readonly Lock _configGate = new();
    private ConfigManifest? _pendingManifest;
    private TaskCompletionSource<ConfigCatalog> _configReady = NewConfigReadySource();
    private volatile ConfigCatalog? _gameConfig;
    private int _configPumpRunning;

    /// <summary>Initializes a new instance of the <see cref="SessionConfigSync"/> class.</summary>
    /// <param name="cacheDirectory">Directory of the on-disk config document cache.</param>
    /// <param name="openDocumentStream">Opens a document download; <see cref="OpenTcpStream"/> in production.</param>
    /// <param name="applyCatalog">Applies a verified catalog to the session's synced catalogs before it is published.</param>
    public SessionConfigSync(string cacheDirectory, ConfigDocumentStreamFactory openDocumentStream, Action<ConfigCatalog> applyCatalog)
    {
        _openDocumentStream = openDocumentStream;
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
                    if (_openDocumentStream(_configSync.ManifestVersion, names) is not { } stream)
                    {
                        return;
                    }

                    names = await this.DownloadConfigDocumentsAsync(stream).ConfigureAwait(false);
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

    /// <summary>The production <see cref="ConfigDocumentStreamFactory"/>: streams the documents over a connected TCP session.</summary>
    public static IAsyncEnumerable<ConfigDocument>? OpenTcpStream(TcpSession? tcp, uint manifestVersion, string[] names) =>
        tcp is { IsConnected: true } connected ? StreamOverTcpAsync(connected, manifestVersion, names) : null;

    private static async IAsyncEnumerable<ConfigDocument> StreamOverTcpAsync(TcpSession tcp, uint manifestVersion, string[] names)
    {
        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(ConfigSyncTimeoutMs).WithEncrypt(tcp.State.EncryptionEnabled);
        using PacketScope<ConfigRequest> lease = PacketFactory<ConfigRequest>.Acquire();
        lease.Value.ManifestVersion = manifestVersion;
        lease.Value.Names = names;
        await foreach (ConfigDocument chunk in tcp.StreamAsync<ConfigDocument>(lease.Value, reqOpts, CancellationToken.None).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private async Task<string[]> DownloadConfigDocumentsAsync(IAsyncEnumerable<ConfigDocument> stream)
    {
        string[] retry = [];
        bool sawEndOfStream = false;
        await foreach (ConfigDocument chunk in stream.ConfigureAwait(false))
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
