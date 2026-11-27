// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Combat.Statuses.Config;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Server-authoritative gameplay config sync (ADR-014), delegated to <see cref="SessionConfigSync"/>. A verified catalog
/// is applied to the session's synced catalogs before it is published through <see cref="GameConfig"/>.
/// </summary>
public sealed partial class MultiplayerSession
{
    private readonly SessionConfigSync _config;

    /// <inheritdoc/>
    public event Action<ConfigCatalog>? GameConfigChanged
    {
        add => _config.Changed += value;
        remove => _config.Changed -= value;
    }

    /// <inheritdoc/>
    public ConfigCatalog? GameConfig => _config.GameConfig;

    /// <inheritdoc/>
    public SyncedItemCatalog Items => _items;

    /// <inheritdoc/>
    public SyncedRaceCatalog Races => _races;

    /// <inheritdoc/>
    public Task<ConfigCatalog> WaitForGameConfigAsync(CancellationToken ct = default) => _config.WaitForGameConfigAsync(ct);

    private SessionConfigSync CreateConfigSync() =>
        new(_options.ConfigCacheDirectory, (version, names) => SessionConfigSync.OpenTcpStream(_tcpSession, version, names), this.ApplyConfigCatalog);

    internal void ApplyConfigCatalog(ConfigCatalog catalog)
    {
        _ = _items.Apply(catalog);
        _ = _entityNames.Apply(catalog);
        _ = SyncedFoodCatalog.Apply(catalog);
        _ = SyncedProgressionConfig.Apply(catalog);
        if (SyncedStatusEffectCatalog.TryParse(catalog, out StatusEffectCatalog statusEffects))
        {
            _statusEffectDefinitions = statusEffects;
        }
        _ = _stamina.Config.Apply(catalog);
        _ = _races.Apply(catalog);
        _ = _structures.Apply(catalog);
        if (_waterRules.Apply(catalog) && _chunkWorld is not null)
        {
            _chunkWorld.WaterRules = _waterRules.Current;
        }
    }
}
