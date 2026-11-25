// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.C2S;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Identity.C2S;
using Carbon.Contracts.Identity.S2C;
using Carbon.Contracts.Maps.C2S;
using Carbon.Contracts.Maps.S2C;
using Carbon.Desktop.Abstractions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Networking.Protocols;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking;

/// <summary>
/// Live WebSocket multiplayer session built on <c>Nalix.SDK</c> (<c>14.4.0</c>), supporting authentication,
/// character enter-world, chunk delta streaming, local client prediction, server reconciliation, and AoI interpolation.
/// </summary>
public sealed class NalixMultiplayerSession : IClientWorldSession
{
    private const float WalkSpeed = 250f;
    private const float SprintSpeed = 450f;

    private readonly DesktopClientOptions _options;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly ConcurrentDictionary<ulong, RemoteEntityState> _remoteEntities = new();
    private readonly ConcurrentDictionary<long, uint> _requestedChunks = new();
    private readonly List<RemoteEntityView> _remoteViewSnapshot = new(64);
    private readonly Lock _worldGate = new();

    private WebSocketSession? _session;
    private ChunkWorld2D? _chunkWorld;
    private WorldSimulation? _simulation;
    private Entity _localPlayer = Entity.Null;
    private ulong _localNetworkId;
    private ulong _worldSeed;
    private float _tileSize = 32f;
    private uint _clientTick;
    private Vector2 _pendingServerReconcile;
    private bool _hasServerReconcile;

