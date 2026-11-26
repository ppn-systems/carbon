// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using Carbon.Maps.Streaming;
using Carbon.Prediction.Reconciliation;
using Carbon.Prediction.Remote;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Nalix.SDK.Transport;

namespace Carbon.Desktop.Networking;

/// <summary>
/// Online-only dual-transport multiplayer client session.
/// Runs reliable transactions over <see cref="TcpSession"/> (X25519 ECDH + AEAD) and streams
/// low-latency movement/combat state over Authenticated UDP (<see cref="DatagramSession"/>),
/// with automatic TCP fallback, background UDP auto-reconnect, prediction reconciliation, and snapshot interpolation.
/// </summary>
public sealed partial class MultiplayerSession : IWorldSession
{
    private const float WalkSpeed = 250f;
    private const float SprintSpeed = 450f;
    private const double InterpolationDelaySeconds = 0.065;
    private const float IdleKeepAliveSeconds = 2.5f;

    private readonly DesktopOptions _options;
    private readonly List<IDisposable> _tcpSubscriptions = [];
    private readonly List<IDisposable> _udpSubscriptions = [];
    private readonly ConcurrentDictionary<ulong, RemoteEntityState> _remoteEntities = new();
    private readonly ConcurrentDictionary<long, uint> _requestedChunks = new();
    private readonly ConcurrentDictionary<Guid, string> _characterNames = new();
    private readonly ConcurrentDictionary<Guid, byte> _characterPresets = new();
    private readonly List<RemoteEntityView> _remoteViewSnapshot = new(64);
    private readonly List<CombatFeedbackEvent> _combatEvents = new(32);
    private readonly List<PropImpactEvent> _propImpactEvents = new(16);
    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunks = new(16);
    private readonly List<string> _hudNotifications = new(16);
    private readonly CharacterProgressionState _progression = new();
    private readonly Dictionary<ushort, float> _skillCooldowns = new(8);
    private readonly List<ushort> _cooldownKeyScratch = new(8);
    private readonly InventoryContainer _inventory = new(PlayerInventoryLayout.TotalCapacity);
    private readonly PredictionReconciler _reconciler = new(128);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _udpGate = new(1, 1);
    private readonly Lock _worldGate = new();
    private RemoteEntityView[] _cachedRemoteViews = [];
    private bool _remoteViewsDirty = true;

    private TcpSession? _tcpSession;
    private DatagramSession? _udpSession;
    private CancellationTokenSource? _sessionCts;
    private float _udpReconnectTimer;
    private int _udpReconnectAttempt;
    private int _udpReconnectInFlight;
    private string _reconnectHost = string.Empty;
    private ushort _reconnectPort;
    private string _lastUsername = string.Empty;
    private string _lastPassword = string.Empty;
    private Guid _activeCharacterId;
    private volatile bool _autoReconnectEnabled;
    private bool _wasTcpConnectedLastFrame;
    private float _tcpReconnectTimer;
    private int _tcpReconnectAttempt;
    private int _tcpReconnectInFlight;
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
    private HeroArchetypeClass _localClass = HeroArchetypeClass.Vanguard;
    private CharacterTraits _localTraits = CharacterTraits.None;
    private ushort _unspentStatPoints;
    private ushort _unspentSkillPoints = 1;
    private ushort _itemSeqId;
    private bool _wasMovingLastTick;
    private float _idleKeepAliveTimer;
    private byte _localAppearancePreset;
    private bool _inventoryLoaded;
    private Vector2 _localFacingDir = new(0f, 1f);
    private float _localHitFlashTimer;
    private ActionState _currentAction = ActionState.None;
    private byte _comboStep;
    private ulong _lockedTargetNetworkId;
    private float _forgeFeedbackTimer;
    private bool _lastForgeSucceeded;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiplayerSession"/> class.
    /// </summary>
    /// <param name="options">Client runtime options.</param>
    public MultiplayerSession(DesktopOptions options) =>
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
    public Vector2 LocalPlayerInterpolatedPosition
    {
        get
        {
            lock (_worldGate)
            {
                return _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
                    ? _simulation.GetInterpolatedPosition(_localPlayer) + _visualOffset
                    : Vector2.Zero;
            }
        }
    }

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
    public HeroArchetypeClass LocalPlayerClass => _localClass;

    /// <inheritdoc/>
    public CharacterTraits LocalCharacterTraits => _localTraits;

    /// <inheritdoc/>
    public CharacterSkillMask LocalUnlockedSkillMask
    {
        get
        {
            lock (_worldGate)
            {
                return _progression.UnlockedSkillMask;
            }
        }
    }

