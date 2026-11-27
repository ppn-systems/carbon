// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration.Catalog;
using Carbon.Maps.Generators;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// Gameplay config received from the server (ADR-014). The client never ships or hardcodes gameplay tables:
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

    /// <summary>
    /// Builds a local world generator for <paramref name="seed"/> with the synced building catalog (villages match the
    /// server's), or returns <see langword="null"/> before the first sync. Used for views of the world outside play, such
    /// as the menu backdrop.
    /// </summary>
    /// <param name="seed">World seed.</param>
    /// <returns>The generator, or <see langword="null"/> when no catalog has been synced yet.</returns>
    ProceduralWorldGenerator? TryCreateWorldGenerator(ulong seed);
}
