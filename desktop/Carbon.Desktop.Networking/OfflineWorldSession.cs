// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Desktop.Networking;

/// <summary>
/// Self-contained offline single-player client session running deterministic local world synthesis,
/// fixed-tick physics, prop harvesting/respawns, and chunk streaming without network dependencies.
/// </summary>
public sealed class OfflineWorldSession : IClientWorldSession
{
    private const float WalkSpeed = 250f;
    private const float SprintSpeed = 450f;

    private static readonly RemoteEntityView[] s_noRemoteEntities = [];

    private readonly DesktopClientOptions _options;
    private ChunkWorld2D? _chunkWorld;
    private WorldSimulation? _simulation;
    private Entity _localPlayer = Entity.Null;
    private ulong _seed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfflineWorldSession"/> class.
    /// </summary>
    /// <param name="options">Client runtime options.</param>
    public OfflineWorldSession(DesktopClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _seed = options.Seed;
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
    public IReadOnlyList<RemoteEntityView> RemoteEntities => s_noRemoteEntities;

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

        if (moveDir.LengthSquared() > 0.001f)
        {
            float baseSpeed = sprint ? SprintSpeed : WalkSpeed;
            _simulation!.EnqueueCommand(SimulationCommand.MoveInput(_localPlayer, moveDir, baseSpeed));
        }

        Vector2 currentPos = _simulation!.Entities.GetTransform(_localPlayer).Position;
        _chunkWorld!.UpdateAroundPosition(currentPos, chunkRadius: 4, evictionRadius: 7);
        _ = _simulation.Update(dt);

        if ((_simulation.Clock.TickCount % 30UL) == 0UL)
        {
            _ = _chunkWorld.ProcessPropRespawns(_simulation.Clock.TickCount);
        }
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

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.TryDamagePropAt(wx, wy, 50f, toolHarvestTier: 3, respawnAt, out _))
                {
                    modifiedChunk = (Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy));
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
        this.BuildWorld(newSeed);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _localPlayer = Entity.Null;
        _simulation = null;
        _chunkWorld = null;
        return ValueTask.CompletedTask;
    }

    private void BuildWorld(ulong seed)
    {
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

        _localPlayer = _simulation.SpawnEntity(spawnPoint, radius: 7f);
        _simulation.Entities.SetVelocity(_localPlayer, new Velocity2D(0f, 0f, 5f));
    }
}