    /// <inheritdoc/>
    public float GetSkillCooldown(ushort skillId)
    {
        lock (_worldGate)
        {
            return _skillCooldowns.TryGetValue(skillId, out float cd) ? cd : 0f;
        }
    }

    /// <inheritdoc/>
    public ushort LocalUnspentStatPoints => _unspentStatPoints;

    /// <inheritdoc/>
    public ushort LocalUnspentSkillPoints => _unspentSkillPoints;

    /// <inheritdoc/>
    public ushort GetAttributePoints(PrimaryAttributeId attribute)
    {
        lock (_worldGate)
        {
            return _progression.GetAttribute(attribute);
        }
    }

    /// <inheritdoc/>
    public byte GetSkillRank(ushort skillId)
    {
        lock (_worldGate)
        {
            return _progression.GetSkillRank(skillId);
        }
    }

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
    public ActionState CurrentAction => _currentAction;

    /// <inheritdoc/>
    public ulong LockedTargetNetworkId => _lockedTargetNetworkId;

    /// <inheritdoc/>
    public float ForgeFeedbackTimer => _forgeFeedbackTimer;

    /// <inheritdoc/>
    public bool LastForgeSucceeded => _lastForgeSucceeded;

    /// <inheritdoc/>
    public void CopyInventorySlots(Span<ItemSlot> destination)
    {
        lock (_worldGate)
        {
            ReadOnlySpan<ItemSlot> source = _inventory.Slots;
            int count = Math.Min(destination.Length, source.Length);
            source[..count].CopyTo(destination);
        }
    }

