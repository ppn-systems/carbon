// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Streaming;
using Carbon.Prediction.Client;
using Carbon.Prediction.Remote;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Nalix.SDK.Transport;

namespace Carbon.Desktop.Networking;

/// <summary>
/// Online-only dual-transport multiplayer client session built on <c>Nalix.SDK</c> (<c>14.4.0</c>).
/// Runs reliable transactions over <see cref="TcpSession"/> (X25519 ECDH + AEAD) and streams
/// low-latency movement/combat state over Authenticated UDP (<see cref="DatagramSession"/> / NDP),
/// with automatic TCP fallback, prediction reconciliation, and snapshot interpolation.
/// </summary>
public sealed partial class NalixMultiplayerSession : IClientWorldSession
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
    private readonly ConcurrentDictionary<Guid, byte> _characterPresets = new();
    private readonly List<RemoteEntityView> _remoteViewSnapshot = new(64);
    private readonly List<ClientCombatEvent> _combatEvents = new(32);
    private readonly List<ClientPropImpactEvent> _propImpactEvents = new(16);
    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunks = new(16);
    private readonly List<string> _hudNotifications = new(16);
    private readonly InventoryContainer _inventory = new(PlayerInventoryLayout.TotalCapacity);
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
    private ushort _localLevel = 1;
    private uint _localExperience;
    private uint _localXpToNext = 100U;
    private float _localMana = 80f;
    private float _localMaxMana = 80f;
    private float _localStamina = 100f;
    private float _localMaxStamina = 100f;
    private float _localDefense = 2f;
    private float _skill1Cd;
    private float _skill2Cd;
    private float _skill3Cd;
    private ushort _itemSeqId;
    private bool _wasMovingLastTick;
    private float _idleKeepAliveTimer;
    private byte _localAppearancePreset;
    private bool _inventoryLoaded;
    private Vector2 _localFacingDir = new(0f, 1f);
    private float _localHitFlashTimer;
    private ClientActionState _currentAction = ClientActionState.None;
    private byte _comboStep;

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
    public ushort LocalPlayerLevel => _localLevel;

    /// <inheritdoc/>
    public uint LocalPlayerExperience => _localExperience;

    /// <inheritdoc/>
    public uint LocalPlayerXpToNext => _localXpToNext;

    /// <inheritdoc/>
    public float LocalPlayerMana => _localMana;

    /// <inheritdoc/>
    public float LocalPlayerMaxMana => _localMaxMana;

    /// <inheritdoc/>
    public float LocalPlayerStamina => _localStamina;

    /// <inheritdoc/>
    public float LocalPlayerMaxStamina => _localMaxStamina;

    /// <inheritdoc/>
    public float LocalPlayerDefense => _localDefense;

    /// <inheritdoc/>
    public float GetSkillCooldown(byte skillSlot) => skillSlot switch
    {
        1 => _skill1Cd,
        2 => _skill2Cd,
        3 => _skill3Cd,
        _ => 0f,
    };

    /// <inheritdoc/>
    public byte SelectedHotbarSlot => _selectedSlot;

    /// <inheritdoc/>
    public InventoryContainer Inventory => _inventory;

    /// <inheritdoc/>
    public Vector2 LastAttackDirection => _lastAttackDir;

    /// <inheritdoc/>
    public float AttackSwingTimer => _attackSwingTimer;

    /// <inheritdoc/>
    public Vector2 LocalFacingDirection => _localFacingDir;

    /// <inheritdoc/>
    public float LocalHitFlashTimer => _localHitFlashTimer;

    /// <inheritdoc/>
    public ClientActionState CurrentAction => _currentAction;

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
                    s.MaxHealth,
                    s.ArchetypeId,
                    s.FacingDir,
                    s.AttackAnimTimer,
                    s.AttackAnimDuration,
                    s.HitFlashTimer,
                    s.EquippedHeadId,
                    s.EquippedChestId,
                    s.EquippedLegsId,
                    s.EquippedFeetId,
                    s.EquippedWeaponId,
                    s.Level,
                    s.LootCount,
                    s.WeaponUpgradeLevel));
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
    public byte LocalPlayerAppearancePreset => _localAppearancePreset;

    /// <inheritdoc/>
    public string TransportLabel =>
        !this.IsConnected
            ? "OFFLINE"
            : (this.IsUdpActive ? "TCP+UDP" : "TCP");

    /// <inheritdoc/>
    public ulong WorldSeed => _worldSeed;

    /// <inheritdoc/>
    public void Update(double dt, Vector2 moveDir, bool sprint)
    {
        if (!this.IsReady)
        {
            return;
        }

        float fdt = (float)dt;
        if (_attackSwingTimer > 0f)
        {
            _attackSwingTimer = MathF.Max(0f, _attackSwingTimer - fdt);
        }

        if (_skill1Cd > 0f)
        {
            _skill1Cd = MathF.Max(0f, _skill1Cd - fdt);
        }

        if (_skill2Cd > 0f)
        {
            _skill2Cd = MathF.Max(0f, _skill2Cd - fdt);
        }

        if (_skill3Cd > 0f)
        {
            _skill3Cd = MathF.Max(0f, _skill3Cd - fdt);
        }

        if (_localHitFlashTimer > 0f)
        {
            _localHitFlashTimer = MathF.Max(0f, _localHitFlashTimer - fdt);
        }

        if (_currentAction.IsActive)
        {
            float rem = _currentAction.RemainingSeconds - fdt;
            _currentAction = rem > 0f
                ? _currentAction with { RemainingSeconds = rem }
                : ClientActionState.None;
        }

        foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
        {
            RemoteEntityState rs = kvp.Value;
            if (rs.AttackAnimTimer > 0f)
            {
                rs.AttackAnimTimer = MathF.Max(0f, rs.AttackAnimTimer - fdt);
            }

            if (rs.HitFlashTimer > 0f)
            {
                rs.HitFlashTimer = MathF.Max(0f, rs.HitFlashTimer - fdt);
            }
        }

        _idleKeepAliveTimer += fdt;

        lock (_worldGate)
        {
            _visualOffset = VisualErrorSmoother.Decay(_visualOffset, fdt);

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

            bool isMoving = moveDir.LengthSquared() > 0.001f;
            bool canSprint = sprint && isMoving && _localStamina > 2f;
            float speed = canSprint ? SprintSpeed : WalkSpeed;
            if (canSprint)
            {
                _localStamina = MathF.Max(0f, _localStamina - (18f * fdt));
            }
            else
            {
                _localStamina = MathF.Min(_localMaxStamina, _localStamina + ((isMoving ? 8f : 16f) * fdt));
            }

            _localMana = MathF.Min(_localMaxMana, _localMana + (3.5f * fdt));

            if (isMoving)
            {
                if (!_currentAction.IsActive)
                {
                    _localFacingDir = Vector2.Normalize(moveDir);
                }

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
                    this.SendMoveInputAsync(moveDir, canSprint);
                }
            }

            Vector2 pos = _simulation.Entities.GetTransform(_localPlayer).Position;
            _chunkWorld!.UpdateAroundPosition(pos, chunkRadius: 4, evictionRadius: 7);
            this.RequestMissingChunkDeltasAround(pos);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.TearDownTransportsAsync().ConfigureAwait(false);
        this.ClearWorldState();
        _connectGate.Dispose();
    }

    private void ClearWorldState()
    {
        lock (_worldGate)
        {
            _remoteEntities.Clear();
            _requestedChunks.Clear();
            _combatEvents.Clear();
            _propImpactEvents.Clear();
            _dirtyChunks.Clear();
            _hudNotifications.Clear();
            _inventory.Clear();
            _inventoryLoaded = false;
            _reconciler.Clear();
            _chunkWorld = null;
            _simulation = null;
            _localPlayer = Entity.Null;
            _localNetworkId = 0UL;
            _worldSeed = 0UL;
            _hasServerReconcile = false;
            _visualOffset = Vector2.Zero;
            _localHitFlashTimer = 0f;
            _currentAction = ClientActionState.None;
            _localLevel = 1;
            _localExperience = 0U;
            _localXpToNext = 100U;
            _localMana = 80f;
            _localMaxMana = 80f;
            _localStamina = 100f;
            _localMaxStamina = 100f;
            _localDefense = 2f;
            _skill1Cd = 0f;
            _skill2Cd = 0f;
            _skill3Cd = 0f;
        }
    }
}
