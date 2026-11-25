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
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.C2S;
using Carbon.Contracts.Maps.S2C;
using Carbon.Desktop.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Streaming;
using Carbon.Prediction.Client;
using Carbon.Prediction.Remote;
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
/// Live WebSocket multiplayer session built on <c>Nalix.SDK</c> (<c>14.4.0</c>), featuring client prediction
/// rewind/replay via <see cref="ClientPredictionReconciler"/>, <see cref="VisualErrorSmoother"/> smoothing,
/// <see cref="SnapshotInterpolationBuffer"/> remote entity smoothing, inventory sync, and live combat push handling.
/// </summary>
public sealed class NalixMultiplayerSession : IClientWorldSession
{
    private const float WalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const double InterpolationDelaySeconds = 0.065;

    private readonly DesktopClientOptions _options;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly ConcurrentDictionary<ulong, RemoteEntityState> _remoteEntities = new();
    private readonly ConcurrentDictionary<long, uint> _requestedChunks = new();
    private readonly List<RemoteEntityView> _remoteViewSnapshot = new(64);
    private readonly List<ClientCombatEvent> _combatEvents = new(32);
    private readonly InventoryContainer _inventory = new(27);
    private readonly ClientPredictionReconciler _reconciler = new(128);
    private readonly Lock _worldGate = new();

    private WebSocketSession? _session;
    private ChunkWorld2D? _chunkWorld;
    private WorldSimulation? _simulation;
    private Entity _localPlayer = Entity.Null;
    private ulong _localNetworkId;
    private ulong _worldSeed;
    private float _tileSize = 32f;
    private ushort _tickRate = 30;
    private uint _clientTick;
    private double _latestServerTime;
    private Vector2 _pendingServerReconcile;
    private Vector2 _pendingServerVelocity;
    private uint _pendingServerAckTick;
    private bool _hasServerReconcile;
    private Vector2 _visualOffset;
    private byte _selectedSlot;
    private Vector2 _lastAttackDir = Vector2.UnitX;
    private float _attackSwingTimer;
    private float _localHealth = 100f;
    private float _localMaxHealth = 100f;

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
            ? _simulation.GetInterpolatedPosition(_localPlayer) + _visualOffset
            : Vector2.Zero;

    /// <inheritdoc/>
    public float LocalPlayerHealth => _localHealth;

    /// <inheritdoc/>
    public float LocalPlayerMaxHealth => _localMaxHealth;

    /// <inheritdoc/>
    public byte SelectedHotbarSlot => _selectedSlot;

    /// <inheritdoc/>
    public InventoryContainer Inventory => _inventory;

    /// <inheritdoc/>
    public Vector2 LastAttackDirection => _lastAttackDir;

    /// <inheritdoc/>
    public float AttackSwingTimer => _attackSwingTimer;

