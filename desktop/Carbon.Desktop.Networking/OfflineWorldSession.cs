// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Combat.Cooldowns;
using Carbon.Combat.Hitboxes;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Desktop.Networking;

/// <summary>
/// Full-loop offline single-player sandbox session running deterministic local world synthesis,
/// AI monsters/NPCs, melee hitbox combat, inventory/crafting, prop harvesting/respawns, and chunk streaming.
/// </summary>
public sealed class OfflineWorldSession : IClientWorldSession
{
    private const float WalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const uint PlayerLayer = 1U;
    private const uint MonsterLayer = 2U;
    private const uint NpcLayer = 4U;
    private const uint PrimaryActionAbilityId = 1U;

    private readonly DesktopClientOptions _options;
    private readonly InventoryContainer _inventory = new(27);
    private readonly CooldownTracker _cooldowns = new();
    private readonly List<RemoteEntityView> _actorViews = new(64);
    private readonly Dictionary<int, OfflineActorMeta> _actorsBySlot = new();
    private readonly HashSet<long> _populatedChunks = new();
    private readonly List<PendingRespawn> _pendingRespawns = new(16);
    private readonly List<ClientCombatEvent> _combatEvents = new(32);
    private readonly Entity[] _hitScratch = new Entity[32];

    private ChunkWorld2D? _chunkWorld;
    private WorldSimulation? _simulation;
    private Entity _localPlayer = Entity.Null;
    private ulong _seed;
    private ulong _nextActorNetId = 5000UL;
    private byte _selectedSlot;
    private Vector2 _lastAttackDir = Vector2.UnitX;
    private float _attackSwingTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfflineWorldSession"/> class.
    /// </summary>
    /// <param name="options">Client runtime options.</param>
    public OfflineWorldSession(DesktopClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _seed = options.Seed;
        StarterGameCatalog.GrantStarterLoadout(_inventory);
    }

    /// <inheritdoc/>
    public ChunkWorld2D ChunkWorld =>
        _chunkWorld ?? throw new InvalidOperationException("Session has not been initialized.");

    /// <inheritdoc/>
    public WorldSimulation Simulation =>
        _simulation ?? throw new InvalidOperationException("Session has not been initialized.");

    /// <inheritdoc/>
    public Entity LocalPlayer => _localPlayer;

    /// <inheritdoc/>
    public Vector2 LocalPlayerInterpolatedPosition =>
        _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
            ? _simulation.GetInterpolatedPosition(_localPlayer)
            : Vector2.Zero;

    /// <inheritdoc/>
    public float LocalPlayerHealth =>
        _simulation is not null && _simulation.Entities.IsAlive(_localPlayer) && _simulation.Entities.HasHealth(_localPlayer)
            ? _simulation.Entities.GetHealth(_localPlayer).Current
            : 100f;

    /// <inheritdoc/>
    public float LocalPlayerMaxHealth =>
        _simulation is not null && _simulation.Entities.IsAlive(_localPlayer) && _simulation.Entities.HasHealth(_localPlayer)
            ? _simulation.Entities.GetHealth(_localPlayer).Max
            : 100f;

    /// <inheritdoc/>
    public byte SelectedHotbarSlot => _selectedSlot;

    /// <inheritdoc/>
    public InventoryContainer Inventory => _inventory;

    /// <inheritdoc/>
    public Vector2 LastAttackDirection => _lastAttackDir;

    /// <inheritdoc/>
    public float AttackSwingTimer => _attackSwingTimer;

    /// <inheritdoc/>
    public IReadOnlyList<RemoteEntityView> RemoteEntities => _actorViews;

    /// <inheritdoc/>
    public bool IsOnlineMultiplayer => false;

    /// <inheritdoc/>
    public bool IsReady => _chunkWorld is not null && _simulation is not null && _simulation.Entities.IsAlive(_localPlayer);

