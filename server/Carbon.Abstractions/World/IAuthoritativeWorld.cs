// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Contracts.Common;
using Carbon.Contracts.Maps.S2C;
using Carbon.Items.Containers;
using Carbon.Maps.Building;

namespace Carbon.Abstractions.World;

/// <summary>
/// Result of attempting to enter the authoritative world simulation.
/// </summary>
/// <param name="Status">Outcome status.</param>
/// <param name="PlayerNetworkId">Assigned player entity network ID.</param>
/// <param name="WorldSeed">Deterministic world seed.</param>
/// <param name="GeneratorVersion">World generator version.</param>
/// <param name="TileSize">Tile size in world units.</param>
/// <param name="Frequency">Macro terrain frequency.</param>
/// <param name="PositionX">Spawned world X coordinate.</param>
/// <param name="PositionY">Spawned world Y coordinate.</param>
/// <param name="ServerTick">Current server tick.</param>
/// <param name="TickRate">Configured simulation tick rate.</param>
public readonly record struct EnterWorldResult(
    OpStatus Status,
    ulong PlayerNetworkId,
    ulong WorldSeed,
    ushort GeneratorVersion,
    float TileSize,
    float Frequency,
    float PositionX,
    float PositionY,
    ulong ServerTick,
    ushort TickRate);

/// <summary>
/// Outcome of an authoritative world mutation returned to handlers without allocating packet wrappers.
/// </summary>
/// <param name="Status">Operation status.</param>
/// <param name="Revision">Updated chunk revision, or zero.</param>
public readonly record struct WorldMutationOutcome(OpStatus Status, uint Revision);

/// <summary>
/// Thread-safe authoritative game world gateway consumed by packet handlers and background runners.
/// </summary>
public interface IAuthoritativeWorld
{
    /// <summary>Gets the world generation seed.</summary>
    ulong WorldSeed { get; }

    /// <summary>Gets the world generator version.</summary>
    ushort GeneratorVersion { get; }

    /// <summary>Gets the tile size in world units.</summary>
    float TileSize { get; }

    /// <summary>Gets the macro terrain noise frequency.</summary>
    float Frequency { get; }

    /// <summary>Gets the fixed simulation tick rate in Hertz.</summary>
    ushort TickRate { get; }

    /// <summary>Gets the current monotonically increasing authoritative server tick.</summary>
    ulong CurrentTick { get; }

    /// <summary>Gets the default safe starter spawn world position.</summary>
    Vector2 DefaultSpawnWorldPosition { get; }

    /// <summary>
    /// Hydrates persisted chunk deltas from storage into the world coordinator before accepting players.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeFromStoreAsync(CancellationToken ct = default);

    /// <summary>
    /// Spawns the selected character into the authoritative simulation and binds the session to <paramref name="connectionId"/>.
    /// </summary>
    EnterWorldResult TryEnterWorld(ulong connectionId, ulong sessionId, CharacterRecord character);

    /// <summary>
    /// Removes the session bound to <paramref name="connectionId"/> and despawns its entity.
    /// </summary>
    bool LeaveWorld(ulong connectionId);

    /// <summary>
    /// Resolves the active session bound to a transport connection.
    /// </summary>
    bool TryGetSessionByConnection(ulong connectionId, out ConnectedPlayerSession? session);

    /// <summary>
    /// Enqueues a client movement input command for the next fixed simulation tick.
    /// </summary>
    void EnqueueMoveInput(ulong connectionId, uint clientTick, Vector2 direction, bool sprint);

    /// <summary>
    /// Updates the player's selected hotbar slot and harvest tier.
    /// </summary>
    OpStatus TrySelectHotbarSlot(ulong connectionId, byte slotIndex);

    /// <summary>
    /// Attempts to harvest or damage the natural prop on the specified world tile.
    /// </summary>
    WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY);

    /// <summary>
    /// Attempts to construct a structure on the specified world tile.
    /// </summary>
    WorldMutationOutcome TryPlaceStructure(ulong connectionId, int tileX, int tileY, StructureType structureType);

    /// <summary>
    /// Attempts to demolish an existing constructed structure on the specified world tile.
    /// </summary>
    WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY);

    /// <summary>
    /// Attempts to toggle an existing door on the specified world tile.
    /// </summary>
    WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY);

    /// <summary>
    /// Builds a thread-safe wire delta packet snapshot for the requested chunk when its revision exceeds
    /// <paramref name="knownRevision"/>.
    /// </summary>
    bool TryCreateChunkDeltaPacket(int chunkX, int chunkY, uint knownRevision, out MapChunkDelta? packet);

    /// <summary>
    /// Enqueues a client combat swing or consumable action onto the next fixed tick.
    /// </summary>
    OpStatus EnqueueAttackInput(ulong connectionId, ulong targetNetworkId, Vector2 aimWorldPosition, byte abilitySlot);

    /// <summary>
    /// Copies occupied inventory slots for the session bound to <paramref name="connectionId"/>.
    /// </summary>
    bool GetInventorySnapshot(ulong connectionId, List<(ushort SlotIndex, ItemSlot Slot)> output);

    /// <summary>
    /// Validates and executes a crafting recipe request on the player's authoritative inventory.
    /// </summary>
    OpStatus TryCraftRecipe(ulong connectionId, ushort recipeId, ushort quantity);

    /// <summary>
    /// Advances the authoritative simulation by one fixed tick and dispatches AoI snapshots.
    /// </summary>
    void StepFixedTick();

    /// <summary>
    /// Flushes dirty chunk deltas and active player character state to the configured stores.
    /// </summary>
    Task FlushDirtyPersistenceAsync(CancellationToken ct = default);
}