    /// <inheritdoc/>
    public bool IsNearForgeStation(float maxDistance = 175f)
    {
        if (!this.IsReady || _chunkWorld is null)
        {
            return false;
        }

        Vector2 playerPos = this.LocalPlayerInterpolatedPosition;
        float maxDistSq = maxDistance * maxDistance;
        lock (_worldGate)
        {
            foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
            {
                RemoteEntityState s = kvp.Value;
                if (s.Kind == EntityKind.Npc &&
                    WorldActorCatalog.GetNpcSpec(s.ArchetypeId).Capabilities.HasFlag(NpcCapabilities.ForgeArtisan) &&
                    Vector2.DistanceSquared(playerPos, s.Position) <= maxDistSq)
                {
                    return true;
                }
            }

            int ptx = (int)MathF.Floor(playerPos.X / _tileSize);
            int pty = (int)MathF.Floor(playerPos.Y / _tileSize);
            int tileSpan = Math.Clamp((int)MathF.Ceiling(maxDistance / _tileSize), 1, 8);
            for (int dy = -tileSpan; dy <= tileSpan; dy++)
            {
                for (int dx = -tileSpan; dx <= tileSpan; dx++)
                {
                    if (_chunkWorld.Buildings.TryGetStructure(ptx + dx, pty + dy, out StructureTile st) &&
                        st.Type == StructureType.ForgeAnvil &&
                        !st.IsDestroyed)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyList<RemoteEntityView> RemoteEntities
    {
        get
        {
            lock (_worldGate)
            {
                if (!_remoteViewsDirty)
                {
                    return _cachedRemoteViews;
                }

                _remoteViewSnapshot.Clear();
                double renderTime = Math.Max(0.0, _latestServerTime - InterpolationDelaySeconds);
                foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
                {
                    RemoteEntityState s = kvp.Value;
                    double sampleTime = s.Kind == EntityKind.Projectile ? _latestServerTime : renderTime;
                    TransformSnapshot sampled = s.Interpolation.Count > 0
                        ? s.Interpolation.Sample(sampleTime, maxExtrapolationSeconds: 0.1f)
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
                        s.WeaponUpgradeLevel,
                        s.ActiveActionKind));
                }

                _cachedRemoteViews = _remoteViewSnapshot.Count == 0 ? [] : [.. _remoteViewSnapshot];
                _remoteViewsDirty = false;
                return _cachedRemoteViews;
            }
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
            ? (Volatile.Read(ref _tcpReconnectInFlight) != 0 ? "RECONNECTING" : "OFFLINE")
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
        this.MonitorConnectionHealth(fdt);

        if (_attackSwingTimer > 0f)
        {
            _attackSwingTimer = MathF.Max(0f, _attackSwingTimer - fdt);
        }

        if (_localHitFlashTimer > 0f)
        {
            _localHitFlashTimer = MathF.Max(0f, _localHitFlashTimer - fdt);
        }

        if (_forgeFeedbackTimer > 0f)
        {
            _forgeFeedbackTimer = MathF.Max(0f, _forgeFeedbackTimer - fdt);
        }

        if (_currentAction.IsActive)
        {
            float rem = _currentAction.RemainingSeconds - fdt;
            _currentAction = rem > 0f
                ? _currentAction with { RemainingSeconds = rem }
                : ActionState.None;
        }

        lock (_worldGate)
        {
            if (_skillCooldowns.Count > 0)
            {
                _cooldownKeyScratch.Clear();
                foreach (KeyValuePair<ushort, float> kvp in _skillCooldowns)
                {
                    _cooldownKeyScratch.Add(kvp.Key);
                }

                for (int i = 0; i < _cooldownKeyScratch.Count; i++)
                {
                    ushort skillId = _cooldownKeyScratch[i];
                    float nextCd = _skillCooldowns[skillId] - fdt;
                    if (nextCd <= 0f)
                    {
                        _ = _skillCooldowns.Remove(skillId);
                    }
                    else
                    {
                        _skillCooldowns[skillId] = nextCd;
                    }
                }
            }

            foreach (KeyValuePair<ulong, RemoteEntityState> kvp in _remoteEntities)
            {
                RemoteEntityState rs = kvp.Value;
                if (rs.AttackAnimTimer > 0f)
                {
                    rs.AttackAnimTimer = MathF.Max(0f, rs.AttackAnimTimer - fdt);
                    if (rs.AttackAnimTimer <= 0f)
                    {
                        rs.ActiveActionKind = ActionAnimationKind.None;
                    }
                }

                if (rs.HitFlashTimer > 0f)
                {
                    rs.HitFlashTimer = MathF.Max(0f, rs.HitFlashTimer - fdt);
                }
            }

            _remoteViewsDirty = true;

            if (_lockedTargetNetworkId != 0UL &&
                (!_remoteEntities.TryGetValue(_lockedTargetNetworkId, out RemoteEntityState? lockedState) ||
                 lockedState.CurrentHealth <= 0f))
            {
                _lockedTargetNetworkId = 0UL;
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
                _localStamina = MathF.Max(0f, _localStamina - (22f * fdt));
            }
            else if (_localStamina < _localMaxStamina)
            {
                _localStamina = MathF.Min(_localMaxStamina, _localStamina + (18f * fdt));
            }

            if (_localMana < _localMaxMana)
            {
                _localMana = MathF.Min(_localMaxMana, _localMana + (6f * fdt));
            }

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

    /// <summary>Tears down only the UDP datagram transport cleanly so unit tests can verify automatic UDP recovery.</summary>
    internal async Task DropUdpTransportForTestingAsync()
    {
        await this.TearDownUdpAsync().ConfigureAwait(false);
        _udpReconnectAttempt = 0;
        _udpReconnectTimer = 1.5f;
    }

    /// <summary>Tears down both TCP and UDP transports while keeping local world state alive to verify full OFFLINE recovery.</summary>
    internal async Task DropAllTransportsForTestingAsync()
    {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.TearDownTransportsAsync().ConfigureAwait(false);
            _isAuthenticated = false;
            _wasTcpConnectedLastFrame = false;
            _tcpReconnectAttempt = 0;
            _tcpReconnectTimer = 0.5f;
        }
        finally
        {
            _ = _connectGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _autoReconnectEnabled = false;
        _activeCharacterId = Guid.Empty;
        _lastUsername = string.Empty;
        _lastPassword = string.Empty;
        await this.TearDownTransportsAsync().ConfigureAwait(false);
        this.ClearWorldState();
        if (_sessionCts is not null)
        {
            await _sessionCts.CancelAsync().ConfigureAwait(false);
            _sessionCts.Dispose();
            _sessionCts = null;
        }

        _connectGate.Dispose();
        _udpGate.Dispose();
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
            _currentAction = ActionState.None;
            _localLevel = 1;
            _localExperience = 0U;
            _localXpToNext = 100U;
            _localMana = 80f;
            _localMaxMana = 80f;
            _localStamina = 100f;
            _localMaxStamina = 100f;
            _localDefense = 2f;
            _localClass = HeroArchetypeClass.Vanguard;
            _localTraits = CharacterTraits.None;
            _progression.Clear();
            _skillCooldowns.Clear();
            _cachedRemoteViews = [];
            _remoteViewsDirty = true;
            _lockedTargetNetworkId = 0UL;
            _forgeFeedbackTimer = 0f;
            _lastForgeSucceeded = false;
        }
    }
}
