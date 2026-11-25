// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Combat.Cooldowns;
using Carbon.Combat.Hitboxes;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Maps.S2C;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
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
/// AI monsters/NPCs, melee combat, inventory/crafting persistence, procedural chunk deltas,
/// Spatial AoI replication, and zero-allocation tick dispatches.
/// </summary>
public sealed class AuthoritativeWorld : IAuthoritativeWorld
{
    private const float BaseWalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const float DefaultInteractRangeTiles = 5.5f;
    private const float DefaultEntityRadius = 7f;
    private const uint PlayerLayer = 1U;
    private const uint MonsterLayer = 2U;
    private const uint NpcLayer = 4U;
    private const int PlayerInventoryCapacity = 27;
    private const uint AttackAbilityId = 1U;

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
    private readonly Dictionary<ulong, NpcActorMeta> _actorsByNetworkId = new();
    private readonly HashSet<long> _populatedChunks = new();
    private readonly List<PendingMonsterRespawn> _pendingRespawns = new(32);
    private readonly HashSet<long> _dirtyChunks = new();
    private readonly Dictionary<Guid, CharacterRecord> _dirtyCharacters = new();

    // Reusable scratch buffers (zero per-tick heap allocations under steady state).
    private readonly List<ulong> _scratchEntered = new(64);
    private readonly List<ulong> _scratchVisible = new(128);
    private readonly List<ulong> _scratchExited = new(64);
    private readonly List<OutboundSpawn> _pendingSpawns = new(64);
    private readonly List<OutboundDespawn> _pendingDespawns = new(64);
    private readonly List<OutboundDelta> _pendingDeltas = new(64);
    private readonly List<EntityDeltaEntry> _flatDeltaEntries = new(512);
    private readonly List<OutboundCombatHit> _pendingCombatHits = new(32);
    private readonly List<OutboundSlotChange> _pendingSlotChanges = new(32);
    private readonly List<ulong> _flatRecipients = new(256);
    private readonly List<ulong> _aoiRecipientsScratch = new(64);
    private readonly List<Entity> _dirtyResetScratch = new(256);
    private readonly List<(ushort SlotIndex, ItemSlot Slot)> _mutationSlotScratch = new(8);
    private readonly Entity[] _combatHitScratch = new Entity[32];

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
        _simulation.RegisterSystem(new AiBehaviorSystem(_chunkWorld.NavGrid, this.TileSize));
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
            this.PopulateChunkActorsAroundLocked(originSpawn, radius: 2);
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
            this.PopulateChunkActorsAroundLocked(spawnPos, radius: 2);

            ulong networkId = ++_nextNetworkId;
            Entity entity = _simulation.SpawnNetworkEntity(networkId, spawnPos, DefaultEntityRadius, sessionId);
            _simulation.Entities.SetCollider(entity, new Collider2D(ColliderKind.Circle, DefaultEntityRadius, default, false, PlayerLayer, uint.MaxValue));
            _simulation.Entities.SetVelocity(entity, new Velocity2D(0f, 0f, 5f));
            _simulation.Entities.SetHealth(entity, new HealthComponent(character.CurrentHealth, character.MaxHealth));

            InventoryContainer inventory = new(PlayerInventoryCapacity);
            if (!InventoryBinaryCodec.TryDeserializeInto(character.InventoryBlob, inventory))
            {
                StarterGameCatalog.GrantStarterLoadout(inventory);
            }

            byte selectedSlot = character.SelectedHotbarSlot;
            byte harvestTier = ResolveSlotHarvestTier(inventory, selectedSlot);

            ConnectedPlayerSession session = new(
                sessionId,
                connectionId,
                character.AccountId,
                character.Id,
                character.Name,
                networkId,
                selectedSlot,
                harvestTier);

