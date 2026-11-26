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

    /// <summary>Gets a value indicating whether the reliable TCP control channel is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Gets a value indicating whether the session has authenticated an account.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Gets a value indicating whether the authenticated UDP (NDP) fast channel is connected.</summary>
    bool IsUdpActive { get; }

    /// <summary>Gets a value indicating whether the world and local avatar are initialized.</summary>
    bool IsReady { get; }

    /// <summary>Gets the active signed-in character display name.</summary>
    string ActiveCharacterName { get; }

    /// <summary>Gets the active transport label (<c>TCP+UDP</c>, <c>TCP</c>, or <c>OFFLINE</c>).</summary>
    string TransportLabel { get; }

    /// <summary>Gets the active world seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>
    /// Connects over TCP (with X25519 AEAD handshake) and authenticates an existing account.
    /// </summary>
    Task<ClientAuthResult> LoginAsync(string host, ushort tcpPort, string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Connects over TCP (with X25519 AEAD handshake) and registers a new account.
    /// </summary>
    Task<ClientAuthResult> RegisterAsync(string host, ushort tcpPort, string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Queries all characters owned by the currently authenticated account.
    /// </summary>
    Task<IReadOnlyList<ClientCharacterSummary>> ListCharactersAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new character on the currently authenticated account.
    /// </summary>
    Task<(bool Succeeded, string Message)> CreateCharacterAsync(string characterName, byte appearancePreset, CancellationToken ct = default);

    /// <summary>
    /// Selects a character, enters the authoritative world, syncs inventory/chunks, and opens Authenticated UDP.
    /// </summary>
    Task<(bool Succeeded, string Message)> EnterWorldAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Leaves the active world, closes Authenticated UDP, and logs out the authenticated session.
    /// </summary>
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Connects, signs in (auto-registering if missing), creates/selects the configured character, and enters the world.
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

    /// <summary>
    /// Executes primary attack, structure placement, tool harvest, or consumable action aimed toward <paramref name="aimWorldPosition"/>.
    /// </summary>
    /// <param name="aimWorldPosition">World-space aim coordinate.</param>
    /// <param name="invalidatedChunk">Chunk coordinate whose terrain, prop, or structure visuals changed, if any.</param>
    /// <returns><see langword="true"/> if chunk visuals should be invalidated; otherwise <see langword="false"/>.</returns>
    bool TriggerPrimaryAction(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) invalidatedChunk);

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
}
