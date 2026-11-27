// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Actions;
using Carbon.Application.World.Actors;
using Carbon.Application.World.Combat;
using Carbon.Application.World.Commands;
using Carbon.Application.World.Commerce;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Needs;
using Carbon.Application.World.Persistence;
using Carbon.Application.World.Players;
using Carbon.Application.World.Projectiles;
using Carbon.Application.World.Props;
using Carbon.Application.World.Replication;
using Carbon.Combat.Cooldowns;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Creatures;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Economy;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Items.Systems;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;
using Carbon.Survival.Config;
using Carbon.Survival.Food;

namespace Carbon.Application.World;

/// <summary>
/// Thread-safe authoritative world simulation coordinator bridging fixed-tick ECS physics,
/// AI monsters/NPCs, melee &amp; skill combat, inventory/crafting persistence, procedural chunk deltas,
/// Spatial AoI replication, and zero-allocation tick dispatches.
/// </summary>
public sealed partial class AuthoritativeWorld : IAuthoritativeWorld, IWorldCommandHandlers
{
    private const float BaseWalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const float BuildInteractRangeTiles = 3.5f;
    private const float DefaultEntityRadius = 7f;
    private const uint PlayerLayer = 1U;
    private const uint MonsterLayer = 2U;
    private const uint NpcLayer = 4U;
    private const int PlayerInventoryCapacity = PlayerInventoryLayout.TotalCapacity;
    private const uint AttackAbilityId = PlayerActionService.AttackAbilityId;
    private const uint SkillAbilityIdBase = PlayerActionService.SkillAbilityIdBase;

    private readonly Lock _gate = new();
    private readonly WorldCommandQueue _commands = new();
    private readonly WorldCommandExecutor _commandExecutor;
    private readonly ProceduralWorldGenerator _generator;
    private readonly ChunkWorld2D _chunkWorld;
    private readonly ConstructionService _construction;
    private readonly InventoryService _inventory;
    private readonly EconomyService _commerce;
    private readonly CombatService _combat;
    private readonly PropHarvestService _props;
    private readonly SettlementNpcSpawner _settlements;
    private readonly PlayerActionService _actions;
    private readonly PlayerMaterialAdapter _playerMaterials;
    private readonly WorldSimulation _simulation;
    private readonly AoiReplicator _replication;
    private readonly IChunkDeltaRepository _chunkDeltaRepo;
    private readonly ICharacterStore _characterStore;
    private readonly IWorldOutboundSink _outboundSink;
    private readonly TimeProvider _time;
    private readonly NeedsService _needs;
    private GameEconomy? _economy;
    private ItemRules _itemRules;

    private readonly PlayerRegistry _players = new();
    private readonly Dictionary<ulong, NpcActorMeta> _actorsByNetworkId = new();
    private readonly GroundLootService _groundLoot;
    private readonly ProjectileService _projectiles;
    private readonly MonsterCatalog _creatureCatalog;
    private readonly CreatureSystems _creatures;
    private readonly HashSet<long> _dirtyChunks = new();
    private readonly CharacterPersistence _persistence;

    // Reusable scratch buffers (zero per-tick heap allocations under steady state).
    // Outbound replication, staged under _gate during the tick and dispatched after it (OutboundTickBuffer).
    private readonly OutboundTickBuffer _outbound = new();
    private readonly List<ulong> _aoiRecipientsScratch = new(64);
    private readonly List<(ushort SlotIndex, ItemSlot Slot)> _mutationSlotScratch = new(8);
    private readonly List<(ushort SlotIndex, ItemSlot Slot)> _spoiledSlotsScratch = new(8);
    private readonly List<(int TileX, int TileY, uint Revision, float MaxDurability)> _respawnedPropsScratch = new(16);
    private readonly Entity[] _combatHitScratch = new Entity[32];

