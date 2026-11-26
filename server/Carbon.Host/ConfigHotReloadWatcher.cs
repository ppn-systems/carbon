// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading;
using Carbon.Server.Configuration;
using Microsoft.Extensions.Logging;

namespace Carbon.Host;

/// <summary>
/// Watches the config directory and reloads <see cref="ConfigRepository"/> shortly after the last change
/// (debounced, so an editor's save burst causes one reload). An invalid edit is logged and the live config is kept.
/// </summary>
internal sealed class ConfigHotReloadWatcher : IDisposable
{
    private static readonly TimeSpan s_debounce = TimeSpan.FromMilliseconds(500);

    private readonly ConfigRepository _config;
    private readonly ILogger _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    /// <summary>Initializes a new instance of the <see cref="ConfigHotReloadWatcher"/> class and starts watching.</summary>
    public ConfigHotReloadWatcher(ConfigRepository config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timer = new Timer(_ => this.ReloadNow(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(config.Directory, ConfigRepository.SearchPattern)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += this.OnFileEvent;
        _watcher.Created += this.OnFileEvent;
        _watcher.Deleted += this.OnFileEvent;
        _watcher.Renamed += this.OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _ = _timer.Change(s_debounce, Timeout.InfiniteTimeSpan);

    private void ReloadNow()
    {
        try
        {
            _ = _config.Reload();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Config hot reload rejected; keeping manifest v{Version}.", _config.ManifestVersion);
        }
    }
}
