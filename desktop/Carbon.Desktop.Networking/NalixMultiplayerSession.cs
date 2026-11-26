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
using Carbon.Items.Definitions;
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
using Nalix.Abstractions.Networking.Datagrams;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Networking.Protocols;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking;

/// <summary>
/// Online-only dual-transport multiplayer client session built on <c>Nalix.SDK</c> (<c>14.4.0</c>).
/// Runs reliable transactions over <see cref="TcpSession"/> (X25519 ECDH + AEAD) and streams
/// low-latency movement/combat state over Authenticated UDP (<see cref="DatagramSession"/> / NDP),
/// with automatic TCP fallback, prediction reconciliation, and snapshot interpolation.
/// </summary>
public sealed class NalixMultiplayerSession : IClientWorldSession
{
    private const float WalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const double InterpolationDelaySeconds = 0.065;
    private const float IdleKeepAliveSeconds = 2.5f;

    private readonly DesktopClientOptions _options;
    private readonly List<IDisposable> _tcpSubscriptions = [];
    private readonly List<IDisposable> _udpSubscriptions = [];
    private readonly ConcurrentDictionary<ulong, RemoteEntityState> _remoteEntities = new();
    private readonly ConcurrentDictionary<long, uint> _requestedChunks = new();
    private readonly ConcurrentDictionary<Guid, string> _characterNames = new();
    private readonly List<RemoteEntityView> _remoteViewSnapshot = new(64);
    private readonly List<ClientCombatEvent> _combatEvents = new(32);
    private readonly InventoryContainer _inventory = new(27);
    private readonly ClientPredictionReconciler _reconciler = new(128);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly Lock _worldGate = new();

