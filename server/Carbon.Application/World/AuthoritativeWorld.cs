// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World;

/// <summary>
/// Thread-safe authoritative world simulation coordinator bridging fixed-tick ECS physics,
/// procedural chunk streaming/deltas, Spatial AoI replication, and write-behind persistence.
/// </summary>
public sealed class AuthoritativeWorld : IAuthoritativeWorld
{
    private const float BaseWalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const float DefaultInteractRangeTiles = 5.5f;
    private const float DefaultEntityRadius = 7f;

    private readonly Lock _gate = new();
    private readonly ProceduralWorldGenerator _generator;
    private readonly ChunkWorld2D _chunkWorld;
    private readonly WorldSimulation _simulation;
    private readonly SpatialAoiTracker _aoi;
    private readonly IChunkDeltaRepository _chunkDeltaRepo;
    private readonly ICharacterStore _characterStore;
    private readonly IWorldOutboundSink _outboundSink;
    private readonly TimeProvider _time;

    private readonly Dictionary<ulong, ActivePlayerState> _playersByConnection = new();
    private readonly Dictionary<ulong, ulong> _connectionByNetworkId = new();
    private readonly HashSet<long> _dirtyChunks = new();
    private readonly Dictionary<Guid, CharacterRecord> _dirtyCharacters = new();

    // Scratch buffers reused under _gate so StepFixedTick stays steady-state allocation-minimal.
    private readonly List<ulong> _scratchEntered = new(64);
    private readonly List<ulong> _scratchVisible = new(128);
    private readonly List<ulong> _scratchExited = new(64);
    private readonly List<OutboundSpawn> _pendingSpawns = new(64);
    private readonly List<OutboundDespawn> _pendingDespawns = new(64);
    private readonly List<OutboundDelta> _pendingDeltas = new(64);
    private readonly List<EntityDeltaEntry> _batchEntriesScratch = new(128);
    private readonly List<ulong> _aoiRecipientsScratch = new(64);
    private readonly List<Entity> _dirtyResetScratch = new(128);

    private ulong _nextNetworkId = 1000UL;
    private ulong _currentTick;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthoritativeWorld"/> class.
    /// </summary>
    public AuthoritativeWorld(
        ulong worldSeed,
        float tileSize,
        float frequency,
        ushort tickRate,
        IChunkDeltaRepository chunkDeltaRepo,
        ICharacterStore characterStore,
        IWorldOutboundSink outboundSink,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(chunkDeltaRepo);
        ArgumentNullException.ThrowIfNull(characterStore);
        ArgumentNullException.ThrowIfNull(outboundSink);

        this.WorldSeed = worldSeed;
        this.TileSize = tileSize > 0f ? tileSize : 32f;
        this.Frequency = frequency > 0f ? frequency : 0.0008f;
        this.TickRate = tickRate > 0 ? tickRate : (ushort)30;

        _chunkDeltaRepo = chunkDeltaRepo;
        _characterStore = characterStore;
        _outboundSink = outboundSink;
        _time = time ?? TimeProvider.System;

        _generator = new ProceduralWorldGenerator(this.WorldSeed);
        _chunkWorld = new ChunkWorld2D(_generator, this.TileSize, this.Frequency);
        _simulation = new WorldSimulation(this.TickRate, this.TileSize)
        {
            WorldPhysics = _chunkWorld,
        };
        _aoi = new SpatialAoiTracker(this.TileSize * 24f);
    }

    /// <inheritdoc/>
    public ulong WorldSeed { get; }

    /// <inheritdoc/>
    public ushort GeneratorVersion => ProceduralWorldGenerator.GeneratorVersion;

    /// <inheritdoc/>
    public float TileSize { get; }

    /// <inheritdoc/>
    public float Frequency { get; }

    /// <inheritdoc/>
    public ushort TickRate { get; }

    /// <inheritdoc/>
    public ulong CurrentTick
    {
        get
        {
            lock (_gate)
            {
                return _currentTick;
            }
        }
    }

    /// <inheritdoc/>
    public Vector2 DefaultSpawnWorldPosition => _chunkWorld.SpawnWorldPosition;

