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
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using Carbon.Items.Systems;
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
    private const int PlayerInventoryCapacity = PlayerInventoryLayout.TotalCapacity;
    private const uint AttackAbilityId = 1U;
    private const uint Skill1AbilityId = 2U;
    private const uint Skill2AbilityId = 3U;
    private const uint Skill3AbilityId = 4U;
    private const byte SkillAbilityCodeOffset = 100;
    private const ulong LootLifetimeTicks = 30UL * 120UL;

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
    private readonly Dictionary<ulong, LootDropMeta> _lootByNetworkId = new();
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
    private readonly List<OutboundPropChange> _pendingPropChanges = new(16);
    private readonly List<OutboundStructureChange> _pendingStructureChanges = new(16);
    private readonly List<OutboundEquipmentChange> _pendingEquipmentChanges = new(16);
    private readonly List<OutboundStatsSync> _pendingStatsSyncs = new(32);
    private readonly List<OutboundSkillCast> _pendingSkillCasts = new(16);
    private readonly List<ulong> _flatRecipients = new(256);
    private readonly List<ulong> _aoiRecipientsScratch = new(64);
    private readonly List<Entity> _dirtyResetScratch = new(256);
    private readonly List<ulong> _staleLootScratch = new(32);
    private readonly List<(ushort SlotIndex, ItemSlot Slot)> _mutationSlotScratch = new(8);
    private readonly Entity[] _combatHitScratch = new Entity[32];
    private readonly Entity[] _packAggroScratch = new Entity[16];

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
        _simulation.RegisterSystem(new LootExpirationSystem());
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

            InventoryContainer inventory = new(PlayerInventoryCapacity);
            if (!InventoryBinaryCodec.TryDeserializeInto(character.InventoryBlob, inventory))
            {
                StarterGameCatalog.GrantStarterLoadout(inventory);
            }

            ushort startLevel = Math.Max((ushort)1, character.Level);
            DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(startLevel, inventory);
            float startHp = MathF.Min(stats.MaxHealth, MathF.Max(1f, character.CurrentHealth));
            _simulation.Entities.SetHealth(entity, new HealthComponent(startHp, stats.MaxHealth));

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

            ActivePlayerState playerState = new(session, entity, character, inventory)
            {
                Level = startLevel,
                Experience = character.Experience,
                Stats = stats,
                CurrentMana = Math.Clamp(character.CurrentMana > 0f ? character.CurrentMana : stats.MaxMana, 0f, stats.MaxMana),
                CurrentStamina = Math.Clamp(character.CurrentStamina > 0f ? character.CurrentStamina : stats.MaxStamina, 0f, stats.MaxStamina),
                StatsDirty = true,
            };

            _playersByConnection[connectionId] = playerState;
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
                this.TickRate,
                character.AppearancePreset);
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

        ushort eqNetHead;
        ushort eqNetChest;
        ushort eqNetLegs;
        ushort eqNetFeet;
        ushort eqNetWeapon;
        byte eqWeaponUpg;
        ushort eqLevel;
        ulong netId;
        bool broadcastEq = false;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            byte tier = ResolveSlotHarvestTier(state.Inventory, slotIndex);
            state.Session = state.Session with { SelectedHotbarSlot = slotIndex, SelectedHarvestTier = tier };
            this.MarkCharacterDirtyLocked(state);

            eqNetHead = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
            eqNetChest = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
            eqNetLegs = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
            eqNetFeet = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
            ItemSlot activeWeapon = slotIndex < state.Inventory.Capacity ? state.Inventory.GetSlot(slotIndex) : ItemSlot.Empty;
            eqNetWeapon = (ushort)activeWeapon.ItemId;
            eqWeaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in activeWeapon);
            eqLevel = state.Level;
            netId = state.Session.PlayerNetworkId;

            if (_simulation.Entities.HasTransform(state.Entity))
            {
                Vector2 pPos = _simulation.Entities.GetTransform(state.Entity).Position;
                this.CollectNearbyConnectionsAroundWorldLocked(pPos, _aoiRecipientsScratch);
                broadcastEq = _aoiRecipientsScratch.Count > 0;
            }
        }

        if (broadcastEq)
        {
            _outboundSink.SendEntityEquipmentChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                netId,
                eqNetHead,
                eqNetChest,
                eqNetLegs,
                eqNetFeet,
                eqNetWeapon,
                eqWeaponUpg,
                eqLevel);
        }

        return OpStatus.Ok;
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
            this.GrantExperienceLocked(state, (uint)(15 * crafted), out _);
            this.MarkCharacterDirtyLocked(state);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(
                connectionId,
                idx,
                slot.ItemId,
                slot.Quantity,
                slot.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        }

        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public OpStatus TryMoveInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot)
    {
        bool statsChanged = false;
        bool leveledUp = false;
        bool broadcastEq = false;
        ushort headId = 0, chestId = 0, legsId = 0, feetId = 0, weaponId = 0, level = 1;
        byte weaponUpg = 0;
        ulong netId = 0UL;
        ActivePlayerState? snapState = null;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            if (fromSlot >= state.Inventory.Capacity || toSlot >= state.Inventory.Capacity)
            {
                return OpStatus.Invalid;
            }

            _mutationSlotScratch.Clear();
            ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
            Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
            before.CopyTo(beforeSnap);

            if (!PlayerInventoryLayout.TryMoveOrEquipSlot(state.Inventory, fromSlot, toSlot))
            {
                return OpStatus.Invalid;
            }

            CollectModifiedSlots(beforeSnap, state.Inventory.Slots, _mutationSlotScratch);
            byte newTier = ResolveSlotHarvestTier(state.Inventory, state.Session.SelectedHotbarSlot);
            state.Session = state.Session with { SelectedHarvestTier = newTier };

            if (fromSlot >= PlayerInventoryLayout.BagLimit ||
                toSlot >= PlayerInventoryLayout.BagLimit ||
                fromSlot == state.Session.SelectedHotbarSlot ||
                toSlot == state.Session.SelectedHotbarSlot)
            {
                this.RecomputeDerivedStatsLocked(state);
                statsChanged = true;
                snapState = state;
                headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
                chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
                legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
                feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
                ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
                weaponId = (ushort)held.ItemId;
                weaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in held);
                level = state.Level;
                netId = state.Session.PlayerNetworkId;

                if (_simulation.Entities.HasTransform(state.Entity))
                {
                    this.CollectNearbyConnectionsAroundWorldLocked(_simulation.Entities.GetTransform(state.Entity).Position, _aoiRecipientsScratch);
                    broadcastEq = _aoiRecipientsScratch.Count > 0;
                }
            }

            this.MarkCharacterDirtyLocked(state);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(
                connectionId,
                idx,
                slot.ItemId,
                slot.Quantity,
                slot.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        }

        if (statsChanged && snapState is not null)
        {
            this.DispatchImmediateStatsSync(snapState, leveledUp);
        }

        if (broadcastEq)
        {
            _outboundSink.SendEntityEquipmentChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                netId,
                headId,
                chestId,
                legsId,
                feetId,
                weaponId,
                weaponUpg,
                level);
        }

        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public OpStatus TryUpgradeInventoryItem(ulong connectionId, ushort slotIndex, uint deterministicRollSeed = 0U)
    {
        ActivePlayerState? snapState = null;
        bool broadcastEq = false;
        ushort headId = 0, chestId = 0, legsId = 0, feetId = 0, weaponId = 0, level = 1;
        byte weaponUpg = 0;
        ulong netId = 0UL;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            if (slotIndex >= state.Inventory.Capacity)
            {
                return OpStatus.Invalid;
            }

            ItemSlot target = state.Inventory.GetSlot(slotIndex);
            if (!ItemEnhancementPolicy.CanUpgrade(in target, out uint mat1, out int c1, out uint mat2, out int c2, out float successChance))
            {
                return OpStatus.Invalid;
            }

            if ((mat1 != 0U && state.Inventory.CountItem(mat1) < c1) ||
                (mat2 != 0U && state.Inventory.CountItem(mat2) < c2))
            {
                return OpStatus.Conflict;
            }

            _mutationSlotScratch.Clear();
            ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
            Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
            before.CopyTo(beforeSnap);

            if (mat1 != 0U)
            {
                _ = state.Inventory.TryRemove(mat1, c1);
            }

            if (mat2 != 0U)
            {
                _ = state.Inventory.TryRemove(mat2, c2);
            }

            double roll = deterministicRollSeed != 0U
                ? HashChunk(this.WorldSeed ^ deterministicRollSeed, slotIndex, (int)_currentTick) % 1000UL / 1000.0
                : Random.Shared.NextDouble();

            bool succeeded = roll <= successChance;
            if (succeeded && StarterGameCatalog.Items.TryGet(target.ItemId, out ItemArchetype? def))
            {
                byte nextLevel = (byte)(ItemEnhancementPolicy.GetUpgradeLevel(in target) + 1);
                float nextDur = ItemEnhancementPolicy.GetEffectiveMaxDurability(def, nextLevel);
                ItemSlot enhanced = ItemEnhancementPolicy.WithUpgradeLevel(in target, nextLevel) with { Durability = nextDur };
                state.Inventory.SetSlot(slotIndex, in enhanced);
                this.GrantExperienceLocked(state, (uint)(20 + (nextLevel * 5)), out _);
            }

            CollectModifiedSlots(beforeSnap, state.Inventory.Slots, _mutationSlotScratch);
            this.RecomputeDerivedStatsLocked(state);
            snapState = state;

            headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
            chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
            legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
            feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
            ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
            weaponId = (ushort)held.ItemId;
            weaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in held);
            level = state.Level;
            netId = state.Session.PlayerNetworkId;

            if (_simulation.Entities.HasTransform(state.Entity) &&
                (slotIndex >= PlayerInventoryLayout.BagLimit || slotIndex == state.Session.SelectedHotbarSlot))
            {
                this.CollectNearbyConnectionsAroundWorldLocked(_simulation.Entities.GetTransform(state.Entity).Position, _aoiRecipientsScratch);
                broadcastEq = _aoiRecipientsScratch.Count > 0;
            }

            this.MarkCharacterDirtyLocked(state);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(
                connectionId,
                idx,
                slot.ItemId,
                slot.Quantity,
                slot.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        }

        if (snapState is not null)
        {
            this.DispatchImmediateStatsSync(snapState, false);
        }

        if (broadcastEq)
        {
            _outboundSink.SendEntityEquipmentChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                netId,
                headId,
                chestId,
                legsId,
                feetId,
                weaponId,
                weaponUpg,
                level);
        }

        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity)
    {
        ActivePlayerState? snapState;
        bool broadcastEq = false;
        ushort headId = 0, chestId = 0, legsId = 0, feetId = 0, weaponId = 0, level = 1;
        byte weaponUpg = 0;
        ulong netId = 0UL;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            if (slotIndex >= state.Inventory.Capacity)
            {
                return OpStatus.Invalid;
            }

            ItemSlot slot = state.Inventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
            {
                return OpStatus.NotFound;
            }

            int dropQty = quantity <= 0 || quantity >= slot.Quantity ? slot.Quantity : quantity;
            ItemSlot dropStack = slot with { Quantity = dropQty };
            ItemSlot remainder = slot.Quantity > dropQty ? slot with { Quantity = slot.Quantity - dropQty } : ItemSlot.Empty;
            state.Inventory.SetSlot(slotIndex, in remainder);

            _mutationSlotScratch.Clear();
            _mutationSlotScratch.Add((slotIndex, remainder));

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            this.SpawnGroundLootLocked(playerPos + new Vector2(28f, 6f), in dropStack);

            this.RecomputeDerivedStatsLocked(state);
            snapState = state;
            if (slotIndex >= PlayerInventoryLayout.BagLimit || slotIndex == state.Session.SelectedHotbarSlot)
            {
                headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
                chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
                legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
                feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
                ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
                weaponId = (ushort)held.ItemId;
                weaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in held);
                level = state.Level;
                netId = state.Session.PlayerNetworkId;
                this.CollectNearbyConnectionsAroundWorldLocked(playerPos, _aoiRecipientsScratch);
                broadcastEq = _aoiRecipientsScratch.Count > 0;
            }

            this.MarkCharacterDirtyLocked(state);
        }

        for (int i = 0; i < _mutationSlotScratch.Count; i++)
        {
            (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
            _outboundSink.SendInventorySlotChanged(
                connectionId,
                idx,
                slot.ItemId,
                slot.Quantity,
                slot.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        }

        if (snapState is not null)
        {
            this.DispatchImmediateStatsSync(snapState, false);
        }

        if (broadcastEq)
        {
            _outboundSink.SendEntityEquipmentChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                netId,
                headId,
                chestId,
                legsId,
                feetId,
                weaponId,
                weaponUpg,
                level);
        }

        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public OpStatus EnqueuePickupLoot(ulong connectionId, ulong lootNetworkId)
    {
        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            state.PendingPickupLootId = lootNetworkId;
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

            if (!_chunkWorld.TryGetPropAt(tileX, tileY, out MapProp existing) ||
                existing.Type == MapPropType.None ||
                existing.Destructibility.IsDestroyed)
            {
                return new WorldMutationOutcome(OpStatus.NotFound, 0);
            }

            if (!existing.Destructibility.IsDestructible)
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
                ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
                : ItemSlot.Empty;
            ToolTargetKind targetKind = ToToolTargetKind(existing.Category);
            PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                in activeSlot,
                targetKind,
                existing.Destructibility.HarvestTier);

            _mutationSlotScratch.Clear();
            if (eval.DurabilityLoss > 0f &&
                this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, eval.DurabilityLoss, out ItemSlot degradedSlot))
            {
                _mutationSlotScratch.Add((state.Session.SelectedHotbarSlot, degradedSlot));
            }

            if (!_chunkWorld.TryDamagePropAt(tileX, tileY, eval.Damage, eval.EffectiveTier, respawnAt, out MapProp updated))
            {
                this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    uint currentRev = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? curDelta) && curDelta is not null
                        ? curDelta.Revision
                        : 0U;
                    _outboundSink.SendPropChanged(
                        CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                        tileX,
                        tileY,
                        currentRev,
                        existing.Destructibility.CurrentDurability,
                        isDestroyed: false);
                }

                for (int i = 0; i < _mutationSlotScratch.Count; i++)
                {
                    (ushort idx, ItemSlot slot) = _mutationSlotScratch[i];
                    _outboundSink.SendInventorySlotChanged(
                        connectionId,
                        idx,
                        slot.ItemId,
                        slot.Quantity,
                        slot.Durability,
                        ItemEnhancementPolicy.GetUpgradeLevel(in slot));
                }

                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            durability = updated.Destructibility.CurrentDurability;
            isDestroyed = updated.Destructibility.IsDestroyed;

            if (isDestroyed)
            {
                ItemSlot drop = ResolvePropDrop(updated.Type);
                if (!drop.IsEmpty)
                {
                    Vector2 propDropPos = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
                    this.SpawnGroundLootLocked(propDropPos, in drop);
                }

                this.GrantExperienceLocked(state, 15U, out _);
                this.MarkCharacterDirtyLocked(state);
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
            _outboundSink.SendInventorySlotChanged(
                connectionId,
                idx,
                slot.ItemId,
                slot.Quantity,
                slot.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slot));
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
            _pendingPropChanges.Clear();
            _pendingStructureChanges.Clear();
            _pendingEquipmentChanges.Clear();
            _pendingStatsSyncs.Clear();
            _pendingSkillCasts.Clear();
            _flatRecipients.Clear();
            _dirtyResetScratch.Clear();

            _currentTick++;
            float dt = 1f / this.TickRate;

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState state = kvp.Value;
                bool isMoving;
                bool isSprinting = false;
                if (state.HasPendingInput)
                {
                    isMoving = state.PendingDirection.LengthSquared() > 1e-4f;
                    isSprinting = isMoving && state.PendingSprint && state.CurrentStamina > 2f;
                    if (isSprinting)
                    {
                        state.CurrentStamina = MathF.Max(0f, state.CurrentStamina - (22f * dt));
                        state.StatsDirty = true;
                    }

                    float speed = isSprinting ? SprintSpeed : BaseWalkSpeed;
                    _simulation.EnqueueCommand(SimulationCommand.MoveInput(
                        state.Entity,
                        state.PendingDirection,
                        speed,
                        state.LastClientTick));
                    state.HasPendingInput = false;
                }

                if (!isSprinting && state.CurrentStamina < state.Stats.MaxStamina)
                {
                    state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + (18f * dt));
                }

                if (state.CurrentMana < state.Stats.MaxMana)
                {
                    state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + (6f * dt));
                }

                if (state.PendingPickupLootId != 0UL)
                {
                    _ = this.TryPickupSpecificLootLocked(state, state.PendingPickupLootId, 68f * 68f);
                    state.PendingPickupLootId = 0UL;
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
            this.CleanupExpiredLootMetaLocked();

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

                // Out-of-combat natural HP regeneration (+3 HP/s after 6s without taking combat hits).
                if ((_currentTick % this.TickRate) == 0UL &&
                    (_currentTick - observer.LastCombatDamageTick) >= (ulong)(this.TickRate * 6) &&
                    _simulation.Entities.HasHealth(observer.Entity))
                {
                    HealthComponent php = _simulation.Entities.GetHealth(observer.Entity);
                    if (php.Current > 0f && php.Current < php.Max)
                    {
                        float healed = MathF.Min(php.Max, php.Current + 3f);
                        _simulation.Entities.SetHealth(observer.Entity, new HealthComponent(healed, php.Max));
                        _simulation.MarkDirty(observer.Entity, EntityDirtyMask.Health);
                        this.MarkCharacterDirtyLocked(observer);
                    }
                }

                // Friendly NPCs near settlements slowly mend damaged tools and blades within 110px.
                if ((_currentTick % (ulong)(this.TickRate * 2)) == 0UL &&
                    this.HasNearbyFriendlyNpcLocked(obsPos, 110f * 110f))
                {
                    for (int sIdx = 0; sIdx < observer.Inventory.Capacity; sIdx++)
                    {
                        ItemSlot curSlot = observer.Inventory.GetSlot(sIdx);
                        if (curSlot.IsEmpty ||
                            !StarterGameCatalog.Items.TryGet(curSlot.ItemId, out ItemArchetype? itemDef) ||
                            itemDef.MaxDurability <= 0f ||
                            curSlot.Durability >= itemDef.MaxDurability)
                        {
                            continue;
                        }

                        float mendedDur = MathF.Min(itemDef.MaxDurability, curSlot.Durability + 12f);
                        ItemSlot repaired = curSlot with { Durability = mendedDur };
                        observer.Inventory.SetSlot(sIdx, in repaired);
                        _pendingSlotChanges.Add(new OutboundSlotChange(observer.Session.ConnectionId, (ushort)sIdx, repaired));
                        this.MarkCharacterDirtyLocked(observer);
                    }
                }

                // Auto-collect ground loot within 32px after brief spawn grace delay.
                this.CollectNearbyGroundLootLocked(observer, obsPos);

                if (observer.StatsDirty || (_currentTick % 10UL) == 0UL)
                {
                    this.EnqueueStatsSyncLocked(observer, observer.LeveledUpPending);
                    observer.StatsDirty = false;
                    observer.LeveledUpPending = false;
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
            ItemSlot slotCopy = change.Slot;
            _outboundSink.SendInventorySlotChanged(
                change.ConnectionId,
                change.SlotIndex,
                slotCopy.ItemId,
                slotCopy.Quantity,
                slotCopy.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slotCopy));
        }

        for (int i = 0; i < _pendingPropChanges.Count; i++)
        {
            OutboundPropChange prop = _pendingPropChanges[i];
            _outboundSink.SendPropChanged(
                recipientsSpan.Slice(prop.RecipientsOffset, prop.RecipientsCount),
                prop.TileX,
                prop.TileY,
                prop.Revision,
                prop.Durability,
                prop.IsDestroyed);
        }

        for (int i = 0; i < _pendingStructureChanges.Count; i++)
        {
            OutboundStructureChange st = _pendingStructureChanges[i];
            _outboundSink.SendStructureChanged(
                recipientsSpan.Slice(st.RecipientsOffset, st.RecipientsCount),
                st.TileX,
                st.TileY,
                st.StructureType,
                st.Durability,
                st.DoorState,
                st.Connectivity);
        }

        for (int i = 0; i < _pendingEquipmentChanges.Count; i++)
        {
            OutboundEquipmentChange eq = _pendingEquipmentChanges[i];
            _outboundSink.SendEntityEquipmentChanged(
                recipientsSpan.Slice(eq.RecipientsOffset, eq.RecipientsCount),
                eq.NetworkId,
                eq.HeadId,
                eq.ChestId,
                eq.LegsId,
                eq.FeetId,
                eq.WeaponId,
                eq.WeaponUpgrade,
                eq.Level);
        }

        for (int i = 0; i < _pendingStatsSyncs.Count; i++)
        {
            OutboundStatsSync sync = _pendingStatsSyncs[i];
            _outboundSink.SendPlayerStatsSync(
                sync.ConnectionId,
                sync.Level,
                sync.Experience,
                sync.XpToNext,
                sync.CurHp,
                sync.MaxHp,
                sync.CurMp,
                sync.MaxMp,
                sync.CurStam,
                sync.MaxStam,
                sync.Defense,
                sync.Skill1Cd,
                sync.Skill2Cd,
                sync.Skill3Cd,
                sync.LeveledUp);
        }

        for (int i = 0; i < _pendingSkillCasts.Count; i++)
        {
            OutboundSkillCast cast = _pendingSkillCasts[i];
            _outboundSink.SendSkillCastBroadcast(
                recipientsSpan.Slice(cast.RecipientsOffset, cast.RecipientsCount),
                cast.CasterNetworkId,
                cast.AbilitySlot,
                cast.OriginX,
                cast.OriginY,
                cast.AimX,
                cast.AimY);
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
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        if (state.PendingAttackAbility >= SkillAbilityCodeOffset)
        {
            byte skillSlot = (byte)(state.PendingAttackAbility - SkillAbilityCodeOffset);
            this.ProcessActiveSkillLocked(state, skillSlot);
            return;
        }

        if (!state.Cooldowns.IsReady(AttackAbilityId, _currentTick))
        {
            return;
        }

        state.Cooldowns.StartCooldown(AttackAbilityId, _currentTick, (uint)Math.Max(10, this.TickRate / 2));

        if (state.PendingAttackAbility < 9 && state.PendingAttackAbility != state.Session.SelectedHotbarSlot)
        {
            byte tier = ResolveSlotHarvestTier(state.Inventory, state.PendingAttackAbility);
            state.Session = state.Session with { SelectedHotbarSlot = state.PendingAttackAbility, SelectedHarvestTier = tier };
            this.MarkCharacterDirtyLocked(state);
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        bool hasArchetype = StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype);

        // Consumable quick-use on primary action (Berry Herb +25 HP, Mana Tonic +45 MP & +30 Stamina).
        if (activeSlot.ItemId == StarterGameCatalog.ManaTonic && activeSlot.Quantity > 0)
        {
            if (state.CurrentMana < state.Stats.MaxMana || state.CurrentStamina < state.Stats.MaxStamina)
            {
                state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + 45f);
                state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + 30f);
                state.StatsDirty = true;

                ItemSlot updatedSlot = activeSlot.Quantity > 1
                    ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
                    : ItemSlot.Empty;
                state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
                _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
                this.MarkCharacterDirtyLocked(state);
                return;
            }
        }

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
                state.StatsDirty = true;

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

        // Placeable quick-build on primary action (Wood/Stone wall & door kits).
        if (activeSlot.Quantity > 0 && hasArchetype && archetype is { Category: ItemCategory.Placeable })
        {
            this.ProcessPlaceablePrimaryActionLocked(state, attackerPos, activeSlot, archetype);
            return;
        }

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

        if (hitCount == 0)
        {
            this.ProcessToolHarvestFallbackLocked(state, in activeSlot);
            return;
        }

        CombatStrikeEvaluation combatEval = ToolUsagePolicy.EvaluateCombatStrike(in activeSlot);
        bool isCrit = activeSlot.ItemId == StarterGameCatalog.IronSword &&
            combatEval.Outcome == ToolStrikeOutcome.Optimal &&
            ((_currentTick + state.Session.PlayerNetworkId) % 5UL == 0UL);
        float scaledDamage = combatEval.Damage * state.Stats.AttackMultiplier;
        float finalDamage = isCrit ? scaledDamage * 1.5f : scaledDamage;
        EntityAttachmentStore<ThreatTable> threatStore = _simulation.Entities.GetAttachmentStore<ThreatTable>();
        bool anyDamaged = false;

        for (int i = 0; i < hitCount; i++)
        {
            Entity target = _combatHitScratch[i];
            if (!_simulation.Entities.IsAlive(target) ||
                !_simulation.Entities.HasHealth(target) ||
                !_simulation.TryGetNetworkIdentity(target, out NetworkIdentity targetIdentity))
            {
                continue;
            }

            anyDamaged = true;

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
                    brain = brain with { State = AiState.Chase, TargetEntity = state.Entity, RepathCooldownTicks = 0 };
                }

                if (_simulation.Entities.HasVelocity(target))
                {
                    ref Velocity2D targetVel = ref _simulation.Entities.GetVelocity(target);
                    targetVel.Linear = aimDir * 135f;
                    _simulation.MarkDirty(target, EntityDirtyMask.Velocity);
                }

                // Pack social aggro: alert nearby allied monsters within 160px to assist.
                _simulation.OverlapCircle(targetPos, 160f, _packAggroScratch, out int allyCount, MonsterLayer);
                for (int a = 0; a < allyCount; a++)
                {
                    Entity ally = _packAggroScratch[a];
                    if (ally == target || !_simulation.Entities.IsAlive(ally) || !_simulation.Entities.HasComponent<AiBrainComponent>(ally))
                    {
                        continue;
                    }

                    threatStore.GetOrAdd(ally, static () => new ThreatTable()).AddThreat(state.Entity, finalDamage * 0.8f);
                    ref AiBrainComponent allyBrain = ref _simulation.Entities.GetComponent<AiBrainComponent>(ally);
                    if (allyBrain.State is AiState.Idle or AiState.Patrol)
                    {
                        allyBrain = allyBrain with { State = AiState.Chase, TargetEntity = state.Entity, RepathCooldownTicks = 0 };
                    }
                }
            }
            else
            {
                if (_actorsByNetworkId.Remove(targetIdentity.NetworkId, out NpcActorMeta meta))
                {
                    _pendingRespawns.Add(new PendingMonsterRespawn(_currentTick + (ulong)(this.TickRate * 45), meta));
                    ItemSlot loot = new(meta.DropItemId, 2);
                    this.SpawnGroundLootLocked(targetPos, in loot);
                    uint xpReward = (uint)Math.Clamp((int)(meta.MaxHealth * 0.45f), 25, 90);
                    this.GrantExperienceLocked(state, xpReward, out _);
                    this.MarkCharacterDirtyLocked(state);
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

        if (anyDamaged &&
            combatEval.DurabilityLoss > 0f &&
            this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, combatEval.DurabilityLoss, out ItemSlot degradedCombatSlot))
        {
            _pendingSlotChanges.Add(new OutboundSlotChange(
                state.Session.ConnectionId,
                state.Session.SelectedHotbarSlot,
                degradedCombatSlot));
        }
    }

    private void ProcessPlaceablePrimaryActionLocked(
        ActivePlayerState state,
        Vector2 attackerPos,
        ItemSlot activeSlot,
        ItemArchetype archetype)
    {
        StructureType structureType = (StructureType)archetype.PlaceableStructureType;
        if (structureType == StructureType.None)
        {
            return;
        }

        int ptx = (int)MathF.Floor(attackerPos.X / this.TileSize);
        int pty = (int)MathF.Floor(attackerPos.Y / this.TileSize);
        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / this.TileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / this.TileSize);

        if (!this.IsWithinInteractRangeLocked(state.Entity, aimTx, aimTy))
        {
            return;
        }

        // Prevent solid walls from trapping the player on their own occupied tile.
        if ((structureType is StructureType.WoodWall or StructureType.StoneWall) && aimTx == ptx && aimTy == pty)
        {
            return;
        }

        MapTile tile = _chunkWorld.GetTileAtWorld(aimTx, aimTy);
        bool hasProp = _chunkWorld.HasSolidPropAt(aimTx, aimTy);
        if (_chunkWorld.Buildings.TryPlaceStructure(aimTx, aimTy, structureType, tile.Biome, hasProp, out StructureTile placed)
            != PlacementValidationResult.Success)
        {
            return;
        }

        ItemSlot updatedSlot = activeSlot.Quantity > 1
            ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
            : ItemSlot.Empty;
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
        this.MarkCharacterDirtyLocked(state);

        int cx = Chunk2D.ToChunkCoord(aimTx);
        int cy = Chunk2D.ToChunkCoord(aimTy);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));

        (int offset, int count) = this.AppendTileRecipientsLocked(aimTx, aimTy);
        if (count > 0)
        {
            _pendingStructureChanges.Add(new OutboundStructureChange(
                offset,
                count,
                aimTx,
                aimTy,
                (byte)placed.Type,
                placed.CurrentDurability,
                (byte)placed.DoorState,
                (byte)placed.Connectivity));
        }
    }

    private void ProcessToolHarvestFallbackLocked(ActivePlayerState state, in ItemSlot activeSlot)
    {
        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / this.TileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / this.TileSize);
        ulong respawnAt = _currentTick + (ulong)(this.TickRate * 30);

        int blockedTx = 0;
        int blockedTy = 0;
        MapProp blockedProp = default;
        bool hasBlocked = false;

        if (this.TryDamagePropAndQueueLocked(state, aimTx, aimTy, in activeSlot, respawnAt, ref blockedProp))
        {
            return;
        }

        if (blockedProp.Type != MapPropType.None)
        {
            blockedTx = aimTx;
            blockedTy = aimTy;
            hasBlocked = true;
        }

        if (!hasBlocked)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int wx = aimTx + dx;
                    int wy = aimTy + dy;
                    MapProp localBlocked = default;
                    if (this.TryDamagePropAndQueueLocked(state, wx, wy, in activeSlot, respawnAt, ref localBlocked))
                    {
                        return;
                    }

                    if (localBlocked.Type != MapPropType.None)
                    {
                        blockedTx = wx;
                        blockedTy = wy;
                        blockedProp = localBlocked;
                        hasBlocked = true;
                        break;
                    }
                }

                if (hasBlocked)
                {
                    break;
                }
            }
        }

        if (hasBlocked)
        {
            int cx = Chunk2D.ToChunkCoord(blockedTx);
            int cy = Chunk2D.ToChunkCoord(blockedTy);
            uint revision = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null
                ? delta.Revision
                : 0U;

            (int offset, int count) = this.AppendTileRecipientsLocked(blockedTx, blockedTy);
            if (count > 0)
            {
                _pendingPropChanges.Add(new OutboundPropChange(
                    offset,
                    count,
                    blockedTx,
                    blockedTy,
                    revision,
                    blockedProp.Destructibility.CurrentDurability,
                    false));
            }
        }
    }

    private bool TryDamagePropAndQueueLocked(
        ActivePlayerState state,
        int wx,
        int wy,
        in ItemSlot activeSlot,
        ulong respawnAt,
        ref MapProp tierBlockedProp)
    {
        if (!this.IsWithinInteractRangeLocked(state.Entity, wx, wy))
        {
            return false;
        }

        if (!_chunkWorld.TryGetPropAt(wx, wy, out MapProp existing) ||
            existing.Type == MapPropType.None ||
            existing.Destructibility.IsDestroyed ||
            !existing.Destructibility.IsDestructible)
        {
            return false;
        }

        ToolTargetKind targetKind = ToToolTargetKind(existing.Category);
        PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
            in activeSlot,
            targetKind,
            existing.Destructibility.HarvestTier);

        if (eval.DurabilityLoss > 0f &&
            this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, eval.DurabilityLoss, out ItemSlot degradedSlot))
        {
            _pendingSlotChanges.Add(new OutboundSlotChange(
                state.Session.ConnectionId,
                state.Session.SelectedHotbarSlot,
                degradedSlot));
        }

        if (!_chunkWorld.TryDamagePropAt(wx, wy, eval.Damage, eval.EffectiveTier, respawnAt, out MapProp updated))
        {
            if (eval.Outcome != ToolStrikeOutcome.Optimal || eval.EffectiveTier < existing.Destructibility.HarvestTier)
            {
                tierBlockedProp = existing;
            }

            return false;
        }

        int cx = Chunk2D.ToChunkCoord(wx);
        int cy = Chunk2D.ToChunkCoord(wy);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
        uint revision = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null
            ? delta.Revision
            : 0U;

        if (updated.Destructibility.IsDestroyed)
        {
            ItemSlot drop = ResolvePropDrop(updated.Type);
            if (!drop.IsEmpty)
            {
                Vector2 propDropPos = new((wx + 0.5f) * this.TileSize, (wy + 0.5f) * this.TileSize);
                this.SpawnGroundLootLocked(propDropPos, in drop);
            }

            this.GrantExperienceLocked(state, 15U, out _);
            this.MarkCharacterDirtyLocked(state);
        }

        (int offset, int count) = this.AppendTileRecipientsLocked(wx, wy);
        if (count > 0)
        {
            _pendingPropChanges.Add(new OutboundPropChange(
                offset,
                count,
                wx,
                wy,
                revision,
                updated.Destructibility.CurrentDurability,
                updated.Destructibility.IsDestroyed));
        }

        return true;
    }

    private (int Offset, int Count) AppendTileRecipientsLocked(int tileX, int tileY)
    {
        this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
        int offset = _flatRecipients.Count;
        int count = _aoiRecipientsScratch.Count;
        for (int i = 0; i < count; i++)
        {
            _flatRecipients.Add(_aoiRecipientsScratch[i]);
        }

        return (offset, count);
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
            if (Vector2.DistanceSquared(atkPos, defPos) > (60f * 60f))
            {
                continue;
            }

            HealthComponent hp = _simulation.Entities.GetHealth(evt.Target);
            float rawDamage = _actorsByNetworkId.TryGetValue(attackerNet.NetworkId, out NpcActorMeta atkMeta)
                ? atkMeta.AttackDamage
                : 10f;
            float monsterDamage = rawDamage;
            if (_connectionByNetworkId.TryGetValue(targetNet.NetworkId, out ulong targetConn) &&
                _playersByConnection.TryGetValue(targetConn, out ActivePlayerState? victim))
            {
                monsterDamage = CharacterProgressionPolicy.MitigateDamage(rawDamage, victim.Stats.Defense);
                victim.LastCombatDamageTick = _currentTick;
                victim.StatsDirty = true;
                this.DegradeEquippedArmorLocked(victim, 0.5f);
            }

            float nextHp = MathF.Max(0f, hp.Current - monsterDamage);
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
            this.QueueCombatHitLocked(defPos, attackerNet.NetworkId, targetNet.NetworkId, monsterDamage, killed ? hp.Max : nextHp, false, killed);
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
                            ushort npcArchetype = poi.Type == PointOfInterestType.Harbor
                                ? (ushort)202
                                : poi.Type == PointOfInterestType.MountainShrine
                                    ? (ushort)203
                                    : (ushort)201;
                            Vector2 npcPos = new((npcTx + 0.5f) * this.TileSize, (npcTy + 0.5f) * this.TileSize);
                            NpcActorMeta npcMeta = new(
                                EntityKind.Npc,
                                npcArchetype,
                                npcName,
                                npcPos,
                                200f,
                                70f,
                                8f,
                                0f,
                                0U,
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
                int wx = (cx * Chunk2D.Size) + lx;
                int wy = (cy * Chunk2D.Size) + ly;
                MapTile spawnTile = chunk.GetTile(lx, ly);
                if (spawnTile.IsSolid ||
                    spawnTile.Biome == BiomeType.ShallowWater ||
                    spawnTile.Biome == BiomeType.DeepOcean ||
                    _chunkWorld.HasSolidPropAt(wx, wy))
                {
                    continue;
                }

                Vector2 monsterPos = new((wx + 0.5f) * this.TileSize, (wy + 0.5f) * this.TileSize);
                if (Vector2.DistanceSquared(monsterPos, safeOrigin) < safeRadiusSq)
                {
                    continue;
                }

                (string mobName, ushort archetypeId, float maxHp, float moveSpeed, float mobRadius, float atkDamage, uint windupTicks, uint dropId) =
                    ResolveBiomeMonster(spawnTile.Biome);
                NpcActorMeta monsterMeta = new(
                    EntityKind.Monster,
                    archetypeId,
                    mobName,
                    monsterPos,
                    maxHp,
                    moveSpeed,
                    mobRadius,
                    atkDamage,
                    windupTicks,
                    key,
                    dropId);
                this.SpawnActorFromMetaLocked(monsterMeta);

                // Spawn a pack companion nearby in ~25% of monster chunks.
                if (((h >> 16) & 3UL) == 0UL)
                {
                    int packLx = Math.Clamp(lx + 2, 1, Chunk2D.Size - 2);
                    int packLy = Math.Clamp(ly + 1, 1, Chunk2D.Size - 2);
                    int packWx = (cx * Chunk2D.Size) + packLx;
                    int packWy = (cy * Chunk2D.Size) + packLy;
                    MapTile packTile = chunk.GetTile(packLx, packLy);
                    if (!packTile.IsSolid &&
                        packTile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
                        !_chunkWorld.HasSolidPropAt(packWx, packWy))
                    {
                        Vector2 packPos = new((packWx + 0.5f) * this.TileSize, (packWy + 0.5f) * this.TileSize);
                        this.SpawnActorFromMetaLocked(monsterMeta with { HomePos = packPos });
                    }
                }
            }
        }
    }

    private void SpawnActorFromMetaLocked(in NpcActorMeta meta)
    {
        ulong netId = ++_nextNetworkId;
        float radius = meta.Radius > 0f ? meta.Radius : DefaultEntityRadius;
        Entity entity = _simulation.SpawnNetworkEntity(netId, meta.HomePos, radius, ownerSessionId: 0UL);
        uint layer = meta.Kind == EntityKind.Monster ? MonsterLayer : NpcLayer;
        _simulation.Entities.SetCollider(entity, new Collider2D(ColliderKind.Circle, radius, default, false, layer, uint.MaxValue));
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
            PatrolRadius: meta.Kind == EntityKind.Monster ? 96f : 64f,
            WindupTicks: meta.WindupTicks);
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
        HealthComponent health = _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity)
            : new HealthComponent(state.SourceCharacter.CurrentHealth, state.SourceCharacter.MaxHealth);

        CharacterRecord updated = state.SourceCharacter with
        {
            Level = state.Level,
            Experience = state.Experience,
            PositionX = pos.X,
            PositionY = pos.Y,
            CurrentHealth = health.Current,
            MaxHealth = health.Max,
            CurrentMana = state.CurrentMana,
            MaxMana = state.Stats.MaxMana,
            CurrentStamina = state.CurrentStamina,
            MaxStamina = state.Stats.MaxStamina,
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
        ushort headId = 0;
        ushort chestId = 0;
        ushort legsId = 0;
        ushort feetId = 0;
        ushort weaponId = 0;
        ushort level = 1;
        int lootCount = 0;
        byte weaponUpgrade = 0;

        if (_connectionByNetworkId.TryGetValue(networkId, out ulong ownerConn) &&
            _playersByConnection.TryGetValue(ownerConn, out ActivePlayerState? state))
        {
            name = state.Session.CharacterName;
            archetype = state.SourceCharacter.AppearancePreset;
            level = state.Level;
            headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
            chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
            legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
            feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
            ItemSlot held = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
                ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
                : ItemSlot.Empty;
            weaponId = (ushort)held.ItemId;
            weaponUpgrade = ItemEnhancementPolicy.GetUpgradeLevel(in held);
        }
        else if (_actorsByNetworkId.TryGetValue(networkId, out NpcActorMeta meta))
        {
            kind = meta.Kind;
            name = meta.Name;
            archetype = meta.ArchetypeId;
            level = meta.ArchetypeId == 102 ? (ushort)3 : (meta.ArchetypeId is 103 or 104 ? (ushort)2 : (ushort)1);
        }
        else if (_lootByNetworkId.TryGetValue(networkId, out LootDropMeta lootMeta))
        {
            kind = EntityKind.LootDrop;
            ItemSlot lootItem = lootMeta.Item;
            name = StarterGameCatalog.GetDisplayName(lootItem.ItemId);
            archetype = (ushort)lootItem.ItemId;
            lootCount = lootItem.Quantity;
            weaponUpgrade = ItemEnhancementPolicy.GetUpgradeLevel(in lootItem);
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
            maxHp,
            headId,
            chestId,
            legsId,
            feetId,
            weaponId,
            level,
            lootCount,
            weaponUpgrade);
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
            if (!slot.IsEmpty && StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def))
            {
                return def.HarvestTier;
            }
        }

        return 0;
    }

    private static ToolTargetKind ToToolTargetKind(PropCategory category) =>
        category == PropCategory.Mineral
            ? ToolTargetKind.Mineral
            : ToolTargetKind.Flora;

    private bool DegradeActiveSlotDurabilityLocked(
        ActivePlayerState state,
        in ItemSlot activeSlot,
        float loss,
        out ItemSlot updatedSlot)
    {
        updatedSlot = activeSlot;
        if (loss <= 0f ||
            activeSlot.IsEmpty ||
            !StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def) ||
            def.MaxDurability <= 0f)
        {
            return false;
        }

        float current = MathF.Max(0f, activeSlot.Durability);
        float next = MathF.Max(0f, current - loss);
        if (MathF.Abs(next - current) < 0.001f)
        {
            return false;
        }

        updatedSlot = activeSlot with { Durability = next };
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        this.MarkCharacterDirtyLocked(state);
        return true;
    }

    private bool HasNearbyFriendlyNpcLocked(Vector2 worldPos, float radiusSq)
    {
        foreach (KeyValuePair<ulong, NpcActorMeta> kv in _actorsByNetworkId)
        {
            if (kv.Value.Kind != EntityKind.Npc)
            {
                continue;
            }

            if (_simulation.TryGetEntityByNetworkId(kv.Key, out Entity npcEntity) &&
                _simulation.Entities.HasTransform(npcEntity) &&
                Vector2.DistanceSquared(worldPos, _simulation.Entities.GetTransform(npcEntity).Position) <= radiusSq)
            {
                return true;
            }
        }

        return false;
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

    private static (
        string Name,
        ushort ArchetypeId,
        float MaxHp,
        float MoveSpeed,
        float Radius,
        float AttackDamage,
        uint WindupTicks,
        uint DropItemId) ResolveBiomeMonster(BiomeType biome)
    {
        if (biome is BiomeType.RedMesa or BiomeType.DuneDesert)
        {
            return ("Rock Golem", 102, 150f, 105f, 11f, 16f, 11U, StarterGameCatalog.IronOre);
        }

        if (biome is BiomeType.FrozenTundra or BiomeType.AlpinePeaks)
        {
            return ("Frost Wolf", 103, 95f, 168f, 8.5f, 12f, 6U, StarterGameCatalog.StoneChunk);
        }

        if (biome is BiomeType.VolcanicAshland or BiomeType.BlossomWoods)
        {
            return ("Ash Wraith", 104, 110f, 148f, 8.5f, 14f, 8U, StarterGameCatalog.ManaCrystal);
        }

        return ("Wild Slime", 101, 75f, 130f, 8f, 9f, 7U, StarterGameCatalog.BerryHerb);
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

    private void SpawnGroundLootLocked(Vector2 worldPos, in ItemSlot item)
    {
        if (item.IsEmpty)
        {
            return;
        }

        ulong netId = ++_nextNetworkId;
        Entity lootEntity = _simulation.SpawnNetworkEntity(netId, worldPos, 10f);
        ulong despawnTick = _currentTick + LootLifetimeTicks;
        _simulation.Entities.SetComponent(lootEntity, new LootDropComponent(item, 0, despawnTick));
        _lootByNetworkId[netId] = new LootDropMeta(item, _currentTick);
    }

    private void CollectNearbyGroundLootLocked(ActivePlayerState observer, Vector2 obsPos)
    {
        if (_lootByNetworkId.Count == 0)
        {
            return;
        }

        _staleLootScratch.Clear();
        foreach (KeyValuePair<ulong, LootDropMeta> kv in _lootByNetworkId)
        {
            if (_currentTick < kv.Value.SpawnTick + 12UL)
            {
                continue;
            }

            if (_simulation.TryGetEntityByNetworkId(kv.Key, out Entity lootEnt) &&
                _simulation.Entities.IsAlive(lootEnt) &&
                _simulation.Entities.HasTransform(lootEnt) &&
                Vector2.DistanceSquared(obsPos, _simulation.Entities.GetTransform(lootEnt).Position) <= (32f * 32f))
            {
                _staleLootScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < _staleLootScratch.Count; i++)
        {
            _ = this.TryPickupSpecificLootLocked(observer, _staleLootScratch[i], 36f * 36f);
        }
    }

    private bool TryPickupSpecificLootLocked(ActivePlayerState state, ulong lootNetId, float maxRangeSq)
    {
        if (!_lootByNetworkId.TryGetValue(lootNetId, out LootDropMeta meta) ||
            !_simulation.TryGetEntityByNetworkId(lootNetId, out Entity lootEnt) ||
            !_simulation.Entities.IsAlive(lootEnt) ||
            !_simulation.Entities.HasTransform(lootEnt) ||
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return false;
        }

        Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 lootPos = _simulation.Entities.GetTransform(lootEnt).Position;
        if (Vector2.DistanceSquared(playerPos, lootPos) > maxRangeSq)
        {
            return false;
        }

        ItemSlot metaItem = meta.Item;
        int maxStack = StarterGameCatalog.GetMaxStackSize(metaItem.ItemId);
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        int remainder = PlayerInventoryLayout.TryAddBagItem(state.Inventory, in metaItem, maxStack);
        if (remainder >= metaItem.Quantity)
        {
            return false;
        }

        ReadOnlySpan<ItemSlot> after = state.Inventory.Slots;
        for (int i = 0; i < after.Length; i++)
        {
            if (snapshot[i] != after[i])
            {
                _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)i, after[i]));
            }
        }

        this.MarkCharacterDirtyLocked(state);

        if (remainder <= 0)
        {
            _ = _lootByNetworkId.Remove(lootNetId);
            _simulation.DespawnEntity(lootEnt);
        }
        else
        {
            ItemSlot left = meta.Item with { Quantity = remainder };
            _lootByNetworkId[lootNetId] = meta with { Item = left };
            _simulation.Entities.SetComponent(lootEnt, new LootDropComponent(left, 0, _currentTick + LootLifetimeTicks));
        }

        return true;
    }

    private void CleanupExpiredLootMetaLocked()
    {
        if (_lootByNetworkId.Count == 0)
        {
            return;
        }

        _staleLootScratch.Clear();
        foreach (KeyValuePair<ulong, LootDropMeta> kv in _lootByNetworkId)
        {
            if (!_simulation.TryGetEntityByNetworkId(kv.Key, out Entity ent) || !_simulation.Entities.IsAlive(ent))
            {
                _staleLootScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < _staleLootScratch.Count; i++)
        {
            _ = _lootByNetworkId.Remove(_staleLootScratch[i]);
        }
    }

    private void ProcessActiveSkillLocked(ActivePlayerState state, byte skillSlot)
    {
        if (!CharacterProgressionPolicy.TryGetSkillSpec(skillSlot, out _, out float mpCost, out float stamCost, out float cdSeconds))
        {
            return;
        }

        uint abilityId = skillSlot switch
        {
            CharacterProgressionPolicy.SkillWhirlwind => Skill1AbilityId,
            CharacterProgressionPolicy.SkillArcaneNova => Skill2AbilityId,
            CharacterProgressionPolicy.SkillVitalSurge => Skill3AbilityId,
            _ => 0U,
        };
        if (abilityId == 0U || !state.Cooldowns.IsReady(abilityId, _currentTick))
        {
            return;
        }

        if (state.CurrentMana < mpCost || state.CurrentStamina < stamCost)
        {
            return;
        }

        state.CurrentMana = MathF.Max(0f, state.CurrentMana - mpCost);
        state.CurrentStamina = MathF.Max(0f, state.CurrentStamina - stamCost);
        state.StatsDirty = true;
        state.Cooldowns.StartCooldown(abilityId, _currentTick, (uint)Math.Max(1, MathF.Ceiling(cdSeconds * this.TickRate)));

        Vector2 pos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 rawAim = state.PendingAttackAim - pos;
        Vector2 aimDir = rawAim.LengthSquared() > 1e-4f ? Vector2.Normalize(rawAim) : Vector2.UnitX;

        this.CollectNearbyConnectionsAroundWorldLocked(pos, _aoiRecipientsScratch);
        int castOffset = _flatRecipients.Count;
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            _flatRecipients.Add(_aoiRecipientsScratch[i]);
        }

        if (_aoiRecipientsScratch.Count > 0)
        {
            _pendingSkillCasts.Add(new OutboundSkillCast(
                castOffset,
                _aoiRecipientsScratch.Count,
                state.Session.PlayerNetworkId,
                skillSlot,
                pos.X,
                pos.Y,
                pos.X + (aimDir.X * 64f),
                pos.Y + (aimDir.Y * 64f)));
        }

        if (skillSlot == CharacterProgressionPolicy.SkillVitalSurge)
        {
            if (_simulation.Entities.HasHealth(state.Entity))
            {
                HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
                float healAmt = MathF.Max(30f, hp.Max * 0.30f);
                float nextHp = MathF.Min(hp.Max, hp.Current + healAmt);
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(nextHp, hp.Max));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
                state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + 45f);
                this.QueueCombatHitLocked(pos, state.Session.PlayerNetworkId, state.Session.PlayerNetworkId, -healAmt, nextHp, false, false);
            }

            this.MarkCharacterDirtyLocked(state);
            return;
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        float weaponBase = ToolUsagePolicy.EvaluateCombatStrike(in activeSlot).Damage * state.Stats.AttackMultiplier;

        float radius = skillSlot == CharacterProgressionPolicy.SkillArcaneNova ? 170f : 96f;
        float halfAngle = skillSlot == CharacterProgressionPolicy.SkillArcaneNova ? 0.65f : MathF.PI;
        float dmgMult = skillSlot == CharacterProgressionPolicy.SkillArcaneNova ? 1.85f : 1.45f;
        float skillDamage = weaponBase * dmgMult;

        HitboxQuery.QueryArc(_simulation, pos, aimDir, radius, halfAngle, _combatHitScratch, out int hitCount, MonsterLayer, state.Entity);
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
            float remaining = MathF.Max(0f, hp.Current - skillDamage);
            bool killed = remaining <= 0f;
            Vector2 targetPos = _simulation.Entities.HasTransform(target) ? _simulation.Entities.GetTransform(target).Position : pos;

            _simulation.Entities.SetHealth(target, new HealthComponent(remaining, hp.Max));
            _simulation.MarkDirty(target, EntityDirtyMask.Health);

            if (!killed)
            {
                threatStore.GetOrAdd(target, static () => new ThreatTable()).AddThreat(state.Entity, skillDamage * 2f);
                if (_simulation.Entities.HasComponent<AiBrainComponent>(target))
                {
                    ref AiBrainComponent brain = ref _simulation.Entities.GetComponent<AiBrainComponent>(target);
                    brain = brain with { State = AiState.Chase, TargetEntity = state.Entity, RepathCooldownTicks = 0 };
                }
            }
            else
            {
                if (_actorsByNetworkId.Remove(targetIdentity.NetworkId, out NpcActorMeta meta))
                {
                    _pendingRespawns.Add(new PendingMonsterRespawn(_currentTick + (ulong)(this.TickRate * 45), meta));
                    ItemSlot loot = new(meta.DropItemId, 2);
                    this.SpawnGroundLootLocked(targetPos, in loot);
                    uint xpReward = (uint)Math.Clamp((int)(meta.MaxHealth * 0.45f), 25, 90);
                    this.GrantExperienceLocked(state, xpReward, out _);
                }

                _simulation.DespawnEntity(target);
            }

            this.QueueCombatHitLocked(targetPos, state.Session.PlayerNetworkId, targetIdentity.NetworkId, skillDamage, remaining, isCritical: true, isKilled: killed);
        }

        this.MarkCharacterDirtyLocked(state);
    }

    private void GrantExperienceLocked(ActivePlayerState state, uint amount, out bool leveledUp)
    {
        leveledUp = false;
        if (amount == 0U || state.Level >= CharacterProgressionPolicy.MaxLevel)
        {
            return;
        }

        state.Experience += amount;
        while (state.Level < CharacterProgressionPolicy.MaxLevel)
        {
            uint needed = CharacterProgressionPolicy.GetXpToNextLevel(state.Level);
            if (state.Experience < needed)
            {
                break;
            }

            state.Experience -= needed;
            state.Level++;
            leveledUp = true;
            state.LeveledUpPending = true;
        }

        if (leveledUp)
        {
            this.RecomputeDerivedStatsLocked(state);
            if (_simulation.Entities.HasHealth(state.Entity))
            {
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(state.Stats.MaxHealth, state.Stats.MaxHealth));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
            }

            state.CurrentMana = state.Stats.MaxMana;
            state.CurrentStamina = state.Stats.MaxStamina;

            if (_simulation.Entities.HasTransform(state.Entity))
            {
                Vector2 pPos = _simulation.Entities.GetTransform(state.Entity).Position;
                this.CollectNearbyConnectionsAroundWorldLocked(pPos, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    int offset = _flatRecipients.Count;
                    for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
                    {
                        _flatRecipients.Add(_aoiRecipientsScratch[i]);
                    }

                    ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
                    _pendingEquipmentChanges.Add(new OutboundEquipmentChange(
                        offset,
                        _aoiRecipientsScratch.Count,
                        state.Session.PlayerNetworkId,
                        (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId,
                        (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId,
                        (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId,
                        (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId,
                        (ushort)held.ItemId,
                        ItemEnhancementPolicy.GetUpgradeLevel(in held),
                        state.Level));
                }
            }
        }

        state.StatsDirty = true;
    }

    private void RecomputeDerivedStatsLocked(ActivePlayerState state)
    {
        DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(state.Level, state.Inventory);
        state.Stats = stats;
        if (_simulation.Entities.HasHealth(state.Entity))
        {
            HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
            float clamped = MathF.Min(stats.MaxHealth, MathF.Max(1f, hp.Current));
            _simulation.Entities.SetHealth(state.Entity, new HealthComponent(clamped, stats.MaxHealth));
            _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
        }

        state.CurrentMana = Math.Clamp(state.CurrentMana, 0f, stats.MaxMana);
        state.CurrentStamina = Math.Clamp(state.CurrentStamina, 0f, stats.MaxStamina);
    }

    private void DegradeEquippedArmorLocked(ActivePlayerState victim, float loss)
    {
        bool anyBroke = false;
        for (int sIdx = PlayerInventoryLayout.HeadSlot; sIdx <= PlayerInventoryLayout.FeetSlot; sIdx++)
        {
            if (sIdx >= victim.Inventory.Capacity)
            {
                break;
            }

            ItemSlot slot = victim.Inventory.GetSlot(sIdx);
            if (slot.IsEmpty || slot.Durability <= 0f)
            {
                continue;
            }

            float nextDur = MathF.Max(0f, slot.Durability - loss);
            ItemSlot updated = slot with { Durability = nextDur };
            victim.Inventory.SetSlot(sIdx, in updated);
            _pendingSlotChanges.Add(new OutboundSlotChange(victim.Session.ConnectionId, (ushort)sIdx, updated));
            if (nextDur <= 0f)
            {
                anyBroke = true;
            }
        }

        if (anyBroke)
        {
            this.RecomputeDerivedStatsLocked(victim);
        }

        this.MarkCharacterDirtyLocked(victim);
    }

    private void CollectNearbyConnectionsAroundWorldLocked(Vector2 target, List<ulong> output)
    {
        output.Clear();
        float radiusSq = (_aoi.ViewRadius + (this.TileSize * 2f)) * (_aoi.ViewRadius + (this.TileSize * 2f));
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState state = kvp.Value;
            if (!_simulation.Entities.HasTransform(state.Entity))
            {
                continue;
            }

            if (Vector2.DistanceSquared(_simulation.Entities.GetTransform(state.Entity).Position, target) <= radiusSq)
            {
                output.Add(kvp.Key);
            }
        }
    }

    private void EnqueueStatsSyncLocked(ActivePlayerState state, bool leveledUp)
    {
        HealthComponent hp = _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity)
            : new HealthComponent(100f, state.Stats.MaxHealth);
        uint xpNext = CharacterProgressionPolicy.GetXpToNextLevel(state.Level);
        float cd1 = this.GetCooldownSecondsRemaining(state.Cooldowns, Skill1AbilityId);
        float cd2 = this.GetCooldownSecondsRemaining(state.Cooldowns, Skill2AbilityId);
        float cd3 = this.GetCooldownSecondsRemaining(state.Cooldowns, Skill3AbilityId);

        _pendingStatsSyncs.Add(new OutboundStatsSync(
            state.Session.ConnectionId,
            state.Level,
            state.Experience,
            xpNext,
            hp.Current,
            hp.Max,
            state.CurrentMana,
            state.Stats.MaxMana,
            state.CurrentStamina,
            state.Stats.MaxStamina,
            state.Stats.Defense,
            cd1,
            cd2,
            cd3,
            leveledUp));
    }

    private void DispatchImmediateStatsSync(ActivePlayerState state, bool leveledUp)
    {
        HealthComponent hp = _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity)
            : new HealthComponent(100f, state.Stats.MaxHealth);
        _outboundSink.SendPlayerStatsSync(
            state.Session.ConnectionId,
            state.Level,
            state.Experience,
            CharacterProgressionPolicy.GetXpToNextLevel(state.Level),
            hp.Current,
            hp.Max,
            state.CurrentMana,
            state.Stats.MaxMana,
            state.CurrentStamina,
            state.Stats.MaxStamina,
            state.Stats.Defense,
            this.GetCooldownSecondsRemaining(state.Cooldowns, Skill1AbilityId),
            this.GetCooldownSecondsRemaining(state.Cooldowns, Skill2AbilityId),
            this.GetCooldownSecondsRemaining(state.Cooldowns, Skill3AbilityId),
            leveledUp);
    }

    private float GetCooldownSecondsRemaining(CooldownTracker tracker, uint abilityId)
    {
        uint remTicks = tracker.GetRemainingTicks(abilityId, _currentTick);
        return remTicks > 0U ? (float)remTicks / this.TickRate : 0f;
    }

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
        public ulong PendingPickupLootId { get; set; }
        public ulong LastCombatDamageTick { get; set; }
        public ushort Level { get; set; } = 1;
        public uint Experience { get; set; }
        public DerivedCharacterStats Stats { get; set; } = new(100f, 80f, 100f, 2f, 1f, 1f);
        public float CurrentMana { get; set; } = 80f;
        public float CurrentStamina { get; set; } = 100f;
        public bool StatsDirty { get; set; }
        public bool LeveledUpPending { get; set; }
    }

    private readonly record struct LootDropMeta(ItemSlot Item, ulong SpawnTick);

    private readonly record struct OutboundEquipmentChange(
        int RecipientsOffset,
        int RecipientsCount,
        ulong NetworkId,
        ushort HeadId,
        ushort ChestId,
        ushort LegsId,
        ushort FeetId,
        ushort WeaponId,
        byte WeaponUpgrade,
        ushort Level);

    private readonly record struct OutboundStatsSync(
        ulong ConnectionId,
        ushort Level,
        uint Experience,
        uint XpToNext,
        float CurHp,
        float MaxHp,
        float CurMp,
        float MaxMp,
        float CurStam,
        float MaxStam,
        float Defense,
        float Skill1Cd,
        float Skill2Cd,
        float Skill3Cd,
        bool LeveledUp);

    private readonly record struct OutboundSkillCast(
        int RecipientsOffset,
        int RecipientsCount,
        ulong CasterNetworkId,
        byte AbilitySlot,
        float OriginX,
        float OriginY,
        float AimX,
        float AimY);

    private readonly record struct NpcActorMeta(
        EntityKind Kind,
        ushort ArchetypeId,
        string Name,
        Vector2 HomePos,
        float MaxHealth,
        float MoveSpeed,
        float Radius,
        float AttackDamage,
        uint WindupTicks,
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

    private readonly record struct OutboundPropChange(
        int RecipientsOffset,
        int RecipientsCount,
        int TileX,
        int TileY,
        uint Revision,
        float Durability,
        bool IsDestroyed);

    private readonly record struct OutboundStructureChange(
        int RecipientsOffset,
        int RecipientsCount,
        int TileX,
        int TileY,
        byte StructureType,
        float Durability,
        byte DoorState,
        byte Connectivity);
}
