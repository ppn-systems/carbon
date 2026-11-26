// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Combat.Cooldowns;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Items.Systems;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World;

/// <summary>
/// Thread-safe authoritative world simulation coordinator bridging fixed-tick ECS physics,
/// AI monsters/NPCs, melee &amp; skill combat, inventory/crafting persistence, procedural chunk deltas,
/// Spatial AoI replication, and zero-allocation tick dispatches.
/// </summary>
public sealed partial class AuthoritativeWorld : IAuthoritativeWorld
{
    private const float BaseWalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const float BuildInteractRangeTiles = 3.5f;
    private const float ToolHarvestRangePx = 52f;
    private const float MeleeStrikeRangePx = 56f;
    private const float DefaultEntityRadius = 7f;
    private const uint PlayerLayer = 1U;
    private const uint MonsterLayer = 2U;
    private const uint NpcLayer = 4U;
    private const uint LootLayer = 8U;
    private const uint ProjectileLayer = 16U;
    private const int PlayerInventoryCapacity = PlayerInventoryLayout.TotalCapacity;
    private const uint AttackAbilityId = 1U;
    private const uint SkillAbilityIdBase = 100U;
    private const uint ConsumableAbilityId = 5U;
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
    private readonly Dictionary<ulong, ProjectileActorMeta> _projectilesByNetworkId = new();
    private readonly HashSet<long> _populatedChunks = new();
    private readonly List<PendingMonsterRespawn> _pendingRespawns = new(32);
    private readonly HashSet<long> _dirtyChunks = new();
    private readonly Dictionary<Guid, CharacterRecord> _dirtyCharacters = new();

    // Reusable scratch buffers (zero per-tick heap allocations under steady state).
    private readonly List<ulong> _scratchEntered = new(64);
    private readonly List<ulong> _scratchVisible = new(128);
    private readonly List<ulong> _scratchExited = new(64);
    // Double-buffered staging vs dispatch queues: staging is mutated only under _gate and swapped into dispatch at tick end.
    private List<OutboundSpawn> _pendingSpawns = new(64);
    private List<OutboundDespawn> _pendingDespawns = new(64);
    private List<OutboundDelta> _pendingDeltas = new(64);
    private List<EntityDeltaEntry> _flatDeltaEntries = new(512);
    private List<OutboundCombatHit> _pendingCombatHits = new(32);
    private List<OutboundSlotChange> _pendingSlotChanges = new(32);
    private List<OutboundPropChange> _pendingPropChanges = new(16);
    private List<OutboundStructureChange> _pendingStructureChanges = new(16);
    private List<OutboundEquipmentChange> _pendingEquipmentChanges = new(16);
    private List<OutboundStatsSync> _pendingStatsSyncs = new(32);
    private List<OutboundSkillCast> _pendingSkillCasts = new(16);
    private List<ulong> _flatRecipients = new(256);

    private List<OutboundSpawn> _dispatchSpawns = new(64);
    private List<OutboundDespawn> _dispatchDespawns = new(64);
    private List<OutboundDelta> _dispatchDeltas = new(64);
    private List<EntityDeltaEntry> _dispatchDeltaEntries = new(512);
    private List<OutboundCombatHit> _dispatchCombatHits = new(32);
    private List<OutboundSlotChange> _dispatchSlotChanges = new(32);
    private List<OutboundPropChange> _dispatchPropChanges = new(16);
    private List<OutboundStructureChange> _dispatchStructureChanges = new(16);
    private List<OutboundEquipmentChange> _dispatchEquipmentChanges = new(16);
    private List<OutboundStatsSync> _dispatchStatsSyncs = new(32);
    private List<OutboundSkillCast> _dispatchSkillCasts = new(16);
    private List<ulong> _dispatchRecipients = new(256);
    private readonly List<ulong> _aoiRecipientsScratch = new(64);
    private readonly List<Entity> _dirtyResetScratch = new(256);
    private readonly List<ulong> _staleLootScratch = new(32);
    private readonly List<ulong> _expiredProjectilesScratch = new(32);
    private readonly List<(ushort SlotIndex, ItemSlot Slot)> _mutationSlotScratch = new(8);
    private readonly List<(int TileX, int TileY, uint Revision, float MaxDurability)> _respawnedPropsScratch = new(16);
    private readonly Entity[] _combatHitScratch = new Entity[32];
    private readonly Entity[] _packAggroScratch = new Entity[16];