    /// <inheritdoc/>
    public ulong WorldSeed => _seed;

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        this.BuildWorld(_seed);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Update(double dt, Vector2 moveDir, bool sprint)
    {
        if (!this.IsReady)
        {
            return;
        }

        if (_attackSwingTimer > 0f)
        {
            _attackSwingTimer = MathF.Max(0f, _attackSwingTimer - (float)dt);
        }

        if (moveDir.LengthSquared() > 0.001f)
        {
            float baseSpeed = sprint ? SprintSpeed : WalkSpeed;
            _simulation!.EnqueueCommand(SimulationCommand.MoveInput(_localPlayer, moveDir, baseSpeed));
        }

        Vector2 currentPos = _simulation!.Entities.GetTransform(_localPlayer).Position;
        _chunkWorld!.UpdateAroundPosition(currentPos, chunkRadius: 4, evictionRadius: 7);
        this.PopulateActorsAround(currentPos, radius: 2);

        _ = _simulation.Update(dt);

        this.ProcessAiAttacks();
        this.ProcessPendingRespawns();

        if ((_simulation.Clock.TickCount % 30UL) == 0UL)
        {
            _ = _chunkWorld.ProcessPropRespawns(_simulation.Clock.TickCount);
        }

        this.RefreshActorViews();
    }

    /// <inheritdoc/>
    public void SelectHotbarSlot(byte slotIndex)
    {
        if (slotIndex < 9)
        {
            _selectedSlot = slotIndex;
        }
    }

    /// <inheritdoc/>
    public void TriggerPrimaryAction(Vector2 aimWorldPosition)
    {
        if (!this.IsReady)
        {
            return;
        }

        Vector2 playerPos = _simulation!.Entities.GetTransform(_localPlayer).Position;
        Vector2 delta = aimWorldPosition - playerPos;
        _lastAttackDir = delta.LengthSquared() > 1e-4f ? Vector2.Normalize(delta) : Vector2.UnitX;

        ulong tick = _simulation.Clock.TickCount;
        if (!_cooldowns.IsReady(PrimaryActionAbilityId, tick))
        {
            return;
        }

        _cooldowns.StartCooldown(PrimaryActionAbilityId, tick, 16U);

        ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
        if (activeSlot.ItemId == StarterGameCatalog.BerryHerb && activeSlot.Quantity > 0 && _simulation.Entities.HasHealth(_localPlayer))
        {
            HealthComponent hp = _simulation.Entities.GetHealth(_localPlayer);
            if (hp.Current < hp.Max)
            {
                float nextHp = MathF.Min(hp.Max, hp.Current + 25f);
                _simulation.Entities.SetHealth(_localPlayer, new HealthComponent(nextHp, hp.Max));
                _inventory.SetSlot(_selectedSlot, activeSlot.Quantity > 1 ? activeSlot with { Quantity = activeSlot.Quantity - 1 } : ItemSlot.Empty);
                _combatEvents.Add(new ClientCombatEvent(playerPos, 25f, false, true, "+25 HP"));
                return;
            }
        }

        _attackSwingTimer = 0.22f;

        HitboxQuery.QueryArc(
            _simulation,
            playerPos,
            _lastAttackDir,
            radius: 72f,
            halfAngleRadians: 1.15f,
            _hitScratch,
            out int hitCount,
            layerMask: MonsterLayer,
            ignoreEntity: _localPlayer);

        float baseDmg = StarterGameCatalog.GetWeaponBaseDamage(activeSlot.ItemId);
        bool crit = activeSlot.ItemId == StarterGameCatalog.IronSword && ((tick % 5UL) == 0UL);
        float dmg = crit ? baseDmg * 1.5f : baseDmg;
        EntityAttachmentStore<ThreatTable> threats = _simulation.Entities.GetAttachmentStore<ThreatTable>();

        for (int i = 0; i < hitCount; i++)
        {
            Entity target = _hitScratch[i];
            if (!_simulation.Entities.IsAlive(target) || !_simulation.Entities.HasHealth(target))
            {
                continue;
            }

            Vector2 targetPos = _simulation.Entities.HasTransform(target) ? _simulation.Entities.GetTransform(target).Position : playerPos;
            HealthComponent hp = _simulation.Entities.GetHealth(target);
            float remaining = MathF.Max(0f, hp.Current - dmg);
            _simulation.Entities.SetHealth(target, new HealthComponent(remaining, hp.Max));
            _combatEvents.Add(new ClientCombatEvent(targetPos, dmg, crit, false));

            if (remaining > 0f)
            {
                threats.GetOrAdd(target, static () => new ThreatTable()).AddThreat(_localPlayer, dmg * 2f);
                if (_simulation.Entities.HasComponent<AiBrainComponent>(target))
                {
                    ref AiBrainComponent brain = ref _simulation.Entities.GetComponent<AiBrainComponent>(target);
                    brain = brain with { State = AiState.Chase, TargetEntity = _localPlayer };
                }
            }
            else
            {
                if (_actorsBySlot.Remove(target.Id, out OfflineActorMeta meta))
                {
                    _pendingRespawns.Add(new PendingRespawn(tick + 1200UL, meta));
                    _ = _inventory.TryAdd(new ItemSlot(meta.DropItemId, 2), StarterGameCatalog.GetMaxStackSize(meta.DropItemId));
                    _combatEvents.Add(new ClientCombatEvent(targetPos, 2f, false, true, $"+2 {StarterGameCatalog.GetDisplayName(meta.DropItemId)}"));
                }

                _simulation.DespawnEntity(target);
            }
        }
    }

