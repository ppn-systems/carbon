// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.Persistence;

namespace Carbon.Abstractions.World;

/// <summary>
/// Role-segregated session entry, teardown, and pending character snapshot port consumed by identity handlers (ISP).
/// </summary>
public interface IWorldSessionGateway
{
    /// <summary>Gets the world generation seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>Gets the world generator version.</summary>
    ushort GeneratorVersion { get; }

    /// <summary>Gets the tile size in world units.</summary>
    float TileSize { get; }

    /// <summary>Gets the macro terrain noise frequency.</summary>
    float Frequency { get; }

    /// <summary>Gets the default safe starter spawn world position.</summary>
    Vector2 DefaultSpawnWorldPosition { get; }

    /// <summary>Spawns the selected character into the authoritative simulation and binds the session to <paramref name="connectionId"/>.</summary>
    EnterWorldResult TryEnterWorld(ulong connectionId, ulong sessionId, CharacterRecord character);

    /// <summary>Removes the session bound to <paramref name="connectionId"/> and despawns its entity.</summary>
    bool LeaveWorld(ulong connectionId);

    /// <summary>Resolves the active session bound to a transport connection.</summary>
    bool TryGetSessionByConnection(ulong connectionId, out ConnectedPlayerSession? session);

    /// <summary>
    /// Retrieves the latest unflushed in-memory <see cref="CharacterRecord"/> for <paramref name="characterId"/>
    /// (active session or pending dirty cache) to prevent stale DB reads during write-behind flush windows.
    /// </summary>
    bool TryGetPendingCharacterSnapshot(Guid characterId, out CharacterRecord? character);
}
