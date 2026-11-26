// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration.Catalog;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// Gameplay config received from the server (ADR-011). The client never ships or hardcodes gameplay tables:
/// domain code reads its document (<c>items</c>, <c>monsters</c>, ...) from <see cref="GameConfig"/>.
/// </summary>
public interface IGameConfigSource
{
    /// <summary>Gets the last fully verified config, or <see langword="null"/> before the first sync completes.</summary>
    ConfigCatalog? GameConfig { get; }

    /// <summary>Raised on the network thread whenever a sync completes with a new verified config.</summary>
    event Action<ConfigCatalog>? GameConfigChanged;

    /// <summary>Waits until the config of the current server connection is verified.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified config.</returns>
    Task<ConfigCatalog> WaitForGameConfigAsync(CancellationToken ct = default);
}