    /// <inheritdoc/>
    public bool TryCraftRecipe(ushort recipeId)
    {
        if (!StarterGameCatalog.Recipes.TryGet(recipeId, out CraftingRecipe? recipe))
        {
            return false;
        }

        int maxStack = StarterGameCatalog.GetMaxStackSize(recipe.Output.ItemId);
        if (!RecipeMatcher.TryCraft(_inventory, recipe, maxStack))
        {
            return false;
        }

        if (this.IsReady)
        {
            _combatEvents.Add(new ClientCombatEvent(
                this.LocalPlayerInterpolatedPosition,
                recipe.Output.Quantity,
                false,
                true,
                $"Crafted {StarterGameCatalog.GetDisplayName(recipe.Output.ItemId)}"));
        }

        return true;
    }

    /// <inheritdoc/>
    public void DrainCombatEvents(List<ClientCombatEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        for (int i = 0; i < _combatEvents.Count; i++)
        {
            destination.Add(_combatEvents[i]);
        }

        _combatEvents.Clear();
    }

    /// <inheritdoc/>
    public bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady)
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _options.TileSize);
        int pty = (int)MathF.Floor(pos.Y / _options.TileSize);
        ulong respawnAt = _simulation!.Clock.TickCount + 300UL;
        byte tier = this.ResolveSelectedHarvestTier();

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.TryDamagePropAt(wx, wy, 50f, tier, respawnAt, out MapProp updated))
                {
                    modifiedChunk = (Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy));
                    if (updated.Destructibility.IsDestroyed)
                    {
                        ItemSlot drop = ResolvePropDrop(updated.Type);
                        if (!drop.IsEmpty)
                        {
                            _ = _inventory.TryAdd(in drop, StarterGameCatalog.GetMaxStackSize(drop.ItemId));
                            _combatEvents.Add(new ClientCombatEvent(
                                updated.WorldPosition,
                                drop.Quantity,
                                false,
                                true,
                                $"+{drop.Quantity} {StarterGameCatalog.GetDisplayName(drop.ItemId)}"));
                        }
                    }

                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryPlaceTestHouse(out (int ChunkX, int ChunkY) centerChunk)
    {
        centerChunk = default;
        if (!this.IsReady)
        {
            return false;
        }

        Vector2 playerPos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(playerPos.X / _options.TileSize) + 4;
        int pty = (int)MathF.Floor(playerPos.Y / _options.TileSize);

        for (int hy = 0; hy < 5; hy++)
        {
            for (int hx = 0; hx < 5; hx++)
            {
                int wx = ptx + hx;
                int wy = pty + hy;
                bool isEdge = hx == 0 || hx == 4 || hy == 0 || hy == 4;
                if (isEdge)
                {
                    StructureType edgeType = (hx == 2 && hy == 4) ? StructureType.WoodDoor : StructureType.StoneWall;
                    _chunkWorld!.Buildings.ForcePlaceStructure(wx, wy, edgeType);
                }
                else
                {
                    _chunkWorld!.Buildings.ForcePlaceStructure(wx, wy, StructureType.WoodFloor);
                }
            }
        }

        centerChunk = (Chunk2D.ToChunkCoord(ptx), Chunk2D.ToChunkCoord(pty));
        return true;
    }

    /// <inheritdoc/>
    public bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady)
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _options.TileSize);
        int pty = (int)MathF.Floor(pos.Y / _options.TileSize);

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.Buildings.ToggleDoor(wx, wy, out _))
                {
                    modifiedChunk = (Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy));
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void ReseedOffline(ulong newSeed)
    {
        _seed = newSeed;
        StarterGameCatalog.GrantStarterLoadout(_inventory);
        this.BuildWorld(newSeed);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _actorViews.Clear();
        _actorsBySlot.Clear();
        _populatedChunks.Clear();
        _pendingRespawns.Clear();
        _combatEvents.Clear();
        _localPlayer = Entity.Null;
        _simulation = null;
        _chunkWorld = null;
        return ValueTask.CompletedTask;
    }

    private void BuildWorld(ulong seed)
    {
        _actorsBySlot.Clear();
        _actorViews.Clear();
        _populatedChunks.Clear();
        _pendingRespawns.Clear();
        _combatEvents.Clear();

        ProceduralWorldGenerator generator = new(seed);
        _chunkWorld = new ChunkWorld2D(generator, _options.TileSize, _options.MacroFrequency);

        Vector2 spawnPoint = _options.FocusTileX is int fx && _options.FocusTileY is int fy
            ? new Vector2((fx + 0.5f) * _options.TileSize, (fy + 0.5f) * _options.TileSize)
            : _chunkWorld.SpawnWorldPosition;

        _chunkWorld.UpdateAroundPosition(spawnPoint, chunkRadius: 4, evictionRadius: 8);

        _simulation = new WorldSimulation(tickRate: 50, cellSize: _options.TileSize)
        {
            WorldPhysics = _chunkWorld,
        };
        _simulation.RegisterSystem(new AiBehaviorSystem(_chunkWorld.NavGrid, _options.TileSize));

        _localPlayer = _simulation.SpawnEntity(spawnPoint, radius: 7f);
        _simulation.Entities.SetCollider(_localPlayer, new Collider2D(ColliderKind.Circle, 7f, default, false, PlayerLayer, uint.MaxValue));
        _simulation.Entities.SetVelocity(_localPlayer, new Velocity2D(0f, 0f, 5f));
        _simulation.Entities.SetHealth(_localPlayer, new HealthComponent(100f, 100f));

        this.PopulateActorsAround(spawnPoint, radius: 2);
        this.RefreshActorViews();
    }

    private void PopulateActorsAround(Vector2 centerWorld, int radius)
    {
        if (_chunkWorld is null || _simulation is null)
        {
            return;
        }

        int centerCx = Chunk2D.ToChunkCoord((int)MathF.Floor(centerWorld.X / _options.TileSize));
        int centerCy = Chunk2D.ToChunkCoord((int)MathF.Floor(centerWorld.Y / _options.TileSize));
        Vector2 safeOrigin = _chunkWorld.SpawnWorldPosition;
        float safeRadiusSq = 100f * _options.TileSize * _options.TileSize;

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
                        int tx = poi.TileX + 1;
                        int ty = poi.TileY + 1;
                        if (!_chunkWorld.GetTileAtWorld(tx, ty).IsSolid)
                        {
                            Vector2 pos = new((tx + 0.5f) * _options.TileSize, (ty + 0.5f) * _options.TileSize);
                            string name = poi.Type == PointOfInterestType.Harbor ? "Harbour Master" : "Village Guard";
                            this.SpawnActor(new OfflineActorMeta(++_nextActorNetId, EntityKind.Npc, name, pos, 200f, 70f, StarterGameCatalog.BerryHerb));
                        }
                    }
                }

                ulong h = HashChunk(_seed, cx, cy);
                if ((h % 10UL) >= 7UL)
                {
                    continue;
                }

                int lx = 6 + (int)(h % 20UL);
                int ly = 6 + (int)((h >> 8) % 20UL);
                MapTile tile = chunk.GetTile(lx, ly);
                if (tile.IsSolid || tile.Biome == BiomeType.ShallowWater || tile.Biome == BiomeType.DeepOcean)
                {
                    continue;
                }

                Vector2 mobPos = new(((cx * Chunk2D.Size) + lx + 0.5f) * _options.TileSize, ((cy * Chunk2D.Size) + ly + 0.5f) * _options.TileSize);
                if (Vector2.DistanceSquared(mobPos, safeOrigin) < safeRadiusSq)
                {
                    continue;
                }

                (string mobName, float maxHp, float speed, uint dropId) = ResolveBiomeMonster(tile.Biome);
                this.SpawnActor(new OfflineActorMeta(++_nextActorNetId, EntityKind.Monster, mobName, mobPos, maxHp, speed, dropId));
            }
        }
    }

    private void SpawnActor(in OfflineActorMeta meta)
    {
        if (_simulation is null)
        {
            return;
        }

        Entity entity = _simulation.SpawnNetworkEntity(meta.NetworkId, meta.HomePos, 7f, 0UL);
        uint layer = meta.Kind == EntityKind.Monster ? MonsterLayer : NpcLayer;
        _simulation.Entities.SetCollider(entity, new Collider2D(ColliderKind.Circle, 7f, default, false, layer, uint.MaxValue));
        _simulation.Entities.SetVelocity(entity, new Velocity2D(0f, 0f, 5f));
        _simulation.Entities.SetHealth(entity, new HealthComponent(meta.MaxHealth, meta.MaxHealth));

        AiBrainComponent brain = new(
            AiState.Patrol,
            meta.HomePos,
            AggroRadius: meta.Kind == EntityKind.Monster ? 192f : 0f,
            AttackRange: meta.Kind == EntityKind.Monster ? 44f : 0f,
            LeashRadius: 384f,
            meta.MoveSpeed,
            TargetLayerMask: meta.Kind == EntityKind.Monster ? PlayerLayer : 0U,
            AttackCooldownTicks: 40U,
            PatrolRadius: meta.Kind == EntityKind.Monster ? 96f : 64f);
        _simulation.Entities.SetComponent(entity, in brain);

        if (meta.Kind == EntityKind.Monster)
        {
            _ = _simulation.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(entity, static () => new ThreatTable());
        }

        _actorsBySlot[entity.Id] = meta;
    }

    private void ProcessAiAttacks()
    {
        if (_simulation is null || !_simulation.Entities.IsAlive(_localPlayer))
        {
            return;
        }

        ReadOnlySpan<AiAttackReadyEvent> events = _simulation.ReadEvents<AiAttackReadyEvent>();
        for (int i = 0; i < events.Length; i++)
        {
            AiAttackReadyEvent evt = events[i];
            if (evt.Target != _localPlayer || !_simulation.Entities.IsAlive(evt.Attacker) ||
                !_simulation.Entities.HasTransform(evt.Attacker) || !_simulation.Entities.HasHealth(_localPlayer))
            {
                continue;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(_localPlayer).Position;
            Vector2 atkPos = _simulation.Entities.GetTransform(evt.Attacker).Position;
            if (Vector2.DistanceSquared(atkPos, playerPos) > (56f * 56f))
            {
                continue;
            }

            HealthComponent hp = _simulation.Entities.GetHealth(_localPlayer);
            float next = MathF.Max(0f, hp.Current - 10f);
            if (next <= 0f)
            {
                _simulation.Entities.SetHealth(_localPlayer, new HealthComponent(hp.Max, hp.Max));
                _simulation.EnqueueCommand(SimulationCommand.Teleport(_localPlayer, _chunkWorld!.SpawnWorldPosition));
                _combatEvents.Add(new ClientCombatEvent(playerPos, 10f, false, false, "Respawned"));
            }
            else
            {
                _simulation.Entities.SetHealth(_localPlayer, new HealthComponent(next, hp.Max));
                _combatEvents.Add(new ClientCombatEvent(playerPos, 10f, false, false));
            }
        }

        _simulation.ClearEvents<AiAttackReadyEvent>();
    }

    private void ProcessPendingRespawns()
    {
        if (_simulation is null)
        {
            return;
        }

        ulong tick = _simulation.Clock.TickCount;
        for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
        {
            PendingRespawn p = _pendingRespawns[i];
            if (tick >= p.RespawnTick)
            {
                _pendingRespawns.RemoveAt(i);
                this.SpawnActor(p.Meta);
            }
        }
    }

    private void RefreshActorViews()
    {
        _actorViews.Clear();
        if (_simulation is null)
        {
            return;
        }

        IReadOnlyList<Entity> active = _simulation.Entities.ActiveEntities;
        for (int i = 0; i < active.Count; i++)
        {
            Entity entity = active[i];
            if (entity == _localPlayer || !_actorsBySlot.TryGetValue(entity.Id, out OfflineActorMeta meta) || !_simulation.Entities.HasTransform(entity))
            {
                continue;
            }

            Vector2 pos = _simulation.GetInterpolatedPosition(entity);
            Vector2 vel = _simulation.Entities.HasVelocity(entity) ? _simulation.Entities.GetVelocity(entity).Linear : Vector2.Zero;
            float hp = _simulation.Entities.HasHealth(entity) ? _simulation.Entities.GetHealth(entity).Current : meta.MaxHealth;
            _actorViews.Add(new RemoteEntityView(meta.NetworkId, meta.Kind, meta.Name, pos, vel, 7f, hp, meta.MaxHealth));
        }
    }

    private byte ResolveSelectedHarvestTier()
    {
        ItemSlot slot = _inventory.GetSlot(_selectedSlot);
        if (!slot.IsEmpty && StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def) && def.HarvestTier > 0)
        {
            return def.HarvestTier;
        }

        return 3;
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

    private static (string Name, float MaxHp, float MoveSpeed, uint DropItemId) ResolveBiomeMonster(BiomeType biome)
    {
        if (biome is BiomeType.RedMesa or BiomeType.DuneDesert)
        {
            return ("Rock Golem", 110f, 115f, StarterGameCatalog.IronOre);
        }

        if (biome is BiomeType.FrozenTundra or BiomeType.AlpinePeaks)
        {
            return ("Frost Wolf", 85f, 150f, StarterGameCatalog.StoneChunk);
        }

        if (biome is BiomeType.VolcanicAshland or BiomeType.BlossomWoods)
        {
            return ("Ash Wraith", 95f, 140f, StarterGameCatalog.ManaCrystal);
        }

        return ("Wild Slime", 60f, 125f, StarterGameCatalog.BerryHerb);
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

    private readonly record struct OfflineActorMeta(
        ulong NetworkId,
        EntityKind Kind,
        string Name,
        Vector2 HomePos,
        float MaxHealth,
        float MoveSpeed,
        uint DropItemId);

    private readonly record struct PendingRespawn(ulong RespawnTick, OfflineActorMeta Meta);
}