    private ulong _nextNetworkId = 1000UL;
    private ulong _currentTick;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthoritativeWorld"/> class.
    /// </summary>
    /// <param name="worldSeed">Procedural world seed (also seeds the food poisoning roll).</param>
    /// <param name="tileSize">Tile size in world units.</param>
    /// <param name="frequency">Terrain noise frequency.</param>
    /// <param name="tickRate">Fixed simulation tick rate.</param>
    /// <param name="chunkDeltaRepo">Chunk delta persistence.</param>
    /// <param name="characterStore">Character persistence.</param>
    /// <param name="outboundSink">Outbound packet sink.</param>
    /// <param name="time">Optional time provider.</param>
    /// <param name="needsConfig">Survival needs tuning (<c>needs.json</c>); defaults to built-in values.</param>
    /// <param name="foodCatalog">Edible items (<c>food.json</c>); defaults to none, so no item is eaten.</param>
    /// <param name="economy">Optional data-driven economy (items, loot, currencies).</param>
    /// <param name="structureCatalog">Optional data-driven building catalog.</param>
    /// <param name="creatures">Monster catalog and spawning config; defaults to <see cref="CreatureContent.LoadDefault"/> (<c>resources/config</c>).</param>
    public AuthoritativeWorld(
        ulong worldSeed,
        float tileSize,
        float frequency,
        ushort tickRate,
        IChunkDeltaRepository chunkDeltaRepo,
        ICharacterStore characterStore,
        IWorldOutboundSink outboundSink,
        TimeProvider? time = null,
        NeedsConfig? needsConfig = null,
        FoodCatalog? foodCatalog = null,
        GameEconomy? economy = null,
        StructureCatalog? structureCatalog = null,
        CreatureContent? creatures = null)
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
        _economy = economy;
        _itemRules = economy?.ItemRules ?? ItemRules.LoadShippedDefaults();

        _generator = new ProceduralWorldGenerator(this.WorldSeed);
        _chunkWorld = new ChunkWorld2D(_generator, this.TileSize, this.Frequency);
        _playerMaterials = new PlayerMaterialAdapter(this);
        _simulation = new WorldSimulation(this.TickRate, this.TileSize)
        {
            WorldPhysics = _chunkWorld,
        };
        CreatureContent creatureContent = creatures ?? CreatureContent.LoadDefault();
        _creatureCatalog = creatureContent.Catalog;
        _creatures = RegisterCreatureSystems(_simulation, creatureContent, _chunkWorld, this.WorldSeed, this.TileSize, new ServerAttackEffects(this));
        _simulation.RegisterSystem(new LootExpirationSystem());
        _groundLoot = new GroundLootService(
            _simulation,
            () => _itemRules,
            () => ++_nextNetworkId,
            () => _currentTick,
            this.QueueLootVisualRefreshLocked,
            _players.GetNetworkIdOfCharacter);
        _construction = new ConstructionService(
            _chunkWorld,
            structureCatalog ?? StructureCatalog.LoadEmbedded(),
            this.IsPlayerOnTileLocked,
            _simulation,
            this.TileSize,
            () => _itemRules,
            _outbound,
            _dirtyChunks,
            this.AppendTileRecipientsLocked,
            (pos, item) => this.SpawnGroundLootLocked(pos, in item));
        _inventory = new InventoryService(
            _simulation,
            _chunkWorld.Buildings,
            this.TileSize,
            this.WorldSeed,
            () => _itemRules,
            () => _economy,
            () => _currentTick,
            this.HasNearbyNpcWithCapabilityLocked,
            _groundLoot,
            _outbound,
            state => this.MarkCharacterDirtyLocked(state, inventoryChanged: true));
        _commerce = new EconomyService(this.WorldSeed, this.TickRate, () => _economy, () => _currentTick, _groundLoot, _inventory, _outbound, this.HasNearbyFriendlyNpcLocked);
        _combat = new CombatService(_simulation, _chunkWorld, MonsterLayer, _outbound, this.CollectNearbyConnectionsAroundWorldLocked, this.OnMonsterKilledLocked);
        _props = new PropHarvestService(
            _simulation,
            _chunkWorld,
            this.TileSize,
            this.TickRate,
            () => _itemRules,
            () => _currentTick,
            _groundLoot,
            _construction,
            _outbound,
            _dirtyChunks,
            this.AppendTileRecipientsLocked,
            this.IsWithinInteractRangeLocked,
            this.DegradeActiveSlotDurabilityLocked,
            (state, xp) =>
            {
                this.GrantExperienceLocked(state, xp, out _);
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            });
        _settlements = new SettlementNpcSpawner(_simulation, _chunkWorld, _actorsByNetworkId, () => ++_nextNetworkId, this.TileSize, this.TickRate, DefaultEntityRadius, PlayerLayer, MonsterLayer, NpcLayer);
        _projectiles = new ProjectileService(_simulation, _chunkWorld, MonsterLayer, () => ++_nextNetworkId, new ProjectileHits(this));

