// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Application.World.Commerce;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Actors;

/// <summary>
/// Builds villages and spawns the world's friendly NPCs as chunks come into play (audit SOLID-1, extracted from
/// <see cref="AuthoritativeWorld"/>): the sanctuary blacksmith beside the world spawn (once); per site, the village's real
/// buildings (<see cref="VillageMaterializer"/>, ADR-025) and a guard at the well plus the blacksmith at the smithy's forge;
/// a harbour master, shrine keeper or guard at other settlements. Each site is populated once.
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
    private readonly HashSet<long> _populatedSites = new();
    private readonly HashSet<long> _builtSites = new();
    private readonly VillageMaterializer? _villages;
    private readonly ISet<long>? _dirtyChunks;
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
        uint npcLayer,
        VillageMaterializer? villages = null,
        ISet<long>? dirtyChunks = null)
    {
        _villages = villages;
        _dirtyChunks = dirtyChunks;
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

    /// <summary>Gets the village builder, when villages are built from real structures.</summary>
    public VillageMaterializer? Villages => _villages;

    /// <summary>
    /// Builds the village of the site owning a chunk, once per site. Called before a chunk's delta is served and when
    /// chunks come into play, so no client ever sees a village chunk before its buildings exist.
    /// </summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkY">Chunk Y.</param>
    public void EnsureVillageBuilt(int chunkX, int chunkY)
    {
        if (_villages is null)
        {
            return;
        }

        (int sx, int sy) = VillageMaterializer.SiteOfChunk(chunkX, chunkY);
        if (_builtSites.Add(Chunk2D.PackKey(sx, sy)))
        {
            _ = _villages.Materialize(sx, sy, _dirtyChunks);
        }
    }

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
                        0U,
                        TraderProfileId: TradeService.BlacksmithProfile,
                        TraderKey: TraderKeyOf(spawnTx, spawnTy, VillageBuildingRole.Smithy),
                        TraderStyle: (byte)VillageStyle.Temperate));
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

                this.EnsureVillageBuilt(cx, cy);

                // Spawn friendly settlement NPCs once per point of interest (a POI is listed by every chunk it reaches).
                for (int i = 0; i < chunk.PointsOfInterest.Count; i++)
                {
                    PointOfInterest poi = chunk.PointsOfInterest[i];
                    if (_populatedSites.Add(Chunk2D.PackKey(poi.TileX, poi.TileY)))
                    {
                        this.SpawnSettlementNpcs(in poi, key);
                    }
                }
            }
        }
    }

    private void SpawnSettlementNpcs(in PointOfInterest poi, long chunkKey)
    {
        if (poi.Type is not (PointOfInterestType.RiverVillage or
            PointOfInterestType.Harbor or
            PointOfInterestType.MountainShrine or
            PointOfInterestType.LakesideCamp or
            PointOfInterestType.DesertVillage or
            PointOfInterestType.SnowVillage or
            PointOfInterestType.SwampVillage))
        {
            return;
        }

        int npcTx = poi.TileX + 1;
        int npcTy = poi.TileY + 1;
        if (this.IsStandable(npcTx, npcTy))
        {
            ushort npcArchetype = poi.Type == PointOfInterestType.Harbor
                ? WorldActorCatalog.HarbourMaster
                : poi.Type == PointOfInterestType.MountainShrine
                    ? WorldActorCatalog.ShrineKeeper
                    : WorldActorCatalog.VillageGuard;
            NpcArchetypeSpec npcSpec = WorldActorCatalog.GetNpcSpec(npcArchetype);
            this.SpawnActor(new NpcActorMeta(
                EntityKind.Npc,
                npcSpec.ArchetypeId,
                npcSpec.Name,
                npcSpec.Level,
                this.TileCenter(npcTx, npcTy),
                200f,
                70f,
                npcSpec.Radius,
                0f,
                0U,
                chunkKey,
                StarterGameCatalog.BerryHerb,
                1,
                0U));
        }

        if (!IsVillage(poi.Type))
        {
            return;
        }

        // The blacksmith works at the smithy's forge; without a smithy (cramped site) he stands by the well.
        (int smithTx, int smithTy) = (poi.TileX - 1, poi.TileY + 1);
        float patrol = 64f;
        if (this.TryFindNpcTile(in poi, VillageBuildingRole.Smithy, out int sTx, out int sTy))
        {
            (smithTx, smithTy) = (sTx, sTy);
            patrol = 12f;
        }

        this.SpawnTrader(in poi, VillageBuildingRole.Stall, WorldActorCatalog.VillageMerchant, "merchant", chunkKey);
        this.SpawnTrader(in poi, VillageBuildingRole.Store, WorldActorCatalog.GeneralTrader, "general", chunkKey);

        if (this.IsStandable(smithTx, smithTy))
        {
            NpcArchetypeSpec vSmith = WorldActorCatalog.GetNpcSpec(WorldActorCatalog.MasterBlacksmith);
            this.SpawnActor(new NpcActorMeta(
                EntityKind.Npc,
                vSmith.ArchetypeId,
                vSmith.Name,
                vSmith.Level,
                this.TileCenter(smithTx, smithTy),
                250f,
                45f,
                vSmith.Radius,
                0f,
                0U,
                chunkKey,
                StarterGameCatalog.IronOre,
                1,
                0U,
                PatrolRadius: patrol,
                TraderProfileId: TradeService.BlacksmithProfile,
                TraderKey: TraderKeyOf(in poi, VillageBuildingRole.Smithy),
                TraderStyle: StyleOf(poi.Type)));
        }
    }

    /// <summary>
    /// Gets the stable key of a village trader (ADR-025): a mix of the village centre and the building role. It seeds
    /// the trader's stock and restock jitter, and keys its persisted sales, so it never changes for a given world.
    /// </summary>
    public static ulong TraderKeyOf(in PointOfInterest poi, VillageBuildingRole role) => TraderKeyOf(poi.TileX, poi.TileY, role);

    /// <summary>Gets the stable key of a trader whose post is anchored at a tile (a village centre, the sanctuary).</summary>
    public static ulong TraderKeyOf(int tileX, int tileY, VillageBuildingRole role)
    {
        ulong x = ((ulong)(uint)tileX << 32) | (uint)tileY;
        x ^= ((ulong)role + 1UL) * 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    /// <summary>Gets the village style index (<see cref="VillageStyle"/>) of a village type.</summary>
    public static byte StyleOf(PointOfInterestType type) => type switch
    {
        PointOfInterestType.DesertVillage => (byte)VillageStyle.Desert,
        PointOfInterestType.SnowVillage => (byte)VillageStyle.Snow,
        PointOfInterestType.SwampVillage => (byte)VillageStyle.Swamp,
        PointOfInterestType.None or PointOfInterestType.RiverVillage or PointOfInterestType.Harbor or
        PointOfInterestType.MountainShrine or PointOfInterestType.LakesideCamp or PointOfInterestType.WorldTree or
        PointOfInterestType.MineEntrance or PointOfInterestType.AncientRuins or PointOfInterestType.BanditCamp or
        PointOfInterestType.Watchtower or PointOfInterestType.BossArena => (byte)VillageStyle.Temperate,
        _ => (byte)VillageStyle.Temperate,
    };

    // A trader stands at its building's NPC spot (behind the market stall, at the store counter) and barely moves.
    private void SpawnTrader(in PointOfInterest poi, VillageBuildingRole role, ushort archetype, string profile, long chunkKey)
    {
        if (!this.TryFindNpcTile(in poi, role, out int tx, out int ty) || !this.IsStandable(tx, ty))
        {
            return;
        }

        NpcArchetypeSpec spec = WorldActorCatalog.GetNpcSpec(archetype);
        this.SpawnActor(new NpcActorMeta(
            EntityKind.Npc,
            spec.ArchetypeId,
            spec.Name,
            spec.Level,
            this.TileCenter(tx, ty),
            180f,
            40f,
            spec.Radius,
            0f,
            0U,
            chunkKey,
            StarterGameCatalog.BerryHerb,
            1,
            0U,
            PatrolRadius: 6f,
            TraderProfileId: profile,
            TraderKey: TraderKeyOf(in poi, role),
            TraderStyle: StyleOf(poi.Type)));
    }

    /// <summary>Finds the NPC spot of the first plot with <paramref name="role"/> in the village of <paramref name="poi"/>.</summary>
    public bool TryFindNpcTile(in PointOfInterest poi, VillageBuildingRole role, out int tileX, out int tileY)
    {
        tileX = 0;
        tileY = 0;
        if (_villages is null)
        {
            return false;
        }

        (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
        ReadOnlySpan<VillagePlot> plots = _villages.PlotsOf(sx, sy);
        for (int i = 0; i < plots.Length; i++)
        {
            if (plots[i].Role == role && VillageBlueprint.TryGetNpcTile(_villages.Villages, plots[i], out tileX, out tileY))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVillage(PointOfInterestType type) =>
        type is PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or PointOfInterestType.SnowVillage or PointOfInterestType.SwampVillage;

    private bool IsStandable(int tileX, int tileY)
    {
        MapTile tile = _chunkWorld.GetTileAtWorld(tileX, tileY);
        return !tile.IsSolid &&
            tile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
            !_chunkWorld.HasSolidPropAt(tileX, tileY) &&
            !_chunkWorld.Buildings.IsSolidAt(tileX, tileY);
    }

    private Vector2 TileCenter(int tileX, int tileY) => new((tileX + 0.5f) * _tileSize, (tileY + 0.5f) * _tileSize);

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
            PatrolRadius: meta.Kind == EntityKind.Monster ? 96f : meta.PatrolRadius,
            WindupTicks: meta.WindupTicks);
        _simulation.Entities.SetComponent(entity, in brain);

        if (meta.Kind == EntityKind.Monster)
        {
            _ = _simulation.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(entity, static () => new ThreatTable());
        }

        _actors[netId] = meta;
    }
}
