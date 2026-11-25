// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Items.Containers;
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

    /// <summary>Gets the frame-interpolated local player world position (with prediction smoothing).</summary>
    Vector2 LocalPlayerInterpolatedPosition { get; }

    /// <summary>Gets the local player's current health.</summary>
    float LocalPlayerHealth { get; }

    /// <summary>Gets the local player's maximum health.</summary>
    float LocalPlayerMaxHealth { get; }

    /// <summary>Gets the active zero-based hotbar slot index (0..8).</summary>
    byte SelectedHotbarSlot { get; }

    /// <summary>Gets the local player's active inventory container.</summary>
    InventoryContainer Inventory { get; }

    /// <summary>Gets the normalized direction vector of the most recent swing/attack.</summary>
    Vector2 LastAttackDirection { get; }

    /// <summary>Gets remaining visual swing arc timer in seconds (&gt; 0 while swinging).</summary>
    float AttackSwingTimer { get; }

    /// <summary>Gets the remote players, monsters, and NPCs currently visible in the Area of Interest.</summary>
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
    /// Advances client simulation, prediction reconciliation, chunk streaming, and interpolation for the frame.
    /// </summary>
    /// <param name="dt">Frame elapsed seconds.</param>
    /// <param name="moveDir">Normalized player movement direction.</param>
    /// <param name="sprint">Whether sprint is held.</param>
    void Update(double dt, Vector2 moveDir, bool sprint);

    /// <summary>Selects a hotbar slot (0..8) and updates active tool/weapon harvest tier.</summary>
    void SelectHotbarSlot(byte slotIndex);

    /// <summary>Executes primary attack or consumable action aimed toward <paramref name="aimWorldPosition"/>.</summary>
    void TriggerPrimaryAction(Vector2 aimWorldPosition);

    /// <summary>Attempts to craft one instance of <paramref name="recipeId"/>.</summary>
    bool TryCraftRecipe(ushort recipeId);

    /// <summary>Drains newly buffered floating combat/loot events into <paramref name="destination"/>.</summary>
    void DrainCombatEvents(List<ClientCombatEvent> destination);

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