        _needs = new NeedsService(
            _simulation,
            _chunkWorld.Buildings,
            this.TileSize,
            () => _itemRules,
            () => _economy,
            needsConfig,
            foodCatalog,
            worldSeed);
        _actions = new PlayerActionService(
            _simulation,
            this.TickRate,
            MonsterLayer,
            () => _itemRules,
            () => _currentTick,
            _outbound,
            _combat,
            _projectiles,
            _props,
            _inventory,
            _needs,
            new PlayerActionHost(this));
        _persistence = new CharacterPersistence(_simulation, _needs, _time);
        _commandExecutor = new WorldCommandExecutor(this);
        _replication = new AoiReplicator(_simulation, _outbound, this.TileSize * 24f, DefaultEntityRadius, this.ResolveSpawnIdentityLocked);
    }

    /// <summary>
    /// Switches the running server domains to tables rebuilt from a new config snapshot (ADR-014 hot reload).
    /// Runs under the world lock between ticks, so a tick sees either the old or the new tables, never a mix.
    /// Meals in progress are remapped to the new food catalog (or cancelled when their food is gone). Existing players
    /// keep their current needs values and wallet caps; new limits apply to new state.
    /// </summary>
    /// <param name="economy">New economy (items, recipes, loot, tunables), or <see langword="null"/> to keep none.</param>
    /// <param name="needsConfig">New needs tuning.</param>
    /// <param name="foodCatalog">New edible items.</param>
    public void ApplyGameData(GameEconomy? economy, NeedsConfig needsConfig, FoodCatalog foodCatalog)
    {
        ArgumentNullException.ThrowIfNull(needsConfig);
        ArgumentNullException.ThrowIfNull(foodCatalog);
        lock (_gate)
        {
            _economy = economy;
            _itemRules = economy?.ItemRules ?? _itemRules;
            _needs.Reconfigure(needsConfig, foodCatalog);
        }
    }

    /// <summary>Gets the economy the simulation currently runs on.</summary>
    public GameEconomy? Economy
    {
        get
        {
            lock (_gate)
            {
                return _economy;
            }
        }
    }

    /// <summary>Gets the item stat and recipe rules the simulation currently runs on.</summary>
    public ItemRules ItemRules
    {
        get
        {
            lock (_gate)
            {
                return _itemRules;
            }
        }
    }

    /// <summary>Gets the needs tuning the simulation currently runs on.</summary>
    public NeedsConfig NeedsConfig
    {
        get
        {
            lock (_gate)
            {
                return _needs.Config;
            }
        }
    }

    /// <summary>Gets the food catalog the simulation currently runs on.</summary>
    public FoodCatalog FoodCatalog
    {
        get
        {
            lock (_gate)
            {
                return _needs.Food;
            }
        }
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
            _settlements.PopulateAround(originSpawn, radius: 2);
        }
    }

    /// <inheritdoc/>
    public EnterWorldResult TryEnterWorld(ulong connectionId, ulong sessionId, CharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);

        lock (_gate)
        {
            if (_players.ByConnection.ContainsKey(connectionId))
            {
                _ = this.RemovePlayerLocked(connectionId);
            }

            // Anti-duplication guard: evict any duplicate active session for the same character ID.
            ulong duplicateConnectionId = 0UL;
            foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
            {
                if (kvp.Value.SourceCharacter.Id == character.Id)
                {
                    duplicateConnectionId = kvp.Key;
                    break;
                }
            }

            if (duplicateConnectionId != 0UL)
            {
                _ = this.RemovePlayerLocked(duplicateConnectionId);
            }

            Vector2 spawnPos = _debugSpawnPosition ?? new Vector2(character.PositionX, character.PositionY);
            _chunkWorld.UpdateAroundPosition(spawnPos, chunkRadius: 2, evictionRadius: 8);
            _settlements.PopulateAround(spawnPos, radius: 2);

            ulong networkId = ++_nextNetworkId;
            Entity entity = _simulation.SpawnNetworkEntity(networkId, spawnPos, DefaultEntityRadius, sessionId);
            _simulation.Entities.SetCollider(entity, new Collider2D(ColliderKind.Circle, DefaultEntityRadius, default, false, PlayerLayer, uint.MaxValue));
            _simulation.Entities.SetVelocity(entity, new Velocity2D(0f, 0f, 5f));
            _simulation.Entities.SetComponent(entity, default(SpawnAnchorComponent));
            _creatures.Spawner.RequestScan();

            InventoryContainer inventory = new(PlayerInventoryCapacity);
            character = CharacterPersistence.LoadInventory(character, inventory);

            ushort startLevel = Math.Max((ushort)1, character.Level);
            byte selectedSlot = character.SelectedHotbarSlot < PlayerInventoryLayout.HotbarCount ? character.SelectedHotbarSlot : (byte)0;
            CharacterProgressionState startProg = CharacterPersistence.LoadProgression(character);

            DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(_itemRules, startLevel, inventory, selectedSlot, startProg);
            float startHp = MathF.Min(stats.MaxHealth, MathF.Max(1f, character.CurrentHealth));
            _simulation.Entities.SetHealth(entity, new HealthComponent(startHp, stats.MaxHealth));
            _simulation.Entities.SetComponent(entity, _needs.Restore(character));

            byte harvestTier = _inventory.ResolveHarvestTier(inventory, selectedSlot);

            ConnectedPlayerSession session = new(
                sessionId,
                connectionId,
                character.AccountId,
                character.Id,
                character.Name,
                networkId,
                selectedSlot,
                harvestTier);

            float startMana = Math.Clamp(character.CurrentMana, 0f, stats.MaxMana);
            float startStamina = Math.Clamp(character.CurrentStamina, 0f, stats.MaxStamina);

            PlayerState playerState = new(session, entity, character, inventory)
            {
                Level = startLevel,
                Experience = character.Experience,
                Stats = stats,
                CurrentMana = startMana,
                CurrentStamina = startStamina,
                StatsDirty = true,
                LastSavedPosition = spawnPos,
                Wallet = _economy is null
                    ? new Wallet(networkId)
                    : new Wallet(networkId, _economy.Config.Currencies.GoldCap, _economy.Config.Currencies.DiamondCap),
                LootPity = new LootPityState(Math.Max(0, character.DiamondPity)),
            };
            playerState.Wallet.Restore(
                Math.Clamp(character.Gold, 0L, playerState.Wallet.GoldCap),
                Math.Clamp(character.Diamonds, 0L, playerState.Wallet.DiamondCap),
                character.WalletSequence);
            _commerce.QueueWalletSync(playerState);
            startProg.CopyTo(playerState.Progression);

            _players.ByConnection[connectionId] = playerState;
            _players.ConnectionByNetworkId[networkId] = connectionId;
            _replication.Track(connectionId);

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
                character.AppearancePreset,
                selectedSlot,
                startLevel,
                character.Experience,
                CharacterProgressionPolicy.GetXpToNextLevel(startLevel),
                startHp,
                stats.MaxHealth,
                startMana,
                stats.MaxMana,
                startStamina,
                stats.MaxStamina,
                stats.Defense,
                (byte)character.Class,
                character.Flags,
                (ulong)playerState.Progression.UnlockedSkillMask,
                CharacterProgressionBinaryCodec.Serialize(playerState.Progression));
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
            if (_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                session = state.Session;
                return true;
            }

            session = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryGetPendingCharacterSnapshot(Guid characterId, out CharacterRecord? character)
    {
        lock (_gate)
        {
            return _persistence.TryGetPending(_players.ByConnection.Values, characterId, out character);
        }
    }

    /// <inheritdoc/>
    public void EnqueueMoveInput(ulong connectionId, uint clientTick, Vector2 direction, bool sprint)
    {
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
        {
            return;
        }

        lock (_gate)
        {
            if (_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
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
        if (!float.IsFinite(aimWorldPosition.X) || !float.IsFinite(aimWorldPosition.Y))
        {
            return OpStatus.Invalid;
        }

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
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
    public ValueTask<WorldMutationOutcome> SubmitAllocateCharacterPointAsync(ulong connectionId, PointAllocationKind kind, ushort targetId, byte amount = 1) =>
        _commands.Submit(WorldCommandKind.AllocateCharacterPoint, connectionId, (ushort)kind, targetId, amount);

    /// <summary>
    /// Allocates unspent attribute or skill mastery points. Synchronous form used by the tick and tests; handlers use
    /// <see cref="SubmitAllocateCharacterPointAsync"/> (ADR-017).
    /// </summary>
    public OpStatus TryAllocateCharacterPoint(ulong connectionId, PointAllocationKind kind, ushort targetId, byte amount = 1)
    {
        int requested = Math.Clamp((int)amount, 1, 10);
        PlayerStatsSnapshot immediateSync;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.IsAlive(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            CharacterProgressionState prog = state.Progression;
            if (kind == PointAllocationKind.Attribute)
            {
                PrimaryAttributeId attr = (PrimaryAttributeId)targetId;
                if (!Enum.IsDefined(attr))
                {
                    return OpStatus.Invalid;
                }

                ushort unspentStats = CharacterProgressionPolicy.GetUnspentStatPoints(state.Level, prog);
                if (unspentStats == 0)
                {
                    return OpStatus.Conflict;
                }

                ushort spend = (ushort)Math.Min(requested, unspentStats);
                prog.AddAttribute(attr, spend);

                float prevMaxHp = state.Stats.MaxHealth;
                float prevMaxMp = state.Stats.MaxMana;
                float prevMaxStam = state.Stats.MaxStamina;
                this.RecomputeDerivedStatsLocked(state);

                if (_simulation.Entities.HasHealth(state.Entity) && state.Stats.MaxHealth > prevMaxHp)
                {
                    HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
                    float nextHp = MathF.Min(state.Stats.MaxHealth, hp.Current + (state.Stats.MaxHealth - prevMaxHp));
                    _simulation.Entities.SetHealth(state.Entity, new HealthComponent(nextHp, state.Stats.MaxHealth));
                    _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
                }

                if (state.Stats.MaxMana > prevMaxMp)
                {
                    state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + (state.Stats.MaxMana - prevMaxMp));
                }

                if (state.Stats.MaxStamina > prevMaxStam)
                {
                    state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + (state.Stats.MaxStamina - prevMaxStam));
                }

                state.StatsDirty = true;
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
                immediateSync = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
            }
            else if (kind == PointAllocationKind.Skill)
            {
                if (!SkillCatalog.Skills.TryGet(targetId, out SkillDefinition? skillDef))
                {
                    return OpStatus.Invalid;
                }

                if (!SkillCatalog.CanCharacterAccessSkill(
                    skillDef,
                    state.SourceCharacter.Class,
                    state.SourceCharacter.Flags,
                    prog.UnlockedSkillMask))
                {
                    return OpStatus.Forbidden;
                }

                ushort unspentSkills = CharacterProgressionPolicy.GetUnspentSkillPoints(state.Level, prog);
                byte curRank = prog.GetSkillRank(targetId);
                if (unspentSkills == 0 || curRank >= skillDef.MaxRank)
                {
                    return OpStatus.Conflict;
                }

                int spend = Math.Min(requested, Math.Min(unspentSkills, skillDef.MaxRank - curRank));
                byte nextRank = (byte)(curRank + spend);
                prog.SetSkillRank(targetId, nextRank);
                state.StatsDirty = true;
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
                immediateSync = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
            }
            else
            {
                return OpStatus.Invalid;
            }
        }

        _outboundSink.SendPlayerStatsSync(connectionId, in immediateSync);
        return OpStatus.Ok;
    }

    private bool RemovePlayerLocked(ulong connectionId)
    {
        if (!_players.ByConnection.Remove(connectionId, out PlayerState? state))
        {
            return false;
        }

        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
        _ = _players.ConnectionByNetworkId.Remove(state.Session.PlayerNetworkId);
        _replication.Untrack(connectionId);
        _simulation.DespawnEntity(state.Entity);
        return true;
    }

    private void MarkCharacterDirtyLocked(PlayerState state, bool inventoryChanged = true) =>
        _persistence.MarkDirty(state, inventoryChanged);

    /// <summary>Resolves what a replicated entity is: a player (name, look, gear), an NPC, a loot stack or a projectile.</summary>
    private SpawnIdentity ResolveSpawnIdentityLocked(ulong networkId)
    {
        if (_players.ConnectionByNetworkId.TryGetValue(networkId, out ulong ownerConn) &&
            _players.ByConnection.TryGetValue(ownerConn, out PlayerState? state))
        {
            ItemSlot held = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
                ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
                : ItemSlot.Empty;
            return new SpawnIdentity(
                EntityKind.Player,
                state.Session.CharacterName,
                state.SourceCharacter.AppearancePreset,
                (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId,
                (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId,
                (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId,
                (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId,
                (ushort)held.ItemId,
                state.Level,
                0,
                ItemEnhancementPolicy.GetUpgradeLevel(in held));
        }

        if (_actorsByNetworkId.TryGetValue(networkId, out NpcActorMeta meta))
        {
            return SpawnIdentity.Unknown with { Kind = meta.Kind, Name = meta.Name, Archetype = meta.ArchetypeId, Level = meta.Level };
        }

        if (_groundLoot.TryGetSpawnIdentity(networkId, this.TickRate, out SpawnIdentity lootIdentity))
        {
            return lootIdentity;
        }

        return _projectiles.TryGet(networkId, out ProjectileState projMeta)
            ? SpawnIdentity.Unknown with { Kind = EntityKind.Projectile, Name = string.Empty, Archetype = projMeta.VisualArchetype }
            : SpawnIdentity.Unknown;
    }

    private void RecomputeDerivedStatsLocked(PlayerState state)
    {
        DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(_itemRules, state.Level, state.Inventory, state.Session.SelectedHotbarSlot, state.Progression);
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

    private void EnqueueEquipmentBroadcastLocked(PlayerState state, Vector2 worldPos)
    {
        this.CollectNearbyConnectionsAroundWorldLocked(worldPos, _aoiRecipientsScratch);
        if (_aoiRecipientsScratch.Count == 0)
        {
            return;
        }

        int offset = _outbound.Pending.Recipients.Count;
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            _outbound.Pending.Recipients.Add(_aoiRecipientsScratch[i]);
        }

        ItemSlot held = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        _outbound.Pending.EquipmentChanges.Add(new OutboundEquipmentChange(
            offset,
            _aoiRecipientsScratch.Count,
            state.Session.PlayerNetworkId,
            (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId,
            (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId,
            (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId,
            (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId,
            (ushort)held.ItemId,
            ItemEnhancementPolicy.GetUpgradeLevel(in held),
            state.Level,
            state.Stats.MaxHealth));
    }

    private PlayerStatsSnapshot BuildPlayerStatsSnapshotLocked(PlayerState state, bool clearLevelUpPending)
    {
        HealthComponent hp = _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity)
            : new HealthComponent(100f, state.Stats.MaxHealth);
        uint xpNext = CharacterProgressionPolicy.GetXpToNextLevel(state.Level);
        bool leveledUp = state.LeveledUpPending;
        if (clearLevelUpPending)
        {
            state.LeveledUpPending = false;
        }

        ReadOnlySpan<SkillDefinition> skills = SkillCatalog.Skills.All;
        int activeCdCount = 0;
        for (int i = 0; i < skills.Length; i++)
        {
            if (this.GetCooldownSecondsRemaining(state.Cooldowns, SkillAbilityIdBase + skills[i].Id) > 0f)
            {
                activeCdCount++;
            }
        }

        ushort[] cdIds = activeCdCount == 0 ? [] : new ushort[activeCdCount];
        float[] cdSecs = activeCdCount == 0 ? [] : new float[activeCdCount];
        int cdWrite = 0;
        for (int i = 0; i < skills.Length && cdWrite < activeCdCount; i++)
        {
            ushort sid = skills[i].Id;
            float rem = this.GetCooldownSecondsRemaining(state.Cooldowns, SkillAbilityIdBase + sid);
            if (rem > 0f)
            {
                cdIds[cdWrite] = sid;
                cdSecs[cdWrite] = rem;
                cdWrite++;
            }
        }

        ushort unspentStats = CharacterProgressionPolicy.GetUnspentStatPoints(state.Level, state.Progression);
        ushort unspentSkills = CharacterProgressionPolicy.GetUnspentSkillPoints(state.Level, state.Progression);
        byte[] progBlob = CharacterProgressionBinaryCodec.Serialize(state.Progression);

        return new PlayerStatsSnapshot(
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
            leveledUp,
            state.SourceCharacter.Flags,
            state.Progression.UnlockedSkillMask,
            unspentStats,
            unspentSkills,
            progBlob,
            cdIds,
            cdSecs,
            _needs.BuildSnapshot(state.Entity, state.RestMode));
    }

    private void EnqueueStatsSyncLocked(PlayerState state, bool leveledUp)
    {
        PlayerStatsSnapshot snap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: leveledUp);
        if (leveledUp)
        {
            snap = snap with { LeveledUp = true };
        }

        _outbound.Pending.StatsSyncs.Add(new OutboundStatsSync(state.Session.ConnectionId, snap));
    }

    private float GetCooldownSecondsRemaining(CooldownTracker tracker, uint abilityId)
    {
        uint remTicks = tracker.GetRemainingTicks(abilityId, _currentTick);
        return remTicks > 0U ? (float)remTicks / this.TickRate : 0f;
    }

    internal static ulong HashChunk(ulong seed, int cx, int cy)
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

    // Debug overrides (folded from AuthoritativeWorld.DebugOverrides.cs):
    // Developer-only overrides for the screenshot review tool: a forced spawn tile and a start hour. They come
    // from the host configuration (Debug builds only), never from a client packet.

    private const int DebugBiomeSearchRadius = 4000;

    // Where every character enters the world while a debug spawn override is active.
    private Vector2? _debugSpawnPosition;

    /// <summary>Gets the forced debug spawn position, if a debug override set one.</summary>
    public Vector2? DebugSpawnPosition
    {
        get
        {
            lock (_gate)
            {
                return _debugSpawnPosition;
            }
        }
    }

    /// <summary>
    /// Applies developer-only overrides: resolves the debug spawn tile (explicit tile, else the nearest
    /// representative tile of a biome, else the world spawn, plus an offset) and winds the world clock to a
    /// start hour. Call once at boot, before players connect.
    /// </summary>
    /// <param name="overrides">The overrides read from the host configuration.</param>
    /// <returns>A one-line description of what was applied, for the boot log.</returns>
    /// <exception cref="ArgumentException">The biome name is unknown or no tile of it was found.</exception>
    public string ApplyDebugOverrides(WorldDebugOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        if (!overrides.IsActive)
        {
            return "none";
        }

        lock (_gate)
        {
            (int X, int Y) spawn = _chunkWorld.SpawnTile;
            if (overrides.SpawnTile is { } tile)
            {
                spawn = tile;
            }
            else if (overrides.SpawnBiome is { } biomeName)
            {
                if (!Enum.TryParse(biomeName, ignoreCase: true, out BiomeType biome) || !Enum.IsDefined(biome))
                {
                    throw new ArgumentException($"Unknown biome '{biomeName}'.", nameof(overrides));
                }

                if (!BiomeLocator.TryFind(_generator, biome, spawn.X, spawn.Y, DebugBiomeSearchRadius, out int bx, out int by))
                {
                    throw new ArgumentException($"No {biome} tile within {DebugBiomeSearchRadius} tiles of spawn.", nameof(overrides));
                }

                spawn = (bx, by);
            }

            spawn = (spawn.X + overrides.SpawnOffset.X, spawn.Y + overrides.SpawnOffset.Y);
            bool movesSpawn = overrides.SpawnTile.HasValue || overrides.SpawnBiome is not null || overrides.SpawnOffset != default;
            if (movesSpawn)
            {
                _debugSpawnPosition = new Vector2((spawn.X + 0.5f) * this.TileSize, (spawn.Y + 0.5f) * this.TileSize);
            }

            if (overrides.StartHour is float hour)
            {
                // The clock is a pure function of the tick (WorldProgression), so the start hour is a tick offset.
                float hoursAhead = (((hour - WorldProgression.DefaultStartHour) % 24f) + 24f) % 24f;
                double seconds = hoursAhead / 24.0 * WorldProgression.DefaultDayLengthSeconds;
                _currentTick = (ulong)Math.Round(seconds * this.TickRate);
            }

            string spawnText = movesSpawn ? $"{spawn.X},{spawn.Y}" : "default";
            string hourText = overrides.StartHour?.ToString("0.##", CultureInfo.InvariantCulture) ?? "default";
            return $"spawn={spawnText}, startHour={hourText}";
        }
    }
}
