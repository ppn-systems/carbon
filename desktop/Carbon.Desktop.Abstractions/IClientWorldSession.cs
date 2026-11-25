// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Transport-agnostic client world session contract shared between the single-player offline sandbox
/// and the live Nalix multiplayer client. Kept free of OpenGL/window dependencies so logic is unit-testable.
/// </summary>
public interface IClientWorldSession : IAsyncDisposable
{
    /// <summary>Gets the chunk world coordinator backing terrain, props, and structures.</summary>
    ChunkWorld2D ChunkWorld { get; }

    /// <summary>Gets the client-side fixed-tick simulation engine.</summary>
    WorldSimulation Simulation { get; }

    /// <summary>Gets the local player's ECS entity handle.</summary>
    Entity LocalPlayer { get; }

    /// <summary>Gets the frame-interpolated local player world position.</summary>
    Vector2 LocalPlayerInterpolatedPosition { get; }

    /// <summary>Gets the remote entities currently visible in the player's Area of Interest.</summary>
    IReadOnlyList<RemoteEntityView> RemoteEntities { get; }

    /// <summary>Gets a value indicating whether the session is connected to a live remote server.</summary>
    bool IsOnlineMultiplayer { get; }

    /// <summary>Gets a value indicating whether the world and local avatar are initialized.</summary>
    bool IsReady { get; }

    /// <summary>Gets the active world seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>
    /// Initializes the world generator, authenticates if online, and spawns the local avatar.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Advances client simulation, chunk streaming, and network reconciliation for the frame.
    /// </summary>
    /// <param name="dt">Frame elapsed seconds.</param>
    /// <param name="moveDir">Normalized player movement direction.</param>
    /// <param name="sprint">Whether sprint is held.</param>
    void Update(double dt, Vector2 moveDir, bool sprint);

    /// <summary>
    /// Attempts to harvest or damage the nearest natural prop beside the local player.
    /// </summary>
    /// <param name="modifiedChunk">Chunk coordinate modified if successful.</param>
    /// <returns><see langword="true"/> if a prop was struck; otherwise <see langword="false"/>.</returns>
    bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk);

    /// <summary>
    /// Places a starter test house near the player's current position.
    /// </summary>
    /// <param name="centerChunk">Center chunk coordinate containing the house.</param>
    /// <returns><see langword="true"/> if placement succeeded or was dispatched.</returns>
    bool TryPlaceTestHouse(out (int ChunkX, int ChunkY) centerChunk);

    /// <summary>
    /// Toggles the nearest door beside the local player.
    /// </summary>
    /// <param name="modifiedChunk">Chunk coordinate containing the toggled door.</param>
    /// <returns><see langword="true"/> if a door was toggled.</returns>
    bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk);

    /// <summary>
    /// Regenerates the offline sandbox with a new seed (no-op in online mode).
    /// </summary>
    /// <param name="newSeed">Replacement seed.</param>
    void ReseedOffline(ulong newSeed);
}
