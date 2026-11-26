// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Actors;

/// <summary>
/// Spawns the world's friendly NPCs as chunks come into play (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>):
/// the sanctuary blacksmith beside the world spawn (once), and per chunk a guard, harbour master or shrine keeper at
/// each settlement point of interest, plus a blacksmith in river, desert and snow villages. Each chunk is populated once.
/// <see cref="SpawnActor"/> creates the replicated entity (collider, AI brain, health, threat table for monsters) and
/// registers its <see cref="NpcActorMeta"/>. Called only under the world lock.
/// </summary>
internal sealed class SettlementNpcSpawner
{
    private readonly WorldSimulation _simulation;
    private readonly ChunkWorld2D _chunkWorld;
    private readonly Dictionary<ulong, NpcActorMeta> _actors;
    private readonly Func<ulong> _allocateNetworkId;
    private readonly float _tileSize;
    private readonly int _tickRate;
    private readonly float _defaultRadius;
    private readonly uint _playerLayer;
    private readonly uint _monsterLayer;
    private readonly uint _npcLayer;
    private readonly HashSet<long> _populatedChunks = new();
    private bool _spawnedSanctuaryBlacksmith;

    /// <summary>Initializes a new instance of the <see cref="SettlementNpcSpawner"/> class.</summary>
    public SettlementNpcSpawner(
        WorldSimulation simulation,
        ChunkWorld2D chunkWorld,
        Dictionary<ulong, NpcActorMeta> actors,
        Func<ulong> allocateNetworkId,
        float tileSize,
        int tickRate,
        float defaultRadius,
        uint playerLayer,
        uint monsterLayer,
        uint npcLayer)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _chunkWorld = chunkWorld ?? throw new ArgumentNullException(nameof(chunkWorld));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _allocateNetworkId = allocateNetworkId ?? throw new ArgumentNullException(nameof(allocateNetworkId));
        _tileSize = tileSize;
        _tickRate = tickRate;
        _defaultRadius = defaultRadius;
        _playerLayer = playerLayer;
        _monsterLayer = monsterLayer;
        _npcLayer = npcLayer;
    }

    /// <summary>Gets the number of chunks already populated.</summary>
    public int PopulatedChunkCount => _populatedChunks.Count;

    public void PopulateAround(Vector2 worldCenter, int radius)
    {
        int centerCx = Chunk2D.ToChunkCoord((int)MathF.Floor(worldCenter.X / _tileSize));
        int centerCy = Chunk2D.ToChunkCoord((int)MathF.Floor(worldCenter.Y / _tileSize));
        Vector2 safeOrigin = _chunkWorld.SpawnWorldPosition;

        if (!_spawnedSanctuaryBlacksmith)
        {
            _spawnedSanctuaryBlacksmith = true;
            int spawnTx = (int)MathF.Floor(safeOrigin.X / _tileSize);
            int spawnTy = (int)MathF.Floor(safeOrigin.Y / _tileSize);
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
                    Vector2 smithPos = new((bx + 0.5f) * _tileSize, (by + 0.5f) * _tileSize);
                    this.SpawnActor(new NpcActorMeta(
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
                            Vector2 npcPos = new((npcTx + 0.5f) * _tileSize, (npcTy + 0.5f) * _tileSize);
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
                            this.SpawnActor(npcMeta);
                        }

                        if (poi.Type is PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or PointOfInterestType.SnowVillage)
                        {
                            int smithTx = poi.TileX - 1;
                            int smithTy = poi.TileY + 1;
                            MapTile vSmithTile = _chunkWorld.GetTileAtWorld(smithTx, smithTy);
                            if (!vSmithTile.IsSolid && !_chunkWorld.HasSolidPropAt(smithTx, smithTy))
                            {
                                NpcArchetypeSpec vSmith = WorldActorCatalog.GetNpcSpec(WorldActorCatalog.MasterBlacksmith);
                                Vector2 vSmithPos = new((smithTx + 0.5f) * _tileSize, (smithTy + 0.5f) * _tileSize);
                                this.SpawnActor(new NpcActorMeta(
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
            }
        }
    }

    public void SpawnActor(in NpcActorMeta meta)
    {
        ulong netId = _allocateNetworkId();
        float radius = meta.Radius > 0f ? meta.Radius : _defaultRadius;
        Entity entity = _simulation.SpawnNetworkEntity(netId, meta.HomePos, radius, ownerSessionId: 0UL);
        uint layer = meta.Kind == EntityKind.Monster ? _monsterLayer : _npcLayer;
        _simulation.Entities.SetCollider(entity, new Collider2D(ColliderKind.Circle, radius, default, false, layer, uint.MaxValue));
        _simulation.Entities.SetVelocity(entity, new Velocity2D(0f, 0f, 5f));
        _simulation.Entities.SetHealth(entity, new HealthComponent(meta.MaxHealth, meta.MaxHealth));

        float aggro = meta.Kind == EntityKind.Monster ? MathF.Max(192f, meta.AttackRange + 48f) : 0f;
        float attackRange = meta.Kind == EntityKind.Monster ? MathF.Max(36f, meta.AttackRange) : 0f;
        uint targetMask = meta.Kind == EntityKind.Monster ? _playerLayer : 0U;
        AiBrainComponent brain = new(
            AiState.Patrol,
            meta.HomePos,
            aggro,
            attackRange,
            LeashRadius: 384f,
            meta.MoveSpeed,
            TargetLayerMask: targetMask,
            AttackCooldownTicks: (uint)Math.Max(15, _tickRate),
            PatrolRadius: meta.Kind == EntityKind.Monster ? 96f : 64f,
            WindupTicks: meta.WindupTicks);
        _simulation.Entities.SetComponent(entity, in brain);

        if (meta.Kind == EntityKind.Monster)
        {
            _ = _simulation.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(entity, static () => new ThreatTable());
        }

        _actors[netId] = meta;
    }
}
