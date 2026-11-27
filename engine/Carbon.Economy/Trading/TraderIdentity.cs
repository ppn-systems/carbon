// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Trading;

/// <summary>A trader in the world: its stable key (seed of its stock and restock jitter), profile and village style.</summary>
/// <param name="Key">Stable key derived by the server from the village site and role.</param>
/// <param name="ProfileId">Profile id in <see cref="TraderCatalog"/>.</param>
/// <param name="Style">Village style index (<see cref="TraderCatalog.StyleNames"/>).</param>
public readonly record struct TraderIdentity(ulong Key, string ProfileId, byte Style);