    /// <inheritdoc/>
    public async Task InitializeFromStoreAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PersistedChunkDelta> deltas = await _chunkDeltaRepo.LoadAllAsync(ct).ConfigureAwait(false);
        lock (_gate)
        {
            for (int i = 0; i < deltas.Count; i++)
            {
                ChunkDelta domainDelta = ChunkDelta.Deserialize(deltas[i].Payload);
                _chunkWorld.ImportDelta(domainDelta);
            }

            Vector2 originSpawn = _chunkWorld.SpawnWorldPosition;
            _chunkWorld.UpdateAroundPosition(originSpawn, chunkRadius: 2, evictionRadius: 8);
        }
    }

    /// <inheritdoc/>
    public EnterWorldResult TryEnterWorld(ulong connectionId, ulong sessionId, CharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);

        lock (_gate)
        {
            if (_playersByConnection.ContainsKey(connectionId))
            {
                _ = this.RemovePlayerLocked(connectionId);
            }

            Vector2 spawnPos = new(character.PositionX, character.PositionY);
            _chunkWorld.UpdateAroundPosition(spawnPos, chunkRadius: 2, evictionRadius: 8);

            ulong networkId = ++_nextNetworkId;
            Entity entity = _simulation.SpawnNetworkEntity(networkId, spawnPos, DefaultEntityRadius, sessionId);
            _simulation.Entities.SetVelocity(entity, new Velocity2D(0f, 0f, 5f));
            _simulation.Entities.SetHealth(entity, new HealthComponent(character.CurrentHealth, character.MaxHealth));

            ConnectedPlayerSession session = new(
                sessionId,
                connectionId,
                character.AccountId,
                character.Id,
                character.Name,
                networkId,
                character.SelectedHotbarSlot,
                ResolveToolHarvestTier(character.SelectedHotbarSlot));

            _playersByConnection[connectionId] = new ActivePlayerState(session, entity, character);
            _connectionByNetworkId[networkId] = connectionId;
            _aoi.TrackConnection(connectionId);

            return new EnterWorldResult(
                OpStatus.Ok,
                networkId,
                this.WorldSeed,
                this.GeneratorVersion,
                this.TileSize,
                this.Frequency,
                spawnPos.X,
                spawnPos.Y,
                _currentTick,
                this.TickRate);
        }
    }

    /// <inheritdoc/>
    public bool LeaveWorld(ulong connectionId)
    {
        lock (_gate)
        {
            return this.RemovePlayerLocked(connectionId);
        }
    }

    /// <inheritdoc/>
    public bool TryGetSessionByConnection(ulong connectionId, out ConnectedPlayerSession? session)
    {
        lock (_gate)
        {
            if (_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                session = state.Session;
                return true;
            }

            session = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public void EnqueueMoveInput(ulong connectionId, uint clientTick, Vector2 direction, bool sprint)
    {
        lock (_gate)
        {
            if (_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                state.PendingDirection = direction;
                state.PendingSprint = sprint;
                state.LastClientTick = clientTick;
                state.HasPendingInput = true;
            }
        }
    }

    /// <inheritdoc/>
    public OpStatus TrySelectHotbarSlot(ulong connectionId, byte slotIndex)
    {
        if (slotIndex > 9)
        {
            return OpStatus.Invalid;
        }

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            byte tier = ResolveToolHarvestTier(slotIndex);
            state.Session = state.Session with { SelectedHotbarSlot = slotIndex, SelectedHarvestTier = tier };
            this.MarkCharacterDirtyLocked(state);
            return OpStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY)
    {
        bool broadcast;
        uint revision = 0;
        float durability;
        bool isDestroyed;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            ulong respawnAt = _currentTick + (ulong)(this.TickRate * 30);
            const float StrikeDamage = 40f;

            if (!_chunkWorld.TryDamagePropAt(tileX, tileY, StrikeDamage, state.Session.SelectedHarvestTier, respawnAt, out MapProp updated))
            {
                OpStatus rejectStatus = updated.Type == MapPropType.None
                    ? OpStatus.NotFound
                    : OpStatus.Forbidden;
                return new WorldMutationOutcome(rejectStatus, 0);
            }

            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            durability = updated.Destructibility.CurrentDurability;
            isDestroyed = updated.Destructibility.IsDestroyed;
            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            ulong[] recipients = [.. _aoiRecipientsScratch];
            _outboundSink.SendPropChanged(recipients, tileX, tileY, revision, durability, isDestroyed);
        }

        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryPlaceStructure(ulong connectionId, int tileX, int tileY, StructureType structureType)
    {
        if (structureType == StructureType.None)
        {
            return new WorldMutationOutcome(OpStatus.Invalid, 0);
        }

        bool broadcast;
        uint revision = 0;
        StructureTile placed;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            MapTile tile = _chunkWorld.GetTileAtWorld(tileX, tileY);
            bool hasProp = _chunkWorld.HasSolidPropAt(tileX, tileY);
            PlacementValidationResult result = _chunkWorld.Buildings.TryPlaceStructure(
                tileX,
                tileY,
                structureType,
                tile.Biome,
                hasProp,
                out placed);

            if (result != PlacementValidationResult.Success)
            {
                OpStatus mapped = result is PlacementValidationResult.BlockedByExistingStructure or PlacementValidationResult.BlockedByNaturalObstacle
                    ? OpStatus.Conflict
                    : OpStatus.Invalid;
                return new WorldMutationOutcome(mapped, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            ulong[] recipients = [.. _aoiRecipientsScratch];
            _outboundSink.SendStructureChanged(
                recipients,
                tileX,
                tileY,
                (byte)placed.Type,
                placed.CurrentDurability,
                (byte)placed.DoorState,
                (byte)placed.Connectivity);
        }

        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY)
    {
        bool broadcast;
        uint revision = 0;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            if (!_chunkWorld.Buildings.TryDemolishStructure(tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.NotFound, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            ulong[] recipients = [.. _aoiRecipientsScratch];
            _outboundSink.SendStructureChanged(recipients, tileX, tileY, 0, 0f, 0, 0);
        }

        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY)
    {
        bool broadcast;
        uint revision = 0;
        StructureTile updated;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            if (!_chunkWorld.Buildings.ToggleDoor(tileX, tileY, out updated))
            {
                return new WorldMutationOutcome(OpStatus.NotFound, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            ulong[] recipients = [.. _aoiRecipientsScratch];
            _outboundSink.SendStructureChanged(
                recipients,
                tileX,
                tileY,
                (byte)updated.Type,
                updated.CurrentDurability,
                (byte)updated.DoorState,
                (byte)updated.Connectivity);
        }

        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public bool TryCreateChunkDeltaPacket(int chunkX, int chunkY, uint knownRevision, out MapChunkDelta? packet)
    {
        lock (_gate)
        {
            if (_chunkWorld.TryGetDelta(chunkX, chunkY, out ChunkDelta? delta) &&
                delta is not null &&
                !delta.IsEmpty &&
                delta.Revision > knownRevision)
            {
                packet = ChunkDeltaPacketMapper.ToPacket(delta, isEndOfStream: true);
                return true;
            }

            packet = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public void StepFixedTick()
    {
        lock (_gate)
        {
            _pendingSpawns.Clear();
            _pendingDespawns.Clear();
            _pendingDeltas.Clear();
            _dirtyResetScratch.Clear();

            _currentTick++;
            float dt = 1f / this.TickRate;

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState state = kvp.Value;
                if (!state.HasPendingInput)
                {
                    continue;
                }

                float speed = state.PendingSprint ? SprintSpeed : BaseWalkSpeed;
                _simulation.EnqueueCommand(SimulationCommand.MoveInput(
                    state.Entity,
                    state.PendingDirection,
                    speed,
                    state.LastClientTick));
                state.HasPendingInput = false;
            }

            _simulation.Tick(new FixedTickArgs(_currentTick, dt, _currentTick * dt));

            if ((_currentTick % this.TickRate) == 0UL)
            {
                _ = _chunkWorld.ProcessPropRespawns(_currentTick);
            }

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState observer = kvp.Value;
                if (!_simulation.Entities.IsAlive(observer.Entity) || !_simulation.Entities.HasTransform(observer.Entity))
                {
                    continue;
                }

                Vector2 obsPos = _simulation.Entities.GetTransform(observer.Entity).Position;
                if ((_currentTick % 10UL) == 0UL)
                {
                    _chunkWorld.UpdateAroundPosition(obsPos, chunkRadius: 2, evictionRadius: 8);
                }

                _aoi.ComputeVisibility(
                    observer.Session.ConnectionId,
                    observer.Session.PlayerNetworkId,
                    obsPos,
                    _simulation,
                    _scratchEntered,
                    _scratchVisible,
                    _scratchExited);

                for (int i = 0; i < _scratchEntered.Count; i++)
                {
                    ulong targetNetId = _scratchEntered[i];
                    if (this.TryBuildSpawnSnapshotLocked(targetNetId, out EntitySpawnSnapshot snap))
                    {
                        _pendingSpawns.Add(new OutboundSpawn(observer.Session.ConnectionId, snap));
                    }
                }

                for (int i = 0; i < _scratchExited.Count; i++)
                {
                    _pendingDespawns.Add(new OutboundDespawn(observer.Session.ConnectionId, _scratchExited[i], _currentTick));
                }

                _batchEntriesScratch.Clear();
                for (int i = 0; i < _scratchVisible.Count; i++)
                {
                    ulong visibleNetId = _scratchVisible[i];
                    if (_simulation.TryGetEntityByNetworkId(visibleNetId, out Entity visibleEntity))
                    {
                        EntityDirtyMask mask = _simulation.GetDirtyFlags(visibleEntity);
                        if (mask != EntityDirtyMask.None)
                        {
                            Vector2 pos = _simulation.Entities.GetTransform(visibleEntity).Position;
                            Vector2 vel = _simulation.Entities.HasVelocity(visibleEntity)
                                ? _simulation.Entities.GetVelocity(visibleEntity).Linear
                                : Vector2.Zero;
                            float hp = _simulation.Entities.HasHealth(visibleEntity)
                                ? _simulation.Entities.GetHealth(visibleEntity).Current
                                : 100f;
                            _batchEntriesScratch.Add(new EntityDeltaEntry(visibleNetId, (byte)mask, pos.X, pos.Y, vel.X, vel.Y, hp));
                        }
                    }
                }

                EntityDirtyMask selfDirty = _simulation.GetDirtyFlags(observer.Entity);
                if (selfDirty != EntityDirtyMask.None || _batchEntriesScratch.Count > 0)
                {
                    _pendingDeltas.Add(new OutboundDelta(
                        observer.Session.ConnectionId,
                        _currentTick,
                        observer.LastClientTick,
                        obsPos.X,
                        obsPos.Y,
                        [.. _batchEntriesScratch]));
                }

                if ((selfDirty & EntityDirtyMask.Transform) != 0)
                {
                    this.MarkCharacterDirtyLocked(observer);
                }

                _dirtyResetScratch.Add(observer.Entity);
            }

            for (int i = 0; i < _dirtyResetScratch.Count; i++)
            {
                _simulation.ClearDirtyFlags(_dirtyResetScratch[i]);
            }
        }

        // Dispatch packets outside _gate so slow sockets cannot block simulation steps.
        for (int i = 0; i < _pendingSpawns.Count; i++)
        {
            OutboundSpawn item = _pendingSpawns[i];
            EntitySpawnSnapshot snap = item.Snapshot;
            _outboundSink.SendEntitySpawn(item.ConnectionId, in snap);
        }

        for (int i = 0; i < _pendingDespawns.Count; i++)
        {
            OutboundDespawn item = _pendingDespawns[i];
            _outboundSink.SendEntityDespawn(item.ConnectionId, item.NetworkId, item.ServerTick);
        }

        for (int i = 0; i < _pendingDeltas.Count; i++)
        {
            OutboundDelta item = _pendingDeltas[i];
            _outboundSink.SendEntityDeltaBatch(
                item.ConnectionId,
                item.ServerTick,
                item.LastClientTick,
                item.SelfX,
                item.SelfY,
                item.Entries);
        }
    }

    /// <inheritdoc/>
    public async Task FlushDirtyPersistenceAsync(CancellationToken ct = default)
    {
        List<PersistedChunkDelta> chunkBatch = [];
        List<CharacterRecord> characterBatch = [];
        DateTimeOffset now = _time.GetUtcNow();

        lock (_gate)
        {
            if (_dirtyChunks.Count > 0)
            {
                foreach (long key in _dirtyChunks)
                {
                    (int cx, int cy) = UnpackKey(key);
                    if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
                    {
                        chunkBatch.Add(new PersistedChunkDelta(
                            cx,
                            cy,
                            delta.Revision,
                            delta.GeneratorVersion,
                            delta.Serialize(),
                            now));
                    }
                }

                _dirtyChunks.Clear();
            }

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                this.MarkCharacterDirtyLocked(kvp.Value);
            }

            if (_dirtyCharacters.Count > 0)
            {
                foreach (KeyValuePair<Guid, CharacterRecord> kvp in _dirtyCharacters)
                {
                    characterBatch.Add(kvp.Value);
                }

                _dirtyCharacters.Clear();
            }
        }

        if (chunkBatch.Count > 0)
        {
            await _chunkDeltaRepo.SaveBatchAsync(chunkBatch, ct).ConfigureAwait(false);
        }

        if (characterBatch.Count > 0)
        {
            await _characterStore.SaveBatchAsync(characterBatch, ct).ConfigureAwait(false);
        }
    }

    private bool RemovePlayerLocked(ulong connectionId)
    {
        if (!_playersByConnection.Remove(connectionId, out ActivePlayerState? state))
        {
            return false;
        }

        this.MarkCharacterDirtyLocked(state);
        _ = _connectionByNetworkId.Remove(state.Session.PlayerNetworkId);
        _aoi.UntrackConnection(connectionId);
        _simulation.DespawnEntity(state.Entity);
        return true;
    }

    private void MarkCharacterDirtyLocked(ActivePlayerState state)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) || !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        Vector2 pos = _simulation.Entities.GetTransform(state.Entity).Position;
        float hp = _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity).Current
            : state.SourceCharacter.CurrentHealth;

        CharacterRecord updated = state.SourceCharacter with
        {
            PositionX = pos.X,
            PositionY = pos.Y,
            CurrentHealth = hp,
            SelectedHotbarSlot = state.Session.SelectedHotbarSlot,
            UpdatedAtUtc = _time.GetUtcNow(),
        };
        state.SourceCharacter = updated;
        _dirtyCharacters[updated.Id] = updated;
    }

    private bool IsWithinInteractRangeLocked(Entity entity, int tileX, int tileY)
    {
        if (!_simulation.Entities.IsAlive(entity) || !_simulation.Entities.HasTransform(entity))
        {
            return false;
        }

        Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
        Vector2 tileCenter = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        float maxDist = DefaultInteractRangeTiles * this.TileSize;
        return Vector2.DistanceSquared(pos, tileCenter) <= (maxDist * maxDist);
    }

    private bool TryBuildSpawnSnapshotLocked(ulong networkId, out EntitySpawnSnapshot snapshot)
    {
        if (!_simulation.TryGetEntityByNetworkId(networkId, out Entity entity) || !_simulation.Entities.HasTransform(entity))
        {
            snapshot = default;
            return false;
        }

        Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
        Vector2 vel = _simulation.Entities.HasVelocity(entity)
            ? _simulation.Entities.GetVelocity(entity).Linear
            : Vector2.Zero;
        float radius = _simulation.Entities.HasCollider(entity)
            ? _simulation.Entities.GetCollider(entity).Radius
            : DefaultEntityRadius;
        float hp = _simulation.Entities.HasHealth(entity)
            ? _simulation.Entities.GetHealth(entity).Current
            : 100f;
        float maxHp = _simulation.Entities.HasHealth(entity)
            ? _simulation.Entities.GetHealth(entity).Max
            : 100f;

        string name = "Adventurer";
        ushort archetype = 0;
        if (_connectionByNetworkId.TryGetValue(networkId, out ulong ownerConn) &&
            _playersByConnection.TryGetValue(ownerConn, out ActivePlayerState? state))
        {
            name = state.Session.CharacterName;
            archetype = state.SourceCharacter.AppearancePreset;
        }

        snapshot = new EntitySpawnSnapshot(
            networkId,
            EntityKind.Player,
            archetype,
            name,
            pos.X,
            pos.Y,
            vel.X,
            vel.Y,
            radius,
            hp,
            maxHp);
        return true;
    }

    private void CollectNearbyConnectionsLocked(int tileX, int tileY, List<ulong> output)
    {
        output.Clear();
        Vector2 target = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        float radiusSq = (_aoi.ViewRadius + (this.TileSize * 2f)) * (_aoi.ViewRadius + (this.TileSize * 2f));

        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState state = kvp.Value;
            if (!_simulation.Entities.HasTransform(state.Entity))
            {
                continue;
            }

            Vector2 pos = _simulation.Entities.GetTransform(state.Entity).Position;
            if (Vector2.DistanceSquared(pos, target) <= radiusSq)
            {
                output.Add(kvp.Key);
            }
        }
    }

    private static byte ResolveToolHarvestTier(byte slotIndex) => slotIndex switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        _ => 1,
    };

    private static (int X, int Y) UnpackKey(long key) =>
        ((int)(key >> 32), (int)(uint)key);

    private sealed class ActivePlayerState(
        ConnectedPlayerSession session,
        Entity entity,
        CharacterRecord sourceCharacter)
    {
        public ConnectedPlayerSession Session { get; set; } = session;
        public Entity Entity { get; } = entity;
        public CharacterRecord SourceCharacter { get; set; } = sourceCharacter;
        public Vector2 PendingDirection { get; set; }
        public bool PendingSprint { get; set; }
        public uint LastClientTick { get; set; }
        public bool HasPendingInput { get; set; }
    }

    private readonly record struct OutboundSpawn(ulong ConnectionId, EntitySpawnSnapshot Snapshot);
    private readonly record struct OutboundDespawn(ulong ConnectionId, ulong NetworkId, ulong ServerTick);
    private readonly record struct OutboundDelta(
        ulong ConnectionId,
        ulong ServerTick,
        uint LastClientTick,
        float SelfX,
        float SelfY,
        EntityDeltaEntry[] Entries);
}