    private bool _spawnedSanctuaryBlacksmith;
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

            // Anti-duplication guard: evict any duplicate active session for the same character ID.
            ulong duplicateConnectionId = 0UL;
            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
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
                StarterGameCatalog.GrantStarterLoadout(inventory, (byte)character.Class);
                character = character with { Flags = character.Flags | CharacterTraits.StarterLoadoutGranted };
            }

            ushort startLevel = Math.Max((ushort)1, character.Level);
            byte selectedSlot = character.SelectedHotbarSlot < PlayerInventoryLayout.HotbarCount ? character.SelectedHotbarSlot : (byte)0;
            CharacterProgressionState startProg = new();
            if (!CharacterProgressionBinaryCodec.TryDeserializeInto(character.ProgressionBlob ?? [], startProg))
            {
                startProg.Clear();
            }

            if (character.UnlockedSkills != CharacterSkillMask.None && startProg.UnlockedSkillMask == CharacterSkillMask.None)
            {
                startProg.SetUnlockedSkillMask(character.UnlockedSkills);
            }

            DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(startLevel, inventory, selectedSlot, startProg);
            float startHp = MathF.Min(stats.MaxHealth, MathF.Max(1f, character.CurrentHealth));
            _simulation.Entities.SetHealth(entity, new HealthComponent(startHp, stats.MaxHealth));

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

            float startMana = Math.Clamp(character.CurrentMana, 0f, stats.MaxMana);
            float startStamina = Math.Clamp(character.CurrentStamina, 0f, stats.MaxStamina);

            ActivePlayerState playerState = new(session, entity, character, inventory)
            {
                Level = startLevel,
                Experience = character.Experience,
                Stats = stats,
                CurrentMana = startMana,
                CurrentStamina = startStamina,
                StatsDirty = true,
                LastSavedPosition = spawnPos,
            };
            startProg.CopyTo(playerState.Progression);

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
    public bool TryGetPendingCharacterSnapshot(Guid characterId, out CharacterRecord? character)
    {
        lock (_gate)
        {
            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                if (kvp.Value.SourceCharacter.Id == characterId)
                {
                    character = this.BuildUpdatedRecordLocked(kvp.Value, serializeInventory: true);
                    return true;
                }
            }

            if (_dirtyCharacters.TryGetValue(characterId, out CharacterRecord? cached))
            {
                character = cached;
                return true;
            }

            character = null;
            return false;
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
        if (!float.IsFinite(aimWorldPosition.X) || !float.IsFinite(aimWorldPosition.Y))
        {
            return OpStatus.Invalid;
        }

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
    public OpStatus TryAllocateCharacterPoint(ulong connectionId, PointAllocationKind kind, ushort targetId, byte amount = 1)
    {
        int requested = Math.Clamp((int)amount, 1, 10);
        PlayerStatsSnapshot immediateSync;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
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

    private void PopulateChunkActorsAroundLocked(Vector2 worldCenter, int radius)
    {
        int centerCx = Chunk2D.ToChunkCoord((int)MathF.Floor(worldCenter.X / this.TileSize));
        int centerCy = Chunk2D.ToChunkCoord((int)MathF.Floor(worldCenter.Y / this.TileSize));
        Vector2 safeOrigin = _chunkWorld.SpawnWorldPosition;
        float safeRadiusSq = 100f * this.TileSize * this.TileSize;

        if (!_spawnedSanctuaryBlacksmith)
        {
            _spawnedSanctuaryBlacksmith = true;
            int spawnTx = (int)MathF.Floor(safeOrigin.X / this.TileSize);
            int spawnTy = (int)MathF.Floor(safeOrigin.Y / this.TileSize);
            long spawnChunkKey = Chunk2D.PackKey(Chunk2D.ToChunkCoord(spawnTx), Chunk2D.ToChunkCoord(spawnTy));
            int[] offsets = [2, -1, -2, -1, 2, 1, 0, -2, -2, 1, 3, 0];
            for (int oi = 0; oi < offsets.Length; oi += 2)
            {
                int bx = spawnTx + offsets[oi];
                int by = spawnTy + offsets[oi + 1];
                MapTile smithTile = _chunkWorld.GetTileAtWorld(bx, by);
                if (!smithTile.IsSolid &&
                    smithTile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
                    !_chunkWorld.HasSolidPropAt(bx, by))
                {
                    NpcArchetypeSpec smithSpec = WorldActorCatalog.GetNpcSpec(WorldActorCatalog.MasterBlacksmith);
                    Vector2 smithPos = new((bx + 0.5f) * this.TileSize, (by + 0.5f) * this.TileSize);
                    this.SpawnActorFromMetaLocked(new NpcActorMeta(
                        EntityKind.Npc,
                        smithSpec.ArchetypeId,
                        smithSpec.Name,
                        smithSpec.Level,
                        smithPos,
                        250f,
                        45f,
                        smithSpec.Radius,
                        0f,
                        0U,
                        spawnChunkKey,
                        StarterGameCatalog.IronOre,
                        1,
                        0U));
                    break;
                }
            }
        }

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
                            ushort npcArchetype = poi.Type == PointOfInterestType.Harbor
                                ? WorldActorCatalog.HarbourMaster
                                : poi.Type == PointOfInterestType.MountainShrine
                                    ? WorldActorCatalog.ShrineKeeper
                                    : WorldActorCatalog.VillageGuard;
                            NpcArchetypeSpec npcSpec = WorldActorCatalog.GetNpcSpec(npcArchetype);
                            Vector2 npcPos = new((npcTx + 0.5f) * this.TileSize, (npcTy + 0.5f) * this.TileSize);
                            NpcActorMeta npcMeta = new(
                                EntityKind.Npc,
                                npcSpec.ArchetypeId,
                                npcSpec.Name,
                                npcSpec.Level,
                                npcPos,
                                200f,
                                70f,
                                npcSpec.Radius,
                                0f,
                                0U,
                                key,
                                StarterGameCatalog.BerryHerb,
                                1,
                                0U);
                            this.SpawnActorFromMetaLocked(npcMeta);
                        }

                        if (poi.Type is PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or PointOfInterestType.SnowVillage)
                        {
                            int smithTx = poi.TileX - 1;
                            int smithTy = poi.TileY + 1;
                            MapTile vSmithTile = _chunkWorld.GetTileAtWorld(smithTx, smithTy);
                            if (!vSmithTile.IsSolid && !_chunkWorld.HasSolidPropAt(smithTx, smithTy))
                            {
                                NpcArchetypeSpec vSmith = WorldActorCatalog.GetNpcSpec(WorldActorCatalog.MasterBlacksmith);
                                Vector2 vSmithPos = new((smithTx + 0.5f) * this.TileSize, (smithTy + 0.5f) * this.TileSize);
                                this.SpawnActorFromMetaLocked(new NpcActorMeta(
                                    EntityKind.Npc,
                                    vSmith.ArchetypeId,
                                    vSmith.Name,
                                    vSmith.Level,
                                    vSmithPos,
                                    250f,
                                    45f,
                                    vSmith.Radius,
                                    0f,
                                    0U,
                                    key,
                                    StarterGameCatalog.IronOre,
                                    1,
                                    0U));
                            }
                        }
                    }
                }

                // Deterministic biome monster spawn (~70% of wilderness chunks away from safe starter zone).
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
                    spawnTile.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean ||
                    _chunkWorld.HasSolidPropAt(wx, wy))
                {
                    continue;
                }

                Vector2 monsterPos = new((wx + 0.5f) * this.TileSize, (wy + 0.5f) * this.TileSize);
                if (Vector2.DistanceSquared(monsterPos, safeOrigin) < safeRadiusSq)
                {
                    continue;
                }

                byte zoneTier = WorldProgression.GetZoneTier(this.WorldSeed, wx, wy);
                ushort monsterArchetype = ResolveBiomeMonsterArchetype(spawnTile.Biome, h);
                ScaledMonsterStats scaled = WorldActorCatalog.ScaleMonsterForZone(monsterArchetype, zoneTier);

                NpcActorMeta monsterMeta = new(
                    EntityKind.Monster,
                    scaled.ArchetypeId,
                    scaled.Name,
                    scaled.Level,
                    monsterPos,
                    scaled.MaxHealth,
                    scaled.MoveSpeed,
                    scaled.Radius,
                    scaled.AttackDamage,
                    scaled.WindupTicks,
                    key,
                    scaled.DropItemId,
                    scaled.DropQuantity,
                    scaled.ExperienceReward,
                    scaled.AttackRange,
                    scaled.ProjectileSpeed,
                    scaled.ProjectileArchetype);
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

        float aggro = meta.Kind == EntityKind.Monster ? MathF.Max(192f, meta.AttackRange + 48f) : 0f;
        float attackRange = meta.Kind == EntityKind.Monster ? MathF.Max(36f, meta.AttackRange) : 0f;
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

    private bool RemovePlayerLocked(ulong connectionId)
    {
        if (!_playersByConnection.Remove(connectionId, out ActivePlayerState? state))
        {
            return false;
        }

        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
        _ = _connectionByNetworkId.Remove(state.Session.PlayerNetworkId);
        _aoi.UntrackConnection(connectionId);
        _simulation.DespawnEntity(state.Entity);
        return true;
    }

    private void MarkCharacterDirtyLocked(ActivePlayerState state, bool inventoryChanged = true)
    {
        if (inventoryChanged)
        {
            state.InventoryDirty = true;
        }

        state.PersistenceDirty = true;
        if (!_simulation.Entities.IsAlive(state.Entity) || !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        CharacterRecord updated = this.BuildUpdatedRecordLocked(state, state.InventoryDirty);
        state.SourceCharacter = updated;
        state.LastSavedPosition = new Vector2(updated.PositionX, updated.PositionY);
        state.InventoryDirty = false;
        _dirtyCharacters[updated.Id] = updated;
    }

    private CharacterRecord BuildUpdatedRecordLocked(ActivePlayerState state, bool serializeInventory)
    {
        Vector2 pos = _simulation.Entities.IsAlive(state.Entity) && _simulation.Entities.HasTransform(state.Entity)
            ? _simulation.Entities.GetTransform(state.Entity).Position
            : new Vector2(state.SourceCharacter.PositionX, state.SourceCharacter.PositionY);

        HealthComponent health = _simulation.Entities.IsAlive(state.Entity) && _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity)
            : new HealthComponent(state.SourceCharacter.CurrentHealth, state.SourceCharacter.MaxHealth);

        byte[] blob = serializeInventory
            ? InventoryBinaryCodec.Serialize(state.Inventory)
            : state.SourceCharacter.InventoryBlob;

        return state.SourceCharacter with
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
            InventoryBlob = blob,
            UpdatedAtUtc = _time.GetUtcNow(),
            UnlockedSkills = state.Progression.UnlockedSkillMask,
            ProgressionBlob = CharacterProgressionBinaryCodec.Serialize(state.Progression),
        };
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
            level = meta.Level;
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
        else if (_projectilesByNetworkId.TryGetValue(networkId, out ProjectileActorMeta projMeta))
        {
            kind = EntityKind.Projectile;
            name = string.Empty;
            archetype = projMeta.VisualArchetype;
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

    private void RecomputeDerivedStatsLocked(ActivePlayerState state)
    {
        DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(state.Level, state.Inventory, state.Session.SelectedHotbarSlot, state.Progression);
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

    private void EnqueueEquipmentBroadcastLocked(ActivePlayerState state, Vector2 worldPos)
    {
        this.CollectNearbyConnectionsAroundWorldLocked(worldPos, _aoiRecipientsScratch);
        if (_aoiRecipientsScratch.Count == 0)
        {
            return;
        }

        int offset = _flatRecipients.Count;
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            _flatRecipients.Add(_aoiRecipientsScratch[i]);
        }

        ItemSlot held = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
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
            state.Level,
            state.Stats.MaxHealth));
    }

    private PlayerStatsSnapshot BuildPlayerStatsSnapshotLocked(ActivePlayerState state, bool clearLevelUpPending)
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
            cdSecs);
    }

    private void EnqueueStatsSyncLocked(ActivePlayerState state, bool leveledUp)
    {
        PlayerStatsSnapshot snap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: leveledUp);
        if (leveledUp)
        {
            snap = snap with { LeveledUp = true };
        }

        _pendingStatsSyncs.Add(new OutboundStatsSync(state.Session.ConnectionId, snap));
    }

    private float GetCooldownSecondsRemaining(CooldownTracker tracker, uint abilityId)
    {
        uint remTicks = tracker.GetRemainingTicks(abilityId, _currentTick);
        return remTicks > 0U ? (float)remTicks / this.TickRate : 0f;
    }

    private static ushort ResolveBiomeMonsterArchetype(BiomeType biome, ulong chunkHash)
    {
        if (biome is BiomeType.RedMesa or BiomeType.DuneDesert)
        {
            ulong roll = (chunkHash >> 12) % 4UL;
            return roll == 0UL
                ? WorldActorCatalog.CrimsonDemon
                : (roll == 1UL ? WorldActorCatalog.SkeletonArcher : WorldActorCatalog.RockGolem);
        }

        if (biome is BiomeType.FrozenTundra or BiomeType.AlpinePeaks)
        {
            return WorldActorCatalog.FrostWolf;
        }

        if (biome is BiomeType.VolcanicAshland)
        {
            return ((chunkHash >> 12) % 2UL == 0UL) ? WorldActorCatalog.CrimsonDemon : WorldActorCatalog.AshWraith;
        }

        if (biome is BiomeType.BlossomWoods)
        {
            return WorldActorCatalog.AshWraith;
        }

        if (biome is BiomeType.MistySwamp or BiomeType.TropicalJungle or BiomeType.Savanna)
        {
            return ((chunkHash >> 12) % 3UL == 0UL) ? WorldActorCatalog.OrcShaman : WorldActorCatalog.WildSlime;
        }

        return ((chunkHash >> 12) % 5UL <= 1UL) ? WorldActorCatalog.SkeletonArcher : WorldActorCatalog.WildSlime;
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
        public Vector2 LastSavedPosition { get; set; }
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
        public CharacterProgressionState Progression { get; } = new();
        public uint CombatRollNonce { get; set; }
        public DerivedCharacterStats Stats { get; set; } = new(100f, 80f, 100f, 2f, 1f, 1f);
        public float CurrentMana { get; set; } = 80f;
        public float CurrentStamina { get; set; } = 100f;
        public bool StatsDirty { get; set; }
        public bool LeveledUpPending { get; set; }
        public bool PersistenceDirty { get; set; }
        public bool InventoryDirty { get; set; }
    }

    private readonly record struct LootDropMeta(ItemSlot Item, ulong SpawnTick);

    private readonly record struct ProjectileActorMeta(
        Entity Entity,
        ulong OwnerNetworkId,
        ulong OwnerConnectionId,
        bool FromPlayer,
        ushort VisualArchetype,
        float Damage,
        bool IsCritical,
        Vector2 Velocity,
        uint RemainingTicks);

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
        ushort Level,
        float MaxHealth);

    private readonly record struct OutboundStatsSync(
        ulong ConnectionId,
        PlayerStatsSnapshot Snapshot);

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
        ushort Level,
        Vector2 HomePos,
        float MaxHealth,
        float MoveSpeed,
        float Radius,
        float AttackDamage,
        uint WindupTicks,
        long ChunkKey,
        uint DropItemId,
        int DropQuantity,
        uint ExperienceReward,
        float AttackRange = 44f,
        float ProjectileSpeed = 0f,
        byte ProjectileArchetype = 0);

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