    /// <summary>
    /// Initializes a new instance of the <see cref="NalixMultiplayerSession"/> class.
    /// </summary>
    /// <param name="options">Client runtime options with valid <see cref="DesktopClientOptions.ServerUrl"/>.</param>
    public NalixMultiplayerSession(DesktopClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            throw new ArgumentException("ServerUrl must be set for multiplayer sessions.", nameof(options));
        }
    }

    /// <inheritdoc/>
    public ChunkWorld2D ChunkWorld =>
        _chunkWorld ?? throw new InvalidOperationException("Multiplayer session has not completed EnterWorld.");

    /// <inheritdoc/>
    public WorldSimulation Simulation =>
        _simulation ?? throw new InvalidOperationException("Multiplayer session has not completed EnterWorld.");

    /// <inheritdoc/>
    public Entity LocalPlayer => _localPlayer;

    /// <inheritdoc/>
    public Vector2 LocalPlayerInterpolatedPosition =>
        _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
            ? _simulation.GetInterpolatedPosition(_localPlayer)
            : Vector2.Zero;

    /// <inheritdoc/>
    public IReadOnlyList<RemoteEntityView> RemoteEntities
    {
        get
        {
            _remoteViewSnapshot.Clear();
            foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
            {
                RemoteEntityState s = kvp.Value;
                _remoteViewSnapshot.Add(new RemoteEntityView(
                    s.NetworkId,
                    s.Kind,
                    s.Name,
                    s.Position,
                    s.Velocity,
                    s.Radius,
                    s.CurrentHealth,
                    s.MaxHealth));
            }

            return _remoteViewSnapshot;
        }
    }

    /// <inheritdoc/>
    public bool IsOnlineMultiplayer => true;

    /// <inheritdoc/>
    public bool IsReady => _chunkWorld is not null && _simulation is not null && _simulation.Entities.IsAlive(_localPlayer);

    /// <inheritdoc/>
    public ulong WorldSeed => _worldSeed;

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Uri serverUri = new(_options.ServerUrl!, UriKind.Absolute);
        bool useTls = string.Equals(serverUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
        ushort port = serverUri.Port > 0 ? (ushort)serverUri.Port : (ushort)(useTls ? 443 : 57206);
        string path = string.IsNullOrWhiteSpace(serverUri.AbsolutePath) ? "/ws/" : serverUri.AbsolutePath;

        WebSocketSession ws = new(
            new TransportOptions
            {
                Address = serverUri.Host,
                Port = port,
                ServerPublicKey = _options.ServerPublicKeyHex ?? string.Empty,
            },
            new WebSocketTransportOptions
            {
                Path = path,
                UseTls = useTls,
            });
        _session = ws;

        // Always subscribe before connecting so early pushes are never dropped.
        _subscriptions.Add(ws.On<EntitySpawn>(this.OnEntitySpawn));
        _subscriptions.Add(ws.On<EntityDespawn>(this.OnEntityDespawn));
        _subscriptions.Add(ws.On<EntityDeltaBatch>(this.OnEntityDeltaBatch));
        _subscriptions.Add(ws.On<MapPropChanged>(this.OnMapPropChanged));
        _subscriptions.Add(ws.On<MapStructureChanged>(this.OnMapStructureChanged));

        await ws.ConnectAsync(null, null, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_options.ServerPublicKeyHex))
        {
            await ws.HandshakeAsync(ct).ConfigureAwait(false);
        }

        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(ws.State.EncryptionEnabled);

        using PacketScope<AuthLogin> loginLease = PacketFactory<AuthLogin>.Acquire();
        loginLease.Value.Username = _options.Username;
        loginLease.Value.Password = _options.Password;
        loginLease.Value.ClientProtocolVersion = CarbonProtocol.Version;

        using AuthResult loginResult = await ws.RequestAsync<AuthResult>(
            loginLease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        if (loginResult.Status == AuthStatus.InvalidCredentials)
        {
            using PacketScope<AuthRegister> regLease = PacketFactory<AuthRegister>.Acquire();
            regLease.Value.Username = _options.Username;
            regLease.Value.Password = _options.Password;
            regLease.Value.ClientProtocolVersion = CarbonProtocol.Version;

            using AuthResult regResult = await ws.RequestAsync<AuthResult>(
                regLease.Value,
                reqOpts,
                predicate: null,
                ct).ConfigureAwait(false);

            if (regResult.Status != AuthStatus.Ok)
            {
                throw new InvalidOperationException($"Account registration failed: {regResult.Status}.");
            }
        }
        else if (loginResult.Status != AuthStatus.Ok)
        {
            throw new InvalidOperationException($"Authentication failed: {loginResult.Status}.");
        }

        Guid selectedCharacterId = await this.EnsureCharacterAsync(ws, reqOpts, ct).ConfigureAwait(false);

        using PacketScope<CharacterSelect> selectLease = PacketFactory<CharacterSelect>.Acquire();
        selectLease.Value.CharacterId = selectedCharacterId;

        using CharacterEnterWorld enter = await ws.RequestAsync<CharacterEnterWorld>(
            selectLease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        if (enter.Status != OpStatus.Ok)
        {
            throw new InvalidOperationException($"CharacterEnterWorld rejected: {enter.Status}.");
        }

        _worldSeed = enter.WorldSeed;
        _tileSize = enter.TileSize > 0f ? enter.TileSize : 32f;
        _localNetworkId = enter.PlayerNetworkId;

        ProceduralWorldGenerator generator = new(_worldSeed);
        _chunkWorld = new ChunkWorld2D(generator, _tileSize, enter.Frequency);
        Vector2 initialPos = new(enter.PositionX, enter.PositionY);
        _chunkWorld.UpdateAroundPosition(initialPos, chunkRadius: 4, evictionRadius: 8);

        _simulation = new WorldSimulation(enter.TickRate > 0 ? enter.TickRate : 30, _tileSize)
        {
            WorldPhysics = _chunkWorld,
        };

        _localPlayer = _simulation.SpawnNetworkEntity(_localNetworkId, initialPos, radius: 7f, ownerSessionId: 1);
        _simulation.Entities.SetVelocity(_localPlayer, new Velocity2D(0f, 0f, 5f));

        this.RequestMissingChunkDeltasAround(initialPos);
    }

    /// <inheritdoc/>
    public void Update(double dt, Vector2 moveDir, bool sprint)
    {
        if (!this.IsReady)
        {
            return;
        }

        lock (_worldGate)
        {
            if (_hasServerReconcile && _simulation!.Entities.IsAlive(_localPlayer))
            {
                ref Transform2D tr = ref _simulation.Entities.GetTransform(_localPlayer);
                float driftSq = Vector2.DistanceSquared(tr.Position, _pendingServerReconcile);
                if (driftSq > (_tileSize * _tileSize))
                {
                    _simulation.EnqueueCommand(SimulationCommand.Teleport(_localPlayer, _pendingServerReconcile));
                }
                else if (driftSq > 4f)
                {
                    tr.Position = Vector2.Lerp(tr.Position, _pendingServerReconcile, 0.2f);
                }

                _hasServerReconcile = false;
            }

            if (moveDir.LengthSquared() > 0.001f)
            {
                float speed = sprint ? SprintSpeed : WalkSpeed;
                _simulation!.EnqueueCommand(SimulationCommand.MoveInput(_localPlayer, moveDir, speed, _clientTick));
            }

            int ticks = _simulation!.Update(dt);
            if (ticks > 0)
            {
                _clientTick += (uint)ticks;
                if (_session is { IsConnected: true })
                {
                    this.SendMoveInputAsync(moveDir, sprint);
                }
            }

            Vector2 pos = _simulation.Entities.GetTransform(_localPlayer).Position;
            _chunkWorld!.UpdateAroundPosition(pos, chunkRadius: 4, evictionRadius: 7);
            this.RequestMissingChunkDeltasAround(pos);
        }
    }

    /// <inheritdoc/>
    public bool TryHarvestNearbyProp(out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady || _session is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize);
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.HasSolidPropAt(wx, wy))
                {
                    _ = this.SendPooledAsync<MapPropHarvest>(p =>
                    {
                        p.TileX = wx;
                        p.TileY = wy;
                    });
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
        if (!this.IsReady || _session is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize) + 2;
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        _ = this.SendPooledAsync<MapStructurePlace>(p =>
        {
            p.TileX = ptx;
            p.TileY = pty;
            p.StructureType = (byte)StructureType.WoodWall;
        });

        centerChunk = (Chunk2D.ToChunkCoord(ptx), Chunk2D.ToChunkCoord(pty));
        return true;
    }

    /// <inheritdoc/>
    public bool TryToggleNearbyDoor(out (int ChunkX, int ChunkY) modifiedChunk)
    {
        modifiedChunk = default;
        if (!this.IsReady || _session is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize);
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int wx = ptx + dx;
                int wy = pty + dy;
                if (_chunkWorld!.Buildings.TryGetStructure(wx, wy, out StructureTile st) && st.Category == StructureCategory.Door)
                {
                    _ = this.SendPooledAsync<MapDoorToggle>(p =>
                    {
                        p.TileX = wx;
                        p.TileY = wy;
                    });
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
        // Authoritative world seed is controlled by the server in online mode.
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        for (int i = 0; i < _subscriptions.Count; i++)
        {
            _subscriptions[i].Dispose();
        }

        _subscriptions.Clear();

        if (_session is not null)
        {
            try
            {
                if (_session.IsConnected)
                {
                    await _session.DisconnectGracefullyAsync(ProtocolReason.NONE).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best-effort graceful disconnect on teardown.
            }

            _session.Dispose();
            _session = null;
        }
    }

    private async Task<Guid> EnsureCharacterAsync(WebSocketSession ws, RequestOptions reqOpts, CancellationToken ct)
    {
        Guid chosen = Guid.Empty;
        using (PacketScope<CharacterListRequest> listLease = PacketFactory<CharacterListRequest>.Acquire())
        {
            await foreach (CharacterSummary item in ws.StreamAsync<CharacterSummary>(
                listLease.Value,
                reqOpts,
                ct).ConfigureAwait(false))
            {
                if (item.IsTerminator || item.CharacterId == Guid.Empty)
                {
                    continue;
                }

                if (chosen == Guid.Empty)
                {
                    chosen = item.CharacterId;
                }
            }
        }

        if (chosen != Guid.Empty)
        {
            return chosen;
        }

        using (PacketScope<CharacterCreate> createLease = PacketFactory<CharacterCreate>.Acquire())
        {
            createLease.Value.CharacterName = _options.CharacterName;
            createLease.Value.AppearancePreset = 0;

            using MutationResult createResult = await ws.RequestAsync<MutationResult>(
                createLease.Value,
                reqOpts,
                predicate: null,
                ct).ConfigureAwait(false);

            if (createResult.Status != OpStatus.Ok)
            {
                throw new InvalidOperationException($"Failed to create starter character: {createResult.Status}.");
            }
        }

        using PacketScope<CharacterListRequest> retryLease = PacketFactory<CharacterListRequest>.Acquire();
        await foreach (CharacterSummary item in ws.StreamAsync<CharacterSummary>(
            retryLease.Value,
            reqOpts,
            ct).ConfigureAwait(false))
        {
            if (!item.IsTerminator && item.CharacterId != Guid.Empty)
            {
                return item.CharacterId;
            }
        }

        throw new InvalidOperationException("Character list remained empty after CharacterCreate succeeded.");
    }

    private void SendMoveInputAsync(Vector2 moveDir, bool sprint)
    {
        uint tick = _clientTick;
        _ = this.SendPooledAsync<EntityMoveInput>(p =>
        {
            p.ClientTick = tick;
            p.DirectionX = moveDir.X;
            p.DirectionY = moveDir.Y;
            p.Sprint = sprint;
        });
    }

    private async Task SendPooledAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        WebSocketSession? ws = _session;
        if (ws is null || !ws.IsConnected)
        {
            return;
        }

        try
        {
            using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
            populate(lease.Value);
            await ws.SendAsync(lease.Value, ws.State.EncryptionEnabled, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Transient send errors are handled by disconnect/reconnect events.
        }
    }

    private void RequestMissingChunkDeltasAround(Vector2 pos)
    {
        if (_session is not { IsConnected: true } || _chunkWorld is null)
        {
            return;
        }

        int cx0 = Chunk2D.ToChunkCoord((int)MathF.Floor(pos.X / _tileSize));
        int cy0 = Chunk2D.ToChunkCoord((int)MathF.Floor(pos.Y / _tileSize));
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int cx = cx0 + dx;
                int cy = cy0 + dy;
                long key = Chunk2D.PackKey(cx, cy);
                if (_requestedChunks.TryAdd(key, 0U))
                {
                    _ = this.SyncChunkDeltaAsync(cx, cy);
                }
            }
        }
    }

    private async Task SyncChunkDeltaAsync(int cx, int cy)
    {
        WebSocketSession? ws = _session;
        if (ws is null || !ws.IsConnected)
        {
            return;
        }

        try
        {
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(ws.State.EncryptionEnabled);
            using PacketScope<MapChunkRequest> reqLease = PacketFactory<MapChunkRequest>.Acquire();
            reqLease.Value.ChunkX = cx;
            reqLease.Value.ChunkY = cy;
            reqLease.Value.KnownRevision = 0U;

            await foreach (MapChunkDelta packet in ws.StreamAsync<MapChunkDelta>(
                reqLease.Value,
                reqOpts,
                CancellationToken.None).ConfigureAwait(false))
            {
                if (packet.IsTerminator)
                {
                    continue;
                }

                ChunkDelta domainDelta = ChunkDeltaPacketMapper.FromPacket(packet);
                lock (_worldGate)
                {
                    _chunkWorld?.ImportDelta(domainDelta);
                    _requestedChunks[Chunk2D.PackKey(cx, cy)] = packet.Revision;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _ = _requestedChunks.TryRemove(Chunk2D.PackKey(cx, cy), out _);
        }
    }

    private void OnEntitySpawn(EntitySpawn pkt)
    {
        if (pkt.NetworkId == _localNetworkId)
        {
            return;
        }

        _remoteEntities[pkt.NetworkId] = new RemoteEntityState
        {
            NetworkId = pkt.NetworkId,
            Kind = pkt.Kind,
            Name = pkt.Name,
            Position = new Vector2(pkt.PositionX, pkt.PositionY),
            Velocity = new Vector2(pkt.VelocityX, pkt.VelocityY),
            Radius = pkt.Radius > 0f ? pkt.Radius : 7f,
            CurrentHealth = pkt.CurrentHealth,
            MaxHealth = pkt.MaxHealth,
        };
    }

    private void OnEntityDespawn(EntityDespawn pkt) =>
        _ = _remoteEntities.TryRemove(pkt.NetworkId, out _);

    private void OnEntityDeltaBatch(EntityDeltaBatch pkt)
    {
        lock (_worldGate)
        {
            _pendingServerReconcile = new Vector2(pkt.SelfPositionX, pkt.SelfPositionY);
            _hasServerReconcile = true;
        }

        int count = pkt.NetworkIds.Length;
        for (int i = 0; i < count; i++)
        {
            ulong netId = pkt.NetworkIds[i];
            if (netId == _localNetworkId)
            {
                continue;
            }

            if (_remoteEntities.TryGetValue(netId, out RemoteEntityState? state))
            {
                if (i < pkt.PositionsX.Length && i < pkt.PositionsY.Length)
                {
                    state.Position = new Vector2(pkt.PositionsX[i], pkt.PositionsY[i]);
                }

                if (i < pkt.VelocitiesX.Length && i < pkt.VelocitiesY.Length)
                {
                    state.Velocity = new Vector2(pkt.VelocitiesX[i], pkt.VelocitiesY[i]);
                }

                if (i < pkt.CurrentHealths.Length)
                {
                    state.CurrentHealth = pkt.CurrentHealths[i];
                }
            }
        }
    }

    private void OnMapPropChanged(MapPropChanged pkt)
    {
        lock (_worldGate)
        {
            _ = _chunkWorld?.TryDamagePropAt(pkt.TileX, pkt.TileY, pkt.IsDestroyed ? 10_000f : 0f, byte.MaxValue, out _);
        }
    }

    private void OnMapStructureChanged(MapStructureChanged pkt)
    {
        lock (_worldGate)
        {
            if (_chunkWorld is null)
            {
                return;
            }

            if (pkt.StructureType == 0)
            {
                _ = _chunkWorld.Buildings.TryDemolishStructure(pkt.TileX, pkt.TileY);
            }
            else
            {
                _chunkWorld.Buildings.ForcePlaceStructure(pkt.TileX, pkt.TileY, (StructureType)pkt.StructureType);
            }
        }
    }

    private sealed class RemoteEntityState
    {
        public ulong NetworkId { get; init; }
        public EntityKind Kind { get; init; }
        public string Name { get; init; } = string.Empty;
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float Radius { get; init; }
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; init; }
    }
}