    private TcpSession? _tcpSession;
    private DatagramSession? _udpSession;
    private string _connectedHost = string.Empty;
    private ushort _connectedPort;
    private bool _isAuthenticated;
    private string _activeCharacterName = string.Empty;

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
    private bool _wasMovingLastTick;
    private float _idleKeepAliveTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NalixMultiplayerSession"/> class.
    /// </summary>
    /// <param name="options">Client runtime options.</param>
    public NalixMultiplayerSession(DesktopClientOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

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
    public bool IsConnected => _tcpSession is { IsConnected: true };

    /// <inheritdoc/>
    public bool IsAuthenticated => this.IsConnected && _isAuthenticated;

    /// <inheritdoc/>
    public bool IsUdpActive => _udpSession is { IsConnected: true };

    /// <inheritdoc/>
    public bool IsReady => _chunkWorld is not null && _simulation is not null && _simulation.Entities.IsAlive(_localPlayer);

    /// <inheritdoc/>
    public string ActiveCharacterName => _activeCharacterName;

    /// <inheritdoc/>
    public string TransportLabel =>
        !this.IsConnected
            ? "OFFLINE"
            : (this.IsUdpActive ? "TCP+UDP" : "TCP");

    /// <inheritdoc/>
    public ulong WorldSeed => _worldSeed;

    /// <inheritdoc/>
    public async Task<ClientAuthResult> LoginAsync(
        string host,
        ushort tcpPort,
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || tcpPort == 0)
        {
            return new ClientAuthResult(false, "Invalid server host or port.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new ClientAuthResult(false, "Username and password are required.");
        }

        try
        {
            TcpSession tcp = await this.EnsureTcpConnectedAsync(host.Trim(), tcpPort, ct).ConfigureAwait(false);
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

            using PacketScope<AuthLogin> lease = PacketFactory<AuthLogin>.Acquire();
            lease.Value.Username = username.Trim();
            lease.Value.Password = password;
            lease.Value.ClientProtocolVersion = CarbonProtocol.Version;

            using AuthResult result = await tcp.RequestAsync<AuthResult>(
                lease.Value,
                reqOpts,
                predicate: null,
                ct).ConfigureAwait(false);

            if (result.Status == AuthStatus.Ok)
            {
                _isAuthenticated = true;
                return new ClientAuthResult(true, "Signed in.", result.AccountId);
            }

            return new ClientAuthResult(false, FormatAuthStatus(result.Status));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await this.TearDownTransportsAsync().ConfigureAwait(false);
            return new ClientAuthResult(false, $"Connection failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ClientAuthResult> RegisterAsync(
        string host,
        ushort tcpPort,
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || tcpPort == 0)
        {
            return new ClientAuthResult(false, "Invalid server host or port.");
        }

        string trimmedUser = username?.Trim() ?? string.Empty;
        if (trimmedUser.Length < 3 || trimmedUser.Length > 24)
        {
            return new ClientAuthResult(false, "Username must be 3 to 24 characters.");
        }

        if (string.IsNullOrEmpty(password) || password.Length < 6 || password.Length > 128)
        {
            return new ClientAuthResult(false, "Password must be 6 to 128 characters.");
        }

        try
        {
            TcpSession tcp = await this.EnsureTcpConnectedAsync(host.Trim(), tcpPort, ct).ConfigureAwait(false);
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

            using PacketScope<AuthRegister> lease = PacketFactory<AuthRegister>.Acquire();
            lease.Value.Username = trimmedUser;
            lease.Value.Password = password;
            lease.Value.ClientProtocolVersion = CarbonProtocol.Version;

            using AuthResult result = await tcp.RequestAsync<AuthResult>(
                lease.Value,
                reqOpts,
                predicate: null,
                ct).ConfigureAwait(false);

            if (result.Status == AuthStatus.Ok)
            {
                _isAuthenticated = true;
                return new ClientAuthResult(true, "Account registered.", result.AccountId);
            }

            return new ClientAuthResult(false, FormatAuthStatus(result.Status));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await this.TearDownTransportsAsync().ConfigureAwait(false);
            return new ClientAuthResult(false, $"Connection failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClientCharacterSummary>> ListCharactersAsync(CancellationToken ct = default)
    {
        TcpSession tcp = this.RequireAuthenticatedTcpSession();
        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

        List<ClientCharacterSummary> roster = [];
        using PacketScope<CharacterListRequest> lease = PacketFactory<CharacterListRequest>.Acquire();
        await foreach (CharacterSummary item in tcp.StreamAsync<CharacterSummary>(
            lease.Value,
            reqOpts,
            ct).ConfigureAwait(false))
        {
            if (item.IsTerminator || item.CharacterId == Guid.Empty)
            {
                continue;
            }

            _characterNames[item.CharacterId] = item.Name;
            roster.Add(new ClientCharacterSummary(
                item.CharacterId,
                item.Name,
                item.Level,
                item.ZoneTier,
                item.PositionX,
                item.PositionY));
        }

        return roster;
    }

    /// <inheritdoc/>
    public async Task<(bool Succeeded, string Message)> CreateCharacterAsync(
        string characterName,
        byte appearancePreset,
        CancellationToken ct = default)
    {
        TcpSession tcp = this.RequireAuthenticatedTcpSession();
        string trimmed = characterName?.Trim() ?? string.Empty;
        if (trimmed.Length < 2 || trimmed.Length > 20)
        {
            return (false, "Name must be 2 to 20 characters.");
        }

        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);
        using PacketScope<CharacterCreate> lease = PacketFactory<CharacterCreate>.Acquire();
        lease.Value.CharacterName = trimmed;
        lease.Value.AppearancePreset = appearancePreset;

        using MutationResult reply = await tcp.RequestAsync<MutationResult>(
            lease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        if (reply.Status == OpStatus.Ok)
        {
            return (true, $"Created {trimmed}.");
        }

        if (reply.Status == OpStatus.Invalid)
        {
            return (false, "Invalid character name.");
        }

        if (reply.Status == OpStatus.Conflict)
        {
            return (false, "Name taken or character slot limit reached.");
        }

        return (false, $"Create failed: {reply.Status}");
    }

    /// <inheritdoc/>
    public async Task<(bool Succeeded, string Message)> EnterWorldAsync(Guid characterId, CancellationToken ct = default)
    {
        if (characterId == Guid.Empty)
        {
            return (false, "No character selected.");
        }

        TcpSession tcp = this.RequireAuthenticatedTcpSession();
        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

        using PacketScope<CharacterSelect> selectLease = PacketFactory<CharacterSelect>.Acquire();
        selectLease.Value.CharacterId = characterId;

        using CharacterEnterWorld enter = await tcp.RequestAsync<CharacterEnterWorld>(
            selectLease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        if (enter.Status != OpStatus.Ok)
        {
            return (false, $"Enter world failed: {enter.Status}");
        }

        lock (_worldGate)
        {
            _remoteEntities.Clear();
            _requestedChunks.Clear();
            _combatEvents.Clear();
            _reconciler.Clear();
            _clientTick = 0U;
            _latestServerTime = enter.TickRate > 0 ? enter.ServerTick / (double)enter.TickRate : 0.0;
            _hasServerReconcile = false;
            _visualOffset = Vector2.Zero;
            _selectedSlot = 0;
            _localHealth = 100f;
            _localMaxHealth = 100f;
            _wasMovingLastTick = false;
            _idleKeepAliveTimer = 0f;

            _worldSeed = enter.WorldSeed;
            _tileSize = enter.TileSize > 0f ? enter.TileSize : 32f;
            _tickRate = enter.TickRate > 0 ? enter.TickRate : (ushort)30;
            _localNetworkId = enter.PlayerNetworkId;
            _activeCharacterName = _characterNames.TryGetValue(characterId, out string? cachedName)
                ? cachedName
                : _options.CharacterName;

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
        }

        await this.FetchInventorySnapshotAsync(tcp, reqOpts, ct).ConfigureAwait(false);
        this.RequestMissingChunkDeltasAround(new Vector2(enter.PositionX, enter.PositionY));
        await this.TryOpenAuthenticatedUdpAsync(tcp, ct).ConfigureAwait(false);

        return (true, "Entered world.");
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await this.TearDownUdpAsync().ConfigureAwait(false);

        TcpSession? tcp = _tcpSession;
        if (tcp is { IsConnected: true } && _isAuthenticated)
        {
            try
            {
                RequestOptions reqOpts = RequestOptions.Default.WithTimeout(2000).WithEncrypt(tcp.State.EncryptionEnabled);
                using PacketScope<AuthLogout> lease = PacketFactory<AuthLogout>.Acquire();
                using MutationResult _ = await tcp.RequestAsync<MutationResult>(lease.Value, reqOpts, predicate: null, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best-effort server logout before clearing local world state.
            }
        }

        this.ClearWorldState();
        _isAuthenticated = false;
        _activeCharacterName = string.Empty;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ClientAuthResult loginOutcome = await this.LoginAsync(
            _options.ServerHost,
            _options.ServerTcpPort,
            _options.Username,
            _options.Password,
            ct).ConfigureAwait(false);

        if (!loginOutcome.Succeeded)
        {
            ClientAuthResult regOutcome = await this.RegisterAsync(
                _options.ServerHost,
                _options.ServerTcpPort,
                _options.Username,
                _options.Password,
                ct).ConfigureAwait(false);

            if (!regOutcome.Succeeded)
            {
                throw new InvalidOperationException($"Auto authentication failed: {loginOutcome.Message} / {regOutcome.Message}");
            }
        }

        IReadOnlyList<ClientCharacterSummary> roster = await this.ListCharactersAsync(ct).ConfigureAwait(false);
        Guid selectedCharacterId;
        if (roster.Count > 0)
        {
            selectedCharacterId = roster[0].CharacterId;
        }
        else
        {
            (bool created, string createMsg) = await this.CreateCharacterAsync(_options.CharacterName, 0, ct).ConfigureAwait(false);
            if (!created)
            {
                throw new InvalidOperationException($"Failed to create starter character: {createMsg}");
            }

            IReadOnlyList<ClientCharacterSummary> updated = await this.ListCharactersAsync(ct).ConfigureAwait(false);
            if (updated.Count == 0)
            {
                throw new InvalidOperationException("Character list remained empty after creation.");
            }

            selectedCharacterId = updated[0].CharacterId;
        }

        (bool entered, string enterMsg) = await this.EnterWorldAsync(selectedCharacterId, ct).ConfigureAwait(false);
        if (!entered)
        {
            throw new InvalidOperationException(enterMsg);
        }
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

        _idleKeepAliveTimer += (float)dt;

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
            bool isMoving = moveDir.LengthSquared() > 0.001f;
            if (isMoving)
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

                bool shouldTransmit = isMoving || _wasMovingLastTick || _idleKeepAliveTimer >= IdleKeepAliveSeconds;
                if (shouldTransmit && _tcpSession is { IsConnected: true })
                {
                    _wasMovingLastTick = isMoving;
                    _idleKeepAliveTimer = 0f;
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
        if (_tcpSession is { IsConnected: true })
        {
            _ = this.SendTcpPooledAsync<SelectHotbarSlot>(p => p.SlotIndex = slotIndex);
        }
    }

    /// <inheritdoc/>
    public bool TriggerPrimaryAction(Vector2 aimWorldPosition, out (int ChunkX, int ChunkY) invalidatedChunk)
    {
        invalidatedChunk = default;
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        Vector2 delta = aimWorldPosition - pos;
        _lastAttackDir = delta.LengthSquared() > 1e-4f ? Vector2.Normalize(delta) : Vector2.UnitX;

        ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
        bool hasArchetype = StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype);
        if (!hasArchetype || archetype is null || archetype.Category is not (ItemCategory.Placeable or ItemCategory.Consumable))
        {
            _attackSwingTimer = 0.22f;
        }

        uint tick = _clientTick;
        byte slot = _selectedSlot;
        _ = this.SendGameplayDatagramOrTcpAsync<EntityAttackInput>(
            DeliveryMethod.ReliableUnordered,
            channel: 0,
            p =>
            {
                p.ClientTick = tick;
                p.TargetNetworkId = 0UL;
                p.AimX = aimWorldPosition.X;
                p.AimY = aimWorldPosition.Y;
                p.AbilitySlot = slot;
            });

        if (activeSlot.Quantity > 0 && hasArchetype && archetype is { Category: ItemCategory.Placeable or ItemCategory.Tool })
        {
            int aimTx = (int)MathF.Floor(aimWorldPosition.X / _tileSize);
            int aimTy = (int)MathF.Floor(aimWorldPosition.Y / _tileSize);
            invalidatedChunk = (Chunk2D.ToChunkCoord(aimTx), Chunk2D.ToChunkCoord(aimTy));
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryCraftRecipe(ushort recipeId)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } ||
            !StarterGameCatalog.Recipes.TryGet(recipeId, out CraftingRecipe? recipe) ||
            !RecipeMatcher.CanCraft(_inventory, recipe))
        {
            return false;
        }

        _ = this.SendTcpPooledAsync<CraftItem>(p =>
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
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
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
                    _ = this.SendTcpPooledAsync<MapPropHarvest>(p =>
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
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
        {
            return false;
        }

        Vector2 pos = this.LocalPlayerInterpolatedPosition;
        int ptx = (int)MathF.Floor(pos.X / _tileSize) + 2;
        int pty = (int)MathF.Floor(pos.Y / _tileSize);

        _ = this.SendTcpPooledAsync<MapStructurePlace>(p =>
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
        if (!this.IsReady || _tcpSession is not { IsConnected: true })
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
                    _ = this.SendTcpPooledAsync<MapDoorToggle>(p =>
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
    public async ValueTask DisposeAsync()
    {
        await this.TearDownTransportsAsync().ConfigureAwait(false);
        this.ClearWorldState();
        _connectGate.Dispose();
    }

    private async Task<TcpSession> EnsureTcpConnectedAsync(string host, ushort tcpPort, CancellationToken ct)
    {
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tcpSession is { IsConnected: true } existing &&
                existing.State.EncryptionEnabled &&
                string.Equals(_connectedHost, host, StringComparison.OrdinalIgnoreCase) &&
                _connectedPort == tcpPort)
            {
                return existing;
            }

            await this.TearDownTransportsAsync().ConfigureAwait(false);
            this.ClearWorldState();
            _isAuthenticated = false;

            bool hasPinnedKey = !string.IsNullOrWhiteSpace(_options.ServerPublicKeyHex);
            TcpSession tcp = new(new TransportOptions
            {
                Address = host,
                Port = tcpPort,
                ServerPublicKey = _options.ServerPublicKeyHex ?? string.Empty,
                AllowTofu = !hasPinnedKey,
                PersistTofuKey = false,
                EncryptRequestsByDefault = true,
            });

            // Subscribe reliable stream push handlers before connecting so early frames are never lost.
            _tcpSubscriptions.Add(tcp.On<EntitySpawn>(this.OnEntitySpawn));
            _tcpSubscriptions.Add(tcp.On<EntityDespawn>(this.OnEntityDespawn));
            _tcpSubscriptions.Add(tcp.On<EntityDeltaBatch>(this.OnEntityDeltaBatch));
            _tcpSubscriptions.Add(tcp.On<CombatHit>(this.OnCombatHit));
            _tcpSubscriptions.Add(tcp.On<InventorySlotChanged>(this.OnInventorySlotChanged));
            _tcpSubscriptions.Add(tcp.On<MapPropChanged>(this.OnMapPropChanged));
            _tcpSubscriptions.Add(tcp.On<MapStructureChanged>(this.OnMapStructureChanged));

            _tcpSession = tcp;
            await tcp.ConnectAsync(host, tcpPort, ct).ConfigureAwait(false);
            await tcp.HandshakeAsync(ct).ConfigureAwait(false);

            _connectedHost = host;
            _connectedPort = tcpPort;
            return tcp;
        }
        finally
        {
            _ = _connectGate.Release();
        }
    }

    private async Task TryOpenAuthenticatedUdpAsync(TcpSession tcp, CancellationToken ct)
    {
        await this.TearDownUdpAsync().ConfigureAwait(false);
        if (!tcp.IsConnected || !tcp.State.EncryptionEnabled)
        {
            return;
        }

        DatagramSession udp = new(tcp);
        try
        {
            _udpSubscriptions.Add(udp.On<EntityDeltaBatch>(this.OnEntityDeltaBatch));
            _udpSubscriptions.Add(udp.On<CombatHit>(this.OnCombatHit));
            _udpSubscriptions.Add(udp.On<EntitySpawn>(this.OnEntitySpawn));
            _udpSubscriptions.Add(udp.On<EntityDespawn>(this.OnEntityDespawn));

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(3000);
            await udp.ConnectAsync(_connectedHost, null, timeoutCts.Token).ConfigureAwait(false);
            _udpSession = udp;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await this.TearDownUdpAsync().ConfigureAwait(false);
            await udp.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Fallback gracefully to TCP if UDP is blocked on this network.
            await this.TearDownUdpAsync().ConfigureAwait(false);
            await udp.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task TearDownTransportsAsync()
    {
        await this.TearDownUdpAsync().ConfigureAwait(false);

        for (int i = 0; i < _tcpSubscriptions.Count; i++)
        {
            _tcpSubscriptions[i].Dispose();
        }

        _tcpSubscriptions.Clear();

        if (_tcpSession is not null)
        {
            try
            {
                if (_tcpSession.IsConnected)
                {
                    await _tcpSession.DisconnectGracefullyAsync(ProtocolReason.NONE).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best-effort graceful disconnect.
            }

            await _tcpSession.DisposeAsync().ConfigureAwait(false);
            _tcpSession = null;
        }

        _connectedHost = string.Empty;
        _connectedPort = 0;
    }

    private async Task TearDownUdpAsync()
    {
        for (int i = 0; i < _udpSubscriptions.Count; i++)
        {
            _udpSubscriptions[i].Dispose();
        }

        _udpSubscriptions.Clear();

        if (_udpSession is not null)
        {
            try
            {
                if (_udpSession.IsConnected)
                {
                    await _udpSession.DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best-effort datagram teardown.
            }

            await _udpSession.DisposeAsync().ConfigureAwait(false);
            _udpSession = null;
        }
    }

    private void ClearWorldState()
    {
        lock (_worldGate)
        {
            _remoteEntities.Clear();
            _requestedChunks.Clear();
            _combatEvents.Clear();
            _inventory.Clear();
            _reconciler.Clear();
            _chunkWorld = null;
            _simulation = null;
            _localPlayer = Entity.Null;
            _localNetworkId = 0UL;
            _worldSeed = 0UL;
            _hasServerReconcile = false;
            _visualOffset = Vector2.Zero;
        }
    }

    private TcpSession RequireAuthenticatedTcpSession()
    {
        if (_tcpSession is not { IsConnected: true } || !_isAuthenticated)
        {
            throw new InvalidOperationException("Session must connect and authenticate before issuing world requests.");
        }

        return _tcpSession;
    }

    private async Task FetchInventorySnapshotAsync(TcpSession tcp, RequestOptions reqOpts, CancellationToken ct)
    {
        _inventory.Clear();
        using PacketScope<InventoryRequest> lease = PacketFactory<InventoryRequest>.Acquire();
        await foreach (InventorySlotItem item in tcp.StreamAsync<InventorySlotItem>(
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
        _ = this.SendGameplayDatagramOrTcpAsync<EntityMoveInput>(
            DeliveryMethod.UnreliableSequenced,
            channel: 1,
            p =>
            {
                p.ClientTick = tick;
                p.DirectionX = moveDir.X;
                p.DirectionY = moveDir.Y;
                p.Sprint = sprint;
            });
    }

    private async Task SendGameplayDatagramOrTcpAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(
        DeliveryMethod deliveryMethod,
        byte channel,
        Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        DatagramSession? udp = _udpSession;
        if (udp is { IsConnected: true })
        {
            try
            {
                using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
                populate(lease.Value);
                await udp.SendAsync(lease.Value, deliveryMethod, channel, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Fall back to reliable TCP stream if datagram send rejects.
            }
        }

        await this.SendTcpPooledAsync(populate).ConfigureAwait(false);
    }

    private async Task SendTcpPooledAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        TcpSession? tcp = _tcpSession;
        if (tcp is null || !tcp.IsConnected)
        {
            return;
        }

        try
        {
            using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
            populate(lease.Value);
            await tcp.SendAsync(lease.Value, tcp.State.EncryptionEnabled, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Transient send errors are handled by disconnect events.
        }
    }

    private void RequestMissingChunkDeltasAround(Vector2 pos)
    {
        if (_tcpSession is not { IsConnected: true } || _chunkWorld is null)
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
        TcpSession? tcp = _tcpSession;
        if (tcp is null || !tcp.IsConnected)
        {
            return;
        }

        try
        {
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);
            using PacketScope<MapChunkRequest> reqLease = PacketFactory<MapChunkRequest>.Acquire();
            reqLease.Value.ChunkX = cx;
            reqLease.Value.ChunkY = cy;
            reqLease.Value.KnownRevision = 0U;

            await foreach (MapChunkDelta packet in tcp.StreamAsync<MapChunkDelta>(
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

    private static string FormatAuthStatus(AuthStatus status)
    {
        if (status == AuthStatus.InvalidCredentials)
        {
            return "Invalid username or password.";
        }

        if (status == AuthStatus.AccountExists)
        {
            return "Account username already exists.";
        }

        if (status == AuthStatus.Locked)
        {
            return "Account is banned or locked.";
        }

        if (status == AuthStatus.InvalidInput)
        {
            return "Invalid username or password format.";
        }

        if (status == AuthStatus.ProtocolMismatch)
        {
            return "Client/server protocol version mismatch.";
        }

        return $"Authentication error ({status}).";
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