    /// <inheritdoc/>
    public IReadOnlyList<RemoteEntityView> RemoteEntities
    {
        get
        {
            _remoteViewSnapshot.Clear();
            double renderTime = Math.Max(0.0, _latestServerTime - InterpolationDelaySeconds);
            foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
            {
                RemoteEntityState s = kvp.Value;
                TransformSnapshot sampled = s.Interpolation.Count > 0
                    ? s.Interpolation.Sample(renderTime, maxExtrapolationSeconds: 0.1f)
                    : new TransformSnapshot(0.0, s.Position, s.Velocity);
                Vector2 viewPos = s.Interpolation.Count > 0 ? sampled.Position : s.Position;
                Vector2 viewVel = s.Interpolation.Count > 0 ? sampled.Velocity : s.Velocity;

                _remoteViewSnapshot.Add(new RemoteEntityView(
                    s.NetworkId,
                    s.Kind,
                    s.Name,
                    viewPos,
                    viewVel,
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

        // Subscribe to server push streams before connecting so early frames are never lost.
        _subscriptions.Add(ws.On<EntitySpawn>(this.OnEntitySpawn));
        _subscriptions.Add(ws.On<EntityDespawn>(this.OnEntityDespawn));
        _subscriptions.Add(ws.On<EntityDeltaBatch>(this.OnEntityDeltaBatch));
        _subscriptions.Add(ws.On<CombatHit>(this.OnCombatHit));
        _subscriptions.Add(ws.On<InventorySlotChanged>(this.OnInventorySlotChanged));
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
        _tickRate = enter.TickRate > 0 ? enter.TickRate : (ushort)30;
        _localNetworkId = enter.PlayerNetworkId;

        ProceduralWorldGenerator generator = new(_worldSeed);
        _chunkWorld = new ChunkWorld2D(generator, _tileSize, enter.Frequency);
        Vector2 initialPos = new(enter.PositionX, enter.PositionY);
        _chunkWorld.UpdateAroundPosition(initialPos, chunkRadius: 4, evictionRadius: 8);

        _simulation = new WorldSimulation(_tickRate, _tileSize)
        {
            WorldPhysics = _chunkWorld,
        };

        _localPlayer = _simulation.SpawnNetworkEntity(_localNetworkId, initialPos, radius: 7f, ownerSessionId: 1);
        _simulation.Entities.SetVelocity(_localPlayer, new Velocity2D(0f, 0f, 5f));

        await this.FetchInventorySnapshotAsync(ws, reqOpts, ct).ConfigureAwait(false);
        this.RequestMissingChunkDeltasAround(initialPos);
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

        lock (_worldGate)
        {
            _visualOffset = VisualErrorSmoother.Decay(_visualOffset, (float)dt);

            if (_hasServerReconcile && _simulation!.Entities.IsAlive(_localPlayer))
            {
                ReconciliationResult result = _reconciler.Reconcile(
                    _pendingServerAckTick,
                    _pendingServerReconcile,
                    _pendingServerVelocity,
                    _simulation,
                    _localPlayer,
                    errorThreshold: 2.0f);

                if (result.WasCorrected)
                {
                    _simulation.Entities.GetTransform(_localPlayer).Position = result.CorrectedPosition;
                    _visualOffset += result.VisualErrorOffset;
                }
                else
                {
                    ref Transform2D tr = ref _simulation.Entities.GetTransform(_localPlayer);
                    float driftSq = Vector2.DistanceSquared(tr.Position, _pendingServerReconcile);
                    if (driftSq > (_tileSize * _tileSize * 4f))
                    {
                        _simulation.EnqueueCommand(SimulationCommand.Teleport(_localPlayer, _pendingServerReconcile));
                    }
                }

                _hasServerReconcile = false;
            }

            float speed = sprint ? SprintSpeed : WalkSpeed;
            if (moveDir.LengthSquared() > 0.001f)
            {
                _simulation!.EnqueueCommand(SimulationCommand.MoveInput(_localPlayer, moveDir, speed, _clientTick));
            }

            int ticks = _simulation!.Update(dt);
            if (ticks > 0)
            {
                Vector2 postPos = _simulation.Entities.GetTransform(_localPlayer).Position;
                Vector2 postVel = _simulation.Entities.HasVelocity(_localPlayer)
                    ? _simulation.Entities.GetVelocity(_localPlayer).Linear
                    : Vector2.Zero;

                for (int t = 0; t < ticks; t++)
                {
                    _clientTick++;
                    _reconciler.RecordPredictedTick(new PredictedTickRecord(_clientTick, moveDir, speed, postPos, postVel));
                }

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
    public void SelectHotbarSlot(byte slotIndex)
    {
        if (slotIndex >= 9)
        {
            return;
        }

        _selectedSlot = slotIndex;
        if (_session is { IsConnected: true })
        {
            _ = this.SendPooledAsync<SelectHotbarSlot>(p => p.SlotIndex = slotIndex);
        }
    }

    /// <inheritdoc/>
    public void TriggerPrimaryAction(Vector2 aimWorldPosition)
    {
        if (!this.IsReady || _session is not { IsConnected: true })
        {
            return;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        Vector2 delta = aimWorldPosition - pos;
        _lastAttackDir = delta.LengthSquared() > 1e-4f ? Vector2.Normalize(delta) : Vector2.UnitX;
        _attackSwingTimer = 0.22f;

        uint tick = _clientTick;
        byte slot = _selectedSlot;
        _ = this.SendPooledAsync<EntityAttackInput>(p =>
        {
            p.ClientTick = tick;
            p.TargetNetworkId = 0UL;
            p.AimX = aimWorldPosition.X;
            p.AimY = aimWorldPosition.Y;
            p.AbilitySlot = slot;
        });
    }

    /// <inheritdoc/>
    public bool TryCraftRecipe(ushort recipeId)
    {
        if (!this.IsReady || _session is not { IsConnected: true } ||
            !StarterGameCatalog.Recipes.TryGet(recipeId, out CraftingRecipe? recipe) ||
            !RecipeMatcher.CanCraft(_inventory, recipe))
        {
            return false;
        }

        _ = this.SendPooledAsync<CraftItem>(p =>
        {
            p.RecipeId = recipeId;
            p.Quantity = 1;
        });
        return true;
    }

    /// <inheritdoc/>
    public void DrainCombatEvents(List<ClientCombatEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_worldGate)
        {
            for (int i = 0; i < _combatEvents.Count; i++)
            {
                destination.Add(_combatEvents[i]);
            }

            _combatEvents.Clear();
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

    private async Task FetchInventorySnapshotAsync(WebSocketSession ws, RequestOptions reqOpts, CancellationToken ct)
    {
        _inventory.Clear();
        using PacketScope<InventoryRequest> lease = PacketFactory<InventoryRequest>.Acquire();
        await foreach (InventorySlotItem item in ws.StreamAsync<InventorySlotItem>(
            lease.Value,
            reqOpts,
            ct).ConfigureAwait(false))
        {
            if (item.IsTerminator || item.SlotIndex >= _inventory.Capacity)
            {
                continue;
            }

            _inventory.SetSlot(item.SlotIndex, new ItemSlot(item.ItemId, item.Count, 0UL, item.Durability));
        }
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
            _localHealth = pkt.CurrentHealth;
            _localMaxHealth = pkt.MaxHealth > 0f ? pkt.MaxHealth : 100f;
            return;
        }

        RemoteEntityState state = new()
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
        _ = state.Interpolation.PushSnapshot(new TransformSnapshot(
            _latestServerTime,
            state.Position,
            state.Velocity));
        _remoteEntities[pkt.NetworkId] = state;
    }

    private void OnEntityDespawn(EntityDespawn pkt) =>
        _ = _remoteEntities.TryRemove(pkt.NetworkId, out _);

    private void OnEntityDeltaBatch(EntityDeltaBatch pkt)
    {
        double snapshotTime = pkt.ServerTick / (double)_tickRate;
        lock (_worldGate)
        {
            _latestServerTime = Math.Max(_latestServerTime, snapshotTime);
            _pendingServerAckTick = pkt.LastProcessedClientTick;
            _pendingServerReconcile = new Vector2(pkt.SelfPositionX, pkt.SelfPositionY);
            _pendingServerVelocity = Vector2.Zero;
            _hasServerReconcile = true;
        }

        int count = pkt.NetworkIds.Length;
        for (int i = 0; i < count; i++)
        {
            ulong netId = pkt.NetworkIds[i];
            if (netId == _localNetworkId)
            {
                if (i < pkt.CurrentHealths.Length)
                {
                    _localHealth = pkt.CurrentHealths[i];
                }

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

                _ = state.Interpolation.PushSnapshot(new TransformSnapshot(snapshotTime, state.Position, state.Velocity));
            }
        }
    }

    private void OnCombatHit(CombatHit pkt)
    {
        Vector2 worldPos = this.LocalPlayerInterpolatedPosition;
        if (pkt.DefenderNetworkId == _localNetworkId)
        {
            _localHealth = pkt.RemainingHealth;
        }
        else if (_remoteEntities.TryGetValue(pkt.DefenderNetworkId, out RemoteEntityState? state))
        {
            state.CurrentHealth = pkt.RemainingHealth;
            worldPos = state.Position;
        }

        lock (_worldGate)
        {
            bool isHeal = pkt.Damage < 0f;
            float mag = MathF.Abs(pkt.Damage);
            _combatEvents.Add(new ClientCombatEvent(worldPos, mag, pkt.IsCritical, isHeal));
        }
    }

    private void OnInventorySlotChanged(InventorySlotChanged pkt)
    {
        if (pkt.SlotIndex >= _inventory.Capacity)
        {
            return;
        }

        ItemSlot next = pkt.ItemId == 0 || pkt.Count <= 0
            ? ItemSlot.Empty
            : new ItemSlot(pkt.ItemId, pkt.Count, 0UL, pkt.Durability);
        _inventory.SetSlot(pkt.SlotIndex, in next);
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
        public SnapshotInterpolationBuffer Interpolation { get; } = new(16);
    }
}