            _playersByConnection[connectionId] = new ActivePlayerState(session, entity, character, inventory);
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
    public OpStatus EnqueueAttackInput(ulong connectionId, ulong targetNetworkId, Vector2 aimWorldPosition, byte abilitySlot)
    {
        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            state.HasPendingAttack = true;
            state.PendingAttackTargetId = targetNetworkId;
            state.PendingAttackAim = aimWorldPosition;
            state.PendingAttackAbility = abilitySlot;
            return OpStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public bool GetInventorySnapshot(ulong connectionId, List<(ushort SlotIndex, ItemSlot Slot)> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return false;
            }

            ReadOnlySpan<ItemSlot> slots = state.Inventory.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty)
                {
                    output.Add(((ushort)i, slots[i]));
                }
            }

            return true;
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

            byte tier = ResolveSlotHarvestTier(state.Inventory, slotIndex);
            state.Session = state.Session with { SelectedHotbarSlot = slotIndex, SelectedHarvestTier = tier };
            this.MarkCharacterDirtyLocked(state);
            return OpStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public OpStatus TryCraftRecipe(ulong connectionId, ushort recipeId, ushort quantity)
    {
        if (recipeId == 0 || quantity == 0 || quantity > 64)
        {
            return OpStatus.Invalid;
        }

        if (!StarterGameCatalog.Recipes.TryGet(recipeId, out CraftingRecipe? recipe))
        {
            return OpStatus.NotFound;
        }

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            int maxOutStack = StarterGameCatalog.GetMaxStackSize(recipe.Output.ItemId);
            if (!RecipeMatcher.CanCraft(state.Inventory, recipe))
            {
                return OpStatus.Conflict;
            }

            _mutationSlotScratch.Clear();
            ReadOnlySpan<ItemSlot> beforeSlots = state.Inventory.Slots;
            Span<ItemSlot> beforeSnapshot = stackalloc ItemSlot[beforeSlots.Length];
            beforeSlots.CopyTo(beforeSnapshot);

            int crafted = 0;
            for (int q = 0; q < quantity; q++)
            {
                if (!RecipeMatcher.TryCraft(state.Inventory, recipe, maxOutStack))
                {
                    break;
                }

                crafted++;
            }

            if (crafted == 0)
            {
                return OpStatus.Conflict;
            }

            CollectModifiedSlots(beforeSnapshot, state.Inventory.Slots, _mutationSlotScratch);
            byte newTier = ResolveSlotHarvestTier(state.Inventory, state.Session.SelectedHotbarSlot);
            state.Session = state.Session with { SelectedHarvestTier = newTier };
            this.MarkCharacterDirtyLocked(state);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(connectionId, idx, slot.ItemId, slot.Quantity, slot.Durability);
        }

        return OpStatus.Ok;
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

            _mutationSlotScratch.Clear();
            if (isDestroyed)
            {
                ItemSlot drop = ResolvePropDrop(updated.Type);
                if (!drop.IsEmpty)
                {
                    AddItemTrackingChanges(state.Inventory, in drop, _mutationSlotScratch);
                    this.MarkCharacterDirtyLocked(state);
                }
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            _outboundSink.SendPropChanged(CollectionsMarshal.AsSpan(_aoiRecipientsScratch), tileX, tileY, revision, durability, isDestroyed);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(connectionId, idx, slot.ItemId, slot.Quantity, slot.Durability);
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

            _mutationSlotScratch.Clear();
            uint requiredKit = ResolveStructureKitItemId(structureType);
            if (requiredKit != 0U && state.Inventory.CountItem(requiredKit) > 0)
            {
                RemoveItemTrackingChanges(state.Inventory, requiredKit, 1, _mutationSlotScratch);
                this.MarkCharacterDirtyLocked(state);
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
            _outboundSink.SendStructureChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                tileX,
                tileY,
                (byte)placed.Type,
                placed.CurrentDurability,
                (byte)placed.DoorState,
                (byte)placed.Connectivity);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(connectionId, idx, slot.ItemId, slot.Quantity, slot.Durability);
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
            _outboundSink.SendStructureChanged(CollectionsMarshal.AsSpan(_aoiRecipientsScratch), tileX, tileY, 0, 0f, 0, 0);
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
            _outboundSink.SendStructureChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
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
            _flatDeltaEntries.Clear();
            _pendingCombatHits.Clear();
            _pendingSlotChanges.Clear();
            _flatRecipients.Clear();
            _dirtyResetScratch.Clear();

            _currentTick++;
            float dt = 1f / this.TickRate;

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState state = kvp.Value;
                if (state.HasPendingInput)
                {
                    float speed = state.PendingSprint ? SprintSpeed : BaseWalkSpeed;
                    _simulation.EnqueueCommand(SimulationCommand.MoveInput(
                        state.Entity,
                        state.PendingDirection,
                        speed,
                        state.LastClientTick));
                    state.HasPendingInput = false;
                }

                if (state.HasPendingAttack)
                {
                    this.ProcessPlayerAttackLocked(state);
                    state.HasPendingAttack = false;
                }
            }

            _simulation.Tick(new FixedTickArgs(_currentTick, dt, _currentTick * dt));

            this.ProcessAiAttacksLocked();
            this.ProcessMonsterRespawnsLocked();

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
                    this.PopulateChunkActorsAroundLocked(obsPos, radius: 2);
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

                int deltaOffset = _flatDeltaEntries.Count;
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
                            _flatDeltaEntries.Add(new EntityDeltaEntry(visibleNetId, (byte)mask, pos.X, pos.Y, vel.X, vel.Y, hp));
                        }
                    }
                }

                int deltaCount = _flatDeltaEntries.Count - deltaOffset;
                EntityDirtyMask selfDirty = _simulation.GetDirtyFlags(observer.Entity);
                if (selfDirty != EntityDirtyMask.None || deltaCount > 0)
                {
                    _pendingDeltas.Add(new OutboundDelta(
                        observer.Session.ConnectionId,
                        _currentTick,
                        observer.LastClientTick,
                        obsPos.X,
                        obsPos.Y,
                        deltaOffset,
                        deltaCount));
                }

                if ((selfDirty & EntityDirtyMask.Transform) != 0)
                {
                    this.MarkCharacterDirtyLocked(observer);
                }
            }

            ReadOnlySpan<Entity> activeEntities = CollectionsMarshal.AsSpan((List<Entity>)_simulation.Entities.ActiveEntities);
            for (int i = 0; i < activeEntities.Length; i++)
            {
                _dirtyResetScratch.Add(activeEntities[i]);
            }

            for (int i = 0; i < _dirtyResetScratch.Count; i++)
            {
                _simulation.ClearDirtyFlags(_dirtyResetScratch[i]);
            }
        }

        // Dispatch packets outside _gate using zero-alloc span slices.
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

        Span<EntityDeltaEntry> deltaSpan = CollectionsMarshal.AsSpan(_flatDeltaEntries);
        for (int i = 0; i < _pendingDeltas.Count; i++)
        {
            OutboundDelta item = _pendingDeltas[i];
            _outboundSink.SendEntityDeltaBatch(
                item.ConnectionId,
                item.ServerTick,
                item.LastClientTick,
                item.SelfX,
                item.SelfY,
                deltaSpan.Slice(item.Offset, item.Count));
        }

        Span<ulong> recipientsSpan = CollectionsMarshal.AsSpan(_flatRecipients);
        for (int i = 0; i < _pendingCombatHits.Count; i++)
        {
            OutboundCombatHit hit = _pendingCombatHits[i];
            _outboundSink.SendCombatHit(
                recipientsSpan.Slice(hit.RecipientsOffset, hit.RecipientsCount),
                hit.AttackerNetworkId,
                hit.DefenderNetworkId,
                hit.Damage,
                hit.RemainingHealth,
                hit.IsCritical,
                hit.IsKilled);
        }

        for (int i = 0; i < _pendingSlotChanges.Count; i++)
        {
            OutboundSlotChange change = _pendingSlotChanges[i];
            _outboundSink.SendInventorySlotChanged(
                change.ConnectionId,
                change.SlotIndex,
                change.Slot.ItemId,
                change.Slot.Quantity,
                change.Slot.Durability);
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

    private void ProcessPlayerAttackLocked(ActivePlayerState state)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) ||
            !_simulation.Entities.HasTransform(state.Entity) ||
            !state.Cooldowns.IsReady(AttackAbilityId, _currentTick))
        {
            return;
        }

        state.Cooldowns.StartCooldown(AttackAbilityId, _currentTick, (uint)Math.Max(10, this.TickRate / 2));

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;

        // Consumable quick-use on primary action (Berry Herb heals +25 HP).
        if (activeSlot.ItemId == StarterGameCatalog.BerryHerb &&
            activeSlot.Quantity > 0 &&
            _simulation.Entities.HasHealth(state.Entity))
        {
            HealthComponent currentHp = _simulation.Entities.GetHealth(state.Entity);
            if (currentHp.Current < currentHp.Max)
            {
                float healedHp = MathF.Min(currentHp.Max, currentHp.Current + 25f);
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(healedHp, currentHp.Max));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);

                ItemSlot updatedSlot = activeSlot.Quantity > 1
                    ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
                    : ItemSlot.Empty;
                state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
                _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
                this.MarkCharacterDirtyLocked(state);

                this.QueueCombatHitLocked(
                    _simulation.Entities.GetTransform(state.Entity).Position,
                    state.Session.PlayerNetworkId,
                    state.Session.PlayerNetworkId,
                    -25f,
                    healedHp,
                    isCritical: false,
                    isKilled: false);
                return;
            }
        }

        Vector2 attackerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 rawAim = state.PendingAttackAim - attackerPos;
        Vector2 aimDir = rawAim.LengthSquared() > 1e-4f ? Vector2.Normalize(rawAim) : Vector2.UnitX;

        HitboxQuery.QueryArc(
            _simulation,
            attackerPos,
            aimDir,
            radius: 72f,
            halfAngleRadians: 1.15f,
            _combatHitScratch,
            out int hitCount,
            layerMask: MonsterLayer,
            ignoreEntity: state.Entity);

        if (hitCount == 0 && state.PendingAttackTargetId != 0 &&
            _simulation.TryGetEntityByNetworkId(state.PendingAttackTargetId, out Entity explicitTarget) &&
            _simulation.Entities.HasTransform(explicitTarget) &&
            _simulation.Entities.HasCollider(explicitTarget) &&
            (_simulation.Entities.GetCollider(explicitTarget).Layer & MonsterLayer) != 0 &&
            Vector2.DistanceSquared(attackerPos, _simulation.Entities.GetTransform(explicitTarget).Position) <= (72f * 72f))
        {
            _combatHitScratch[0] = explicitTarget;
            hitCount = 1;
        }

        float baseDamage = StarterGameCatalog.GetWeaponBaseDamage(activeSlot.ItemId);
        bool isCrit = activeSlot.ItemId == StarterGameCatalog.IronSword && ((_currentTick + state.Session.PlayerNetworkId) % 5UL == 0UL);
        float finalDamage = isCrit ? baseDamage * 1.5f : baseDamage;
        EntityAttachmentStore<ThreatTable> threatStore = _simulation.Entities.GetAttachmentStore<ThreatTable>();

        for (int i = 0; i < hitCount; i++)
        {
            Entity target = _combatHitScratch[i];
            if (!_simulation.Entities.IsAlive(target) ||
                !_simulation.Entities.HasHealth(target) ||
                !_simulation.TryGetNetworkIdentity(target, out NetworkIdentity targetIdentity))
            {
                continue;
            }

            HealthComponent hp = _simulation.Entities.GetHealth(target);
            float remaining = MathF.Max(0f, hp.Current - finalDamage);
            bool killed = remaining <= 0f;
            Vector2 targetPos = _simulation.Entities.HasTransform(target) ? _simulation.Entities.GetTransform(target).Position : attackerPos;

            _simulation.Entities.SetHealth(target, new HealthComponent(remaining, hp.Max));
            _simulation.MarkDirty(target, EntityDirtyMask.Health);

            if (!killed)
            {
                threatStore.GetOrAdd(target, static () => new ThreatTable()).AddThreat(state.Entity, finalDamage * 2f);
                if (_simulation.Entities.HasComponent<AiBrainComponent>(target))
                {
                    ref AiBrainComponent brain = ref _simulation.Entities.GetComponent<AiBrainComponent>(target);
                    brain = brain with { State = AiState.Chase, TargetEntity = state.Entity };
                }
            }
            else
            {
                if (_actorsByNetworkId.Remove(targetIdentity.NetworkId, out NpcActorMeta meta))
                {
                    _pendingRespawns.Add(new PendingMonsterRespawn(_currentTick + (ulong)(this.TickRate * 45), meta));
                    ItemSlot loot = new(meta.DropItemId, 2);
                    int beforeCount = _pendingSlotChanges.Count;
                    AddPendingSlotLoot(state.Inventory, in loot, state.Session.ConnectionId, _pendingSlotChanges);
                    if (_pendingSlotChanges.Count > beforeCount)
                    {
                        this.MarkCharacterDirtyLocked(state);
                    }
                }

                _simulation.DespawnEntity(target);
            }

            this.QueueCombatHitLocked(
                targetPos,
                state.Session.PlayerNetworkId,
                targetIdentity.NetworkId,
                finalDamage,
                remaining,
                isCrit,
                killed);
        }
    }

    private void ProcessAiAttacksLocked()
    {
        ReadOnlySpan<AiAttackReadyEvent> attackEvents = _simulation.ReadEvents<AiAttackReadyEvent>();
        for (int i = 0; i < attackEvents.Length; i++)
        {
            AiAttackReadyEvent evt = attackEvents[i];
            if (!_simulation.Entities.IsAlive(evt.Attacker) ||
                !_simulation.Entities.IsAlive(evt.Target) ||
                !_simulation.Entities.HasTransform(evt.Attacker) ||
                !_simulation.Entities.HasTransform(evt.Target) ||
                !_simulation.Entities.HasHealth(evt.Target) ||
                !_simulation.TryGetNetworkIdentity(evt.Attacker, out NetworkIdentity attackerNet) ||
                !_simulation.TryGetNetworkIdentity(evt.Target, out NetworkIdentity targetNet))
            {
                continue;
            }

            Vector2 atkPos = _simulation.Entities.GetTransform(evt.Attacker).Position;
            Vector2 defPos = _simulation.Entities.GetTransform(evt.Target).Position;
            if (Vector2.DistanceSquared(atkPos, defPos) > (56f * 56f))
            {
                continue;
            }

            HealthComponent hp = _simulation.Entities.GetHealth(evt.Target);
            const float MonsterDamage = 10f;
            float nextHp = MathF.Max(0f, hp.Current - MonsterDamage);
            bool killed = nextHp <= 0f;
            if (killed)
            {
                _simulation.Entities.SetHealth(evt.Target, new HealthComponent(hp.Max, hp.Max));
                _simulation.EnqueueCommand(SimulationCommand.Teleport(evt.Target, _chunkWorld.SpawnWorldPosition));
            }
            else
            {
                _simulation.Entities.SetHealth(evt.Target, new HealthComponent(nextHp, hp.Max));
            }

            _simulation.MarkDirty(evt.Target, EntityDirtyMask.Health);
            this.QueueCombatHitLocked(defPos, attackerNet.NetworkId, targetNet.NetworkId, MonsterDamage, killed ? hp.Max : nextHp, false, killed);
        }

        _simulation.ClearEvents<AiAttackReadyEvent>();
    }

    private void ProcessMonsterRespawnsLocked()
    {
        for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
        {
            PendingMonsterRespawn pending = _pendingRespawns[i];
            if (_currentTick < pending.RespawnTick)
            {
                continue;
            }

            _pendingRespawns.RemoveAt(i);
            this.SpawnActorFromMetaLocked(pending.Meta);
        }
    }

    private void PopulateChunkActorsAroundLocked(Vector2 worldCenter, int radius)
    {
        int centerCx = Chunk2D.ToChunkCoord((int)MathF.Floor(worldCenter.X / this.TileSize));
        int centerCy = Chunk2D.ToChunkCoord((int)MathF.Floor(worldCenter.Y / this.TileSize));
        Vector2 safeOrigin = _chunkWorld.SpawnWorldPosition;
        float safeRadiusSq = 100f * this.TileSize * this.TileSize;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int cx = centerCx + dx;
                int cy = centerCy + dy;
                long key = Chunk2D.PackKey(cx, cy);
                if (!_populatedChunks.Add(key) || !_chunkWorld.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk is null)
                {
                    continue;
                }

                // Spawn friendly settlement NPCs near Points of Interest.
                for (int i = 0; i < chunk.PointsOfInterest.Count; i++)
                {
                    PointOfInterest poi = chunk.PointsOfInterest[i];
                    if (poi.Type is PointOfInterestType.RiverVillage or
                        PointOfInterestType.Harbor or
                        PointOfInterestType.MountainShrine or
                        PointOfInterestType.LakesideCamp or
                        PointOfInterestType.DesertVillage or
                        PointOfInterestType.SnowVillage or
                        PointOfInterestType.SwampVillage)
                    {
                        int npcTx = poi.TileX + 1;
                        int npcTy = poi.TileY + 1;
                        MapTile npcTile = _chunkWorld.GetTileAtWorld(npcTx, npcTy);
                        if (!npcTile.IsSolid)
                        {
                            string npcName = poi.Type == PointOfInterestType.Harbor
                                ? "Harbour Master"
                                : poi.Type == PointOfInterestType.MountainShrine
                                    ? "Shrine Keeper"
                                    : "Village Guard";
                            Vector2 npcPos = new((npcTx + 0.5f) * this.TileSize, (npcTy + 0.5f) * this.TileSize);
                            NpcActorMeta npcMeta = new(
                                EntityKind.Npc,
                                201,
                                npcName,
                                npcPos,
                                200f,
                                70f,
                                key,
                                StarterGameCatalog.BerryHerb);
                            this.SpawnActorFromMetaLocked(npcMeta);
                        }
                    }
                }

                // Deterministic biome monster spawn (~70% of wilderness chunks away from safe starter tile).
                ulong h = HashChunk(this.WorldSeed, cx, cy);
                if ((h % 10UL) >= 7UL)
                {
                    continue;
                }

                int lx = 6 + (int)(h % 20UL);
                int ly = 6 + (int)((h >> 8) % 20UL);
                MapTile spawnTile = chunk.GetTile(lx, ly);
                if (spawnTile.IsSolid ||
                    spawnTile.Biome == BiomeType.ShallowWater ||
                    spawnTile.Biome == BiomeType.DeepOcean)
                {
                    continue;
                }

                int wx = (cx * Chunk2D.Size) + lx;
                int wy = (cy * Chunk2D.Size) + ly;
                Vector2 monsterPos = new((wx + 0.5f) * this.TileSize, (wy + 0.5f) * this.TileSize);
                if (Vector2.DistanceSquared(monsterPos, safeOrigin) < safeRadiusSq)
                {
                    continue;
                }

                (string mobName, ushort archetypeId, float maxHp, float moveSpeed, uint dropId) = ResolveBiomeMonster(spawnTile.Biome);
                NpcActorMeta monsterMeta = new(
                    EntityKind.Monster,
                    archetypeId,
                    mobName,
                    monsterPos,
                    maxHp,
                    moveSpeed,
                    key,
                    dropId);
                this.SpawnActorFromMetaLocked(monsterMeta);
            }
        }
    }

    private void SpawnActorFromMetaLocked(in NpcActorMeta meta)
    {
        ulong netId = ++_nextNetworkId;
        Entity entity = _simulation.SpawnNetworkEntity(netId, meta.HomePos, DefaultEntityRadius, ownerSessionId: 0UL);
        uint layer = meta.Kind == EntityKind.Monster ? MonsterLayer : NpcLayer;
        _simulation.Entities.SetCollider(entity, new Collider2D(ColliderKind.Circle, DefaultEntityRadius, default, false, layer, uint.MaxValue));
        _simulation.Entities.SetVelocity(entity, new Velocity2D(0f, 0f, 5f));
        _simulation.Entities.SetHealth(entity, new HealthComponent(meta.MaxHealth, meta.MaxHealth));

        float aggro = meta.Kind == EntityKind.Monster ? 192f : 0f;
        float attackRange = meta.Kind == EntityKind.Monster ? 44f : 0f;
        uint targetMask = meta.Kind == EntityKind.Monster ? PlayerLayer : 0U;
        AiBrainComponent brain = new(
            AiState.Patrol,
            meta.HomePos,
            aggro,
            attackRange,
            LeashRadius: 384f,
            meta.MoveSpeed,
            TargetLayerMask: targetMask,
            AttackCooldownTicks: (uint)Math.Max(15, (int)this.TickRate),
            PatrolRadius: meta.Kind == EntityKind.Monster ? 96f : 64f);
        _simulation.Entities.SetComponent(entity, in brain);

        if (meta.Kind == EntityKind.Monster)
        {
            _ = _simulation.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(entity, static () => new ThreatTable());
        }

        _actorsByNetworkId[netId] = meta;
    }

    private void QueueCombatHitLocked(
        Vector2 worldPos,
        ulong attackerNetId,
        ulong defenderNetId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled)
    {
        int offset = _flatRecipients.Count;
        float radiusSq = (_aoi.ViewRadius + (this.TileSize * 2f)) * (_aoi.ViewRadius + (this.TileSize * 2f));
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState player = kvp.Value;
            if (!_simulation.Entities.HasTransform(player.Entity))
            {
                continue;
            }

            if (Vector2.DistanceSquared(_simulation.Entities.GetTransform(player.Entity).Position, worldPos) <= radiusSq)
            {
                _flatRecipients.Add(kvp.Key);
            }
        }

        int count = _flatRecipients.Count - offset;
        if (count > 0)
        {
            _pendingCombatHits.Add(new OutboundCombatHit(
                offset,
                count,
                attackerNetId,
                defenderNetId,
                damage,
                remainingHealth,
                isCritical,
                isKilled));
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
            InventoryBlob = InventoryBinaryCodec.Serialize(state.Inventory),
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

        EntityKind kind = EntityKind.Player;
        string name = "Adventurer";
        ushort archetype = 0;

        if (_connectionByNetworkId.TryGetValue(networkId, out ulong ownerConn) &&
            _playersByConnection.TryGetValue(ownerConn, out ActivePlayerState? state))
        {
            name = state.Session.CharacterName;
            archetype = state.SourceCharacter.AppearancePreset;
        }
        else if (_actorsByNetworkId.TryGetValue(networkId, out NpcActorMeta meta))
        {
            kind = meta.Kind;
            name = meta.Name;
            archetype = meta.ArchetypeId;
        }

        snapshot = new EntitySpawnSnapshot(
            networkId,
            kind,
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

    private static byte ResolveSlotHarvestTier(InventoryContainer inventory, byte slotIndex)
    {
        if (slotIndex < inventory.Capacity)
        {
            ItemSlot slot = inventory.GetSlot(slotIndex);
            if (!slot.IsEmpty && StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def) && def.HarvestTier > 0)
            {
                return def.HarvestTier;
            }
        }

        return slotIndex switch
        {
            1 => 2,
            2 => 1,
            _ => 1,
        };
    }

    private static void AddItemTrackingChanges(
        InventoryContainer inventory,
        in ItemSlot incoming,
        List<(ushort SlotIndex, ItemSlot Slot)> changes)
    {
        int maxStack = StarterGameCatalog.GetMaxStackSize(incoming.ItemId);
        ReadOnlySpan<ItemSlot> before = inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        _ = inventory.TryAdd(in incoming, maxStack);
        CollectModifiedSlots(snapshot, inventory.Slots, changes);
    }

    private static void AddPendingSlotLoot(
        InventoryContainer inventory,
        in ItemSlot incoming,
        ulong connectionId,
        List<OutboundSlotChange> output)
    {
        int maxStack = StarterGameCatalog.GetMaxStackSize(incoming.ItemId);
        ReadOnlySpan<ItemSlot> before = inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        _ = inventory.TryAdd(in incoming, maxStack);
        ReadOnlySpan<ItemSlot> after = inventory.Slots;
        for (int i = 0; i < after.Length; i++)
        {
            if (snapshot[i] != after[i])
            {
                output.Add(new OutboundSlotChange(connectionId, (ushort)i, after[i]));
            }
        }
    }

    private static void RemoveItemTrackingChanges(
        InventoryContainer inventory,
        uint itemId,
        int quantity,
        List<(ushort SlotIndex, ItemSlot Slot)> changes)
    {
        ReadOnlySpan<ItemSlot> before = inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        _ = inventory.TryRemove(itemId, quantity);
        CollectModifiedSlots(snapshot, inventory.Slots, changes);
    }

    private static void CollectModifiedSlots(
        ReadOnlySpan<ItemSlot> before,
        ReadOnlySpan<ItemSlot> after,
        List<(ushort SlotIndex, ItemSlot Slot)> output)
    {
        for (int i = 0; i < after.Length; i++)
        {
            if (before[i] != after[i])
            {
                output.Add(((ushort)i, after[i]));
            }
        }
    }

    private static ItemSlot ResolvePropDrop(MapPropType propType)
    {
        if (propType is MapPropType.OakTree or MapPropType.PineTree or MapPropType.SnowPine or
            MapPropType.PalmTree or MapPropType.BirchTree or MapPropType.WillowTree or
            MapPropType.DeadTree or MapPropType.AcaciaTree or MapPropType.CherryBlossomTree)
        {
            return new ItemSlot(StarterGameCatalog.WoodLog, 3);
        }

        if (propType is MapPropType.CopperVein or MapPropType.IronVein or MapPropType.GoldVein or MapPropType.SilverVein)
        {
            return new ItemSlot(StarterGameCatalog.IronOre, 2);
        }

        if (propType is MapPropType.CrystalCluster or MapPropType.GlacialCrystal)
        {
            return new ItemSlot(StarterGameCatalog.ManaCrystal, 2);
        }

        if (propType is MapPropType.Bush or MapPropType.Cactus or MapPropType.MushroomCluster)
        {
            return new ItemSlot(StarterGameCatalog.BerryHerb, 2);
        }

        if (propType is MapPropType.Boulder or MapPropType.BasaltPillar or MapPropType.CoalDeposit)
        {
            return new ItemSlot(StarterGameCatalog.StoneChunk, 3);
        }

        return ItemSlot.Empty;
    }

    private static uint ResolveStructureKitItemId(StructureType structureType)
    {
        if (structureType == StructureType.WoodWall)
        {
            return StarterGameCatalog.WoodWallKit;
        }

        if (structureType == StructureType.StoneWall)
        {
            return StarterGameCatalog.StoneWallKit;
        }

        if (structureType == StructureType.WoodDoor)
        {
            return StarterGameCatalog.WoodDoorKit;
        }

        return 0U;
    }

    private static (string Name, ushort ArchetypeId, float MaxHp, float MoveSpeed, uint DropItemId) ResolveBiomeMonster(BiomeType biome)
    {
        if (biome is BiomeType.RedMesa or BiomeType.DuneDesert)
        {
            return ("Rock Golem", 102, 110f, 115f, StarterGameCatalog.IronOre);
        }

        if (biome is BiomeType.FrozenTundra or BiomeType.AlpinePeaks)
        {
            return ("Frost Wolf", 103, 85f, 150f, StarterGameCatalog.StoneChunk);
        }

        if (biome is BiomeType.VolcanicAshland or BiomeType.BlossomWoods)
        {
            return ("Ash Wraith", 104, 95f, 140f, StarterGameCatalog.ManaCrystal);
        }

        return ("Wild Slime", 101, 60f, 125f, StarterGameCatalog.BerryHerb);
    }

    private static ulong HashChunk(ulong seed, int cx, int cy)
    {
        ulong x = seed ^ ((uint)cx * 0x9E3779B97F4A7C15UL) ^ ((uint)cy * 0xBF58476D1CE4E5B9UL);
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return x;
    }

    private static (int X, int Y) UnpackKey(long key) =>
        ((int)(key >> 32), (int)(uint)key);

    private sealed class ActivePlayerState(
        ConnectedPlayerSession session,
        Entity entity,
        CharacterRecord sourceCharacter,
        InventoryContainer inventory)
    {
        public ConnectedPlayerSession Session { get; set; } = session;
        public Entity Entity { get; } = entity;
        public CharacterRecord SourceCharacter { get; set; } = sourceCharacter;
        public InventoryContainer Inventory { get; } = inventory;
        public CooldownTracker Cooldowns { get; } = new();
        public Vector2 PendingDirection { get; set; }
        public bool PendingSprint { get; set; }
        public uint LastClientTick { get; set; }
        public bool HasPendingInput { get; set; }
        public bool HasPendingAttack { get; set; }
        public ulong PendingAttackTargetId { get; set; }
        public Vector2 PendingAttackAim { get; set; }
        public byte PendingAttackAbility { get; set; }
    }

    private readonly record struct NpcActorMeta(
        EntityKind Kind,
        ushort ArchetypeId,
        string Name,
        Vector2 HomePos,
        float MaxHealth,
        float MoveSpeed,
        long ChunkKey,
        uint DropItemId);

    private readonly record struct PendingMonsterRespawn(ulong RespawnTick, NpcActorMeta Meta);
    private readonly record struct OutboundSpawn(ulong ConnectionId, EntitySpawnSnapshot Snapshot);
    private readonly record struct OutboundDespawn(ulong ConnectionId, ulong NetworkId, ulong ServerTick);
    private readonly record struct OutboundDelta(
        ulong ConnectionId,
        ulong ServerTick,
        uint LastClientTick,
        float SelfX,
        float SelfY,
        int Offset,
        int Count);

    private readonly record struct OutboundCombatHit(
        int RecipientsOffset,
        int RecipientsCount,
        ulong AttackerNetworkId,
        ulong DefenderNetworkId,
        float Damage,
        float RemainingHealth,
        bool IsCritical,
        bool IsKilled);

    private readonly record struct OutboundSlotChange(
        ulong ConnectionId,
        ushort SlotIndex,
        ItemSlot Slot);
}
