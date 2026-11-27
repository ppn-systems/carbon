// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Config.S2C;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Identity.C2S;
using Carbon.Contracts.Identity.S2C;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.C2S;
using Carbon.Contracts.Maps.S2C;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Prediction.Remote;
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

namespace Carbon.Desktop.Networking.Session;

public sealed partial class MultiplayerSession
{
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
                _sessionCts ??= new CancellationTokenSource();
                return existing;
            }

            await this.TearDownTransportsAsync().ConfigureAwait(false);
            this.ClearWorldState();
            _isAuthenticated = false;

            return await this.OpenFreshTcpTransportLockedAsync(host, tcpPort, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _connectGate.Release();
        }
    }

    private async Task<TcpSession> OpenFreshTcpTransportLockedAsync(string host, ushort tcpPort, CancellationToken ct)
    {
        await this.TearDownTransportsAsync().ConfigureAwait(false);

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

        this.ResetConfigSyncForNewConnection();
        _tcpSubscriptions.Add(tcp.On<ConfigManifest>(this.OnConfigManifest));
        _tcpSubscriptions.Add(tcp.On<EntitySpawn>(this.OnEntitySpawn));
        _tcpSubscriptions.Add(tcp.On<EntityDespawn>(this.OnEntityDespawn));
        _tcpSubscriptions.Add(tcp.On<EntityDeltaBatch>(this.OnEntityDeltaBatch));
        _tcpSubscriptions.Add(tcp.On<CombatHit>(this.OnCombatHit));
        _tcpSubscriptions.Add(tcp.On<InventorySlotChanged>(this.OnInventorySlotChanged));
        _tcpSubscriptions.Add(tcp.On<EntityEquipmentChanged>(this.OnEntityEquipmentChanged));
        _tcpSubscriptions.Add(tcp.On<PlayerStatsSync>(this.OnPlayerStatsSync));
        _tcpSubscriptions.Add(tcp.On<WalletSync>(this.OnWalletSync));
        _tcpSubscriptions.Add(tcp.On<TraderStock>(this.OnTraderStock));
        _tcpSubscriptions.Add(tcp.On<SkillCastBroadcast>(this.OnSkillCastBroadcast));
        _tcpSubscriptions.Add(tcp.On<EntityAttackPhase>(this.OnEntityAttackPhase));
        _tcpSubscriptions.Add(tcp.On<MapPropChanged>(this.OnMapPropChanged));
        _tcpSubscriptions.Add(tcp.On<MapStructureChanged>(this.OnMapStructureChanged));
        _tcpSubscriptions.Add(tcp.On<MapConstructionChanged>(this.OnMapConstructionChanged));

        _tcpSession = tcp;
        await tcp.ConnectAsync(host, tcpPort, ct).ConfigureAwait(false);
        await tcp.HandshakeAsync(ct).ConfigureAwait(false);

        _connectedHost = host;
        _connectedPort = tcpPort;
        _sessionCts = new CancellationTokenSource();
        return tcp;
    }

    private async Task TryOpenAuthenticatedUdpAsync(TcpSession tcp, CancellationToken ct)
    {
        await _udpGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await this.TearDownUdpLockedAsync().ConfigureAwait(false);
            if (ct.IsCancellationRequested || !tcp.IsConnected || !tcp.State.EncryptionEnabled)
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
                _udpSubscriptions.Add(udp.On<EntityEquipmentChanged>(this.OnEntityEquipmentChanged));
                _udpSubscriptions.Add(udp.On<SkillCastBroadcast>(this.OnSkillCastBroadcast));
                _udpSubscriptions.Add(udp.On<EntityAttackPhase>(this.OnEntityAttackPhase));

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(3000);
                await udp.ConnectAsync(_connectedHost, null, timeoutCts.Token).ConfigureAwait(false);
                _udpSession = udp;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await this.TearDownUdpLockedAsync().ConfigureAwait(false);
                await udp.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await this.TearDownUdpLockedAsync().ConfigureAwait(false);
                await udp.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _udpGate.Release();
        }
    }

    private void MonitorConnectionHealth(float frameDt)
    {
        if (!this.IsReady)
        {
            return;
        }

        if (_tcpSession is { IsConnected: true } tcp && this.IsAuthenticated)
        {
            _wasTcpConnectedLastFrame = true;
            _tcpReconnectAttempt = 0;
            _tcpReconnectTimer = 0f;

            if (this.IsUdpActive)
            {
                _udpReconnectAttempt = 0;
                _udpReconnectTimer = 0f;
                return;
            }

            if (Volatile.Read(ref _udpReconnectInFlight) != 0)
            {
                return;
            }

            _udpReconnectTimer -= frameDt;
            if (_udpReconnectTimer > 0f)
            {
                return;
            }

            int udpAttempt = _udpReconnectAttempt;
            float nextUdpBackoff = udpAttempt switch
            {
                0 => 1.5f,
                1 => 3.0f,
                2 => 6.0f,
                _ => 8.0f,
            };
            _udpReconnectAttempt = Math.Min(udpAttempt + 1, 8);
            _udpReconnectTimer = nextUdpBackoff;

            if (Interlocked.CompareExchange(ref _udpReconnectInFlight, 1, 0) == 0)
            {
                _ = this.ReconnectUdpBackgroundAsync(tcp);
            }

            return;
        }

        if (!_autoReconnectEnabled || _activeCharacterId == Guid.Empty)
        {
            return;
        }

        if (_wasTcpConnectedLastFrame)
        {
            _wasTcpConnectedLastFrame = false;
            lock (_worldGate)
            {
                _remoteEntities.Clear();
                _cachedRemoteViews = [];
                _remoteViewsDirty = true;
                _lockedTargetNetworkId = 0UL;
                _hudNotifications.Add("Connection lost - reconnecting...");
            }

            _tcpReconnectAttempt = 0;
            _tcpReconnectTimer = 1.0f;
        }

        if (Volatile.Read(ref _tcpReconnectInFlight) != 0)
        {
            return;
        }

        _tcpReconnectTimer -= frameDt;
        if (_tcpReconnectTimer > 0f)
        {
            return;
        }

        int attempt = _tcpReconnectAttempt;
        float nextBackoff = attempt switch
        {
            0 => 1.5f,
            1 => 3.0f,
            2 => 5.0f,
            _ => 8.0f,
        };
        _tcpReconnectAttempt = Math.Min(attempt + 1, 8);
        _tcpReconnectTimer = nextBackoff;

        if (Interlocked.CompareExchange(ref _tcpReconnectInFlight, 1, 0) == 0)
        {
            _ = this.ReconnectFullSessionBackgroundAsync();
        }
    }

    private async Task ReconnectFullSessionBackgroundAsync()
    {
        try
        {
            TcpSession? restoredTcp = null;
            Vector2 rejoinPos = Vector2.Zero;

            await _connectGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_autoReconnectEnabled ||
                    !this.IsReady ||
                    _activeCharacterId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(_reconnectHost) ||
                    _reconnectPort == 0 ||
                    string.IsNullOrEmpty(_lastUsername) ||
                    string.IsNullOrEmpty(_lastPassword))
                {
                    return;
                }

                if (_tcpSession is { IsConnected: true } && this.IsAuthenticated)
                {
                    return;
                }

                bool enteredWorld = false;
                try
                {
                    using CancellationTokenSource opCts = new(TimeSpan.FromSeconds(6));
                    TcpSession tcp = await this.OpenFreshTcpTransportLockedAsync(_reconnectHost, _reconnectPort, opCts.Token).ConfigureAwait(false);
                    RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

                    using PacketScope<AuthLogin> loginLease = PacketFactory<AuthLogin>.Acquire();
                    loginLease.Value.Username = _lastUsername;
                    loginLease.Value.Password = _lastPassword;
                    loginLease.Value.ClientProtocolVersion = CarbonProtocol.Version;

                    using AuthResult authRes = await tcp.RequestAsync<AuthResult>(
                        loginLease.Value,
                        reqOpts,
                        predicate: null,
                        opCts.Token).ConfigureAwait(false);

                    if (authRes.Status is AuthStatus.InvalidCredentials or AuthStatus.Locked or AuthStatus.ProtocolMismatch)
                    {
                        _autoReconnectEnabled = false;
                        lock (_worldGate)
                        {
                            _hudNotifications.Add("Reconnect stopped: authentication rejected");
                        }

                        return;
                    }

                    if (authRes.Status != AuthStatus.Ok)
                    {
                        return;
                    }

                    using PacketScope<CharacterSelect> selectLease = PacketFactory<CharacterSelect>.Acquire();
                    selectLease.Value.CharacterId = _activeCharacterId;

                    using CharacterEnterWorld enter = await tcp.RequestAsync<CharacterEnterWorld>(
                        selectLease.Value,
                        reqOpts,
                        predicate: null,
                        opCts.Token).ConfigureAwait(false);

                    if (enter.Status != OpStatus.Ok)
                    {
                        return;
                    }

                    _isAuthenticated = true;
                    enteredWorld = true;
                    restoredTcp = tcp;
                    rejoinPos = new Vector2(enter.PositionX, enter.PositionY);

                    this.ApplyReconnectedWorldState(enter);
                    try
                    {
                        await this.FetchInventorySnapshotAsync(tcp, reqOpts, opCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // Inventory snapshot sync will retry on next mutation if needed.
                    }

                    this.RequestMissingChunkDeltasAround(rejoinPos);
                    _udpReconnectAttempt = 0;
                    _udpReconnectTimer = 1.5f;
                }
                finally
                {
                    if (!enteredWorld)
                    {
                        _isAuthenticated = false;
                        await this.TearDownTransportsAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _ = _connectGate.Release();
            }

            if (restoredTcp is not null)
            {
                CancellationToken sessionToken = _sessionCts?.Token ?? CancellationToken.None;
                await this.TryOpenAuthenticatedUdpAsync(restoredTcp, sessionToken).ConfigureAwait(false);
                _tcpReconnectAttempt = 0;
                _tcpReconnectTimer = 0f;
                _wasTcpConnectedLastFrame = true;
                lock (_worldGate)
                {
                    _hudNotifications.Add(this.IsUdpActive ? "Reconnected (TCP+UDP)" : "Reconnected (TCP)");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Session disposed or timed out during reconnect.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Next tick backoff will retry automatically.
        }
        finally
        {
            _ = Interlocked.Exchange(ref _tcpReconnectInFlight, 0);
        }
    }

    private void ApplyReconnectedWorldState(CharacterEnterWorld enter)
    {
        lock (_worldGate)
        {
            _remoteEntities.Clear();
            _requestedChunks.Clear();
            _dirtyChunks.Clear();
            _reconciler.Clear();
            _clientTick = 0U;
            _latestServerTime = enter.TickRate > 0 ? enter.ServerTick / (double)enter.TickRate : 0.0;
            _hasServerReconcile = false;
            _visualOffset = Vector2.Zero;
            _selectedSlot = enter.SelectedHotbarSlot < 9 ? enter.SelectedHotbarSlot : (byte)0;
            _localLevel = Math.Max((ushort)1, enter.Level);
            _localExperience = enter.Experience;
            _localXpToNext = Math.Max(1U, enter.XpToNextLevel);
            _localHealth = enter.CurrentHealth;
            _localMaxHealth = MathF.Max(1f, enter.MaxHealth);
            _localMana = enter.CurrentMana;
            _localMaxMana = MathF.Max(1f, enter.MaxMana);
            _localStamina = enter.CurrentStamina;
            _localMaxStamina = MathF.Max(1f, enter.MaxStamina);
            _localDefense = enter.Defense;
            _localClass = (HeroArchetypeClass)enter.ClassId;
            _localTraits = enter.Traits;
            _skillCooldowns.Clear();
            _ = CharacterProgressionBinaryCodec.TryDeserializeInto(enter.ProgressionBlob ?? [], _progression);
            if (enter.UnlockedSkillMask != 0UL)
            {
                _progression.SetUnlockedSkillMask(_progression.UnlockedSkillMask | (CharacterSkillMask)enter.UnlockedSkillMask);
            }

            _unspentStatPoints = CharacterProgressionPolicy.GetUnspentStatPoints(_localLevel, _progression);
            _unspentSkillPoints = CharacterProgressionPolicy.GetUnspentSkillPoints(_localLevel, _progression);
            _remoteViewsDirty = true;
            _cachedRemoteViews = [];
            _lockedTargetNetworkId = 0UL;
            _wasMovingLastTick = false;
            _idleKeepAliveTimer = 0f;

            _tileSize = enter.TileSize > 0f ? enter.TileSize : 32f;
            _tickRate = enter.TickRate > 0 ? enter.TickRate : (ushort)30;
            _localNetworkId = enter.PlayerNetworkId;
            if (enter.AppearancePreset != 0)
            {
                _localAppearancePreset = enter.AppearancePreset;
            }

            // The world swap runs on the main thread, before the chunk deltas requested next (same FIFO inbox), so the
            // renderer never sees ChunkWorld2D/WorldSimulation replaced mid-frame (audit TS-2). Only plain values are
            // captured: `enter` is a pooled packet disposed when the reconnect returns.
            Vector2 rejoinPos = new(enter.PositionX, enter.PositionY);
            ulong worldSeed = enter.WorldSeed;
            float frequency = enter.Frequency;
            this.PostWorldMutation(() => this.RebuildWorldForRejoin(rejoinPos, worldSeed, frequency));
        }
    }

    private void RebuildWorldForRejoin(Vector2 rejoinPos, ulong worldSeed, float frequency)
    {
        lock (_worldGate)
        {
            if (_chunkWorld is not null &&
                _simulation is not null &&
                _simulation.Entities.IsAlive(_localPlayer) &&
                _worldSeed == worldSeed)
            {
                _simulation.BindNetworkIdentity(_localPlayer, _localNetworkId, ownerSessionId: 1);
                _simulation.Entities.GetTransform(_localPlayer).Position = rejoinPos;
                _simulation.EnqueueCommand(SimulationCommand.Teleport(_localPlayer, rejoinPos));
                _chunkWorld.UpdateAroundPosition(rejoinPos, chunkRadius: 4, evictionRadius: 8);
            }
            else
            {
                _worldSeed = worldSeed;
                ProceduralWorldGenerator generator = new(_worldSeed);
                _chunkWorld = new ChunkWorld2D(generator, _tileSize, frequency);
                _constructionMarkers.Clear();
                _chunkWorld.UpdateAroundPosition(rejoinPos, chunkRadius: 4, evictionRadius: 8);
                _simulation = new WorldSimulation(_tickRate, _tileSize)
                {
                    WorldPhysics = _chunkWorld,
                };
                _localPlayer = _simulation.SpawnNetworkEntity(_localNetworkId, rejoinPos, radius: 7f, ownerSessionId: 1);
                _simulation.Entities.SetVelocity(_localPlayer, new Velocity2D(0f, 0f, 5f));
            }
        }
    }

    private async Task HandleTcpFaultAsync(TcpSession faultedTcp)
    {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_tcpSession, faultedTcp))
            {
                _isAuthenticated = false;
                await this.TearDownTransportsAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _connectGate.Release();
        }
    }

    private async Task ReconnectUdpBackgroundAsync(TcpSession tcp)
    {
        try
        {
            CancellationToken sessionToken = _sessionCts?.Token ?? CancellationToken.None;
            if (sessionToken.IsCancellationRequested || !this.IsReady || !this.IsAuthenticated || !tcp.IsConnected)
            {
                return;
            }

            await this.TryOpenAuthenticatedUdpAsync(tcp, sessionToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Session torn down during UDP reconnect.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Next interval on MonitorUdpHealth will retry with backoff.
        }
        finally
        {
            _ = Interlocked.Exchange(ref _udpReconnectInFlight, 0);
        }
    }

    private async Task HandleUdpFaultAsync(DatagramSession faultedUdp)
    {
        await _udpGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_udpSession, faultedUdp))
            {
                await this.TearDownUdpLockedAsync().ConfigureAwait(false);
                if (_udpReconnectTimer <= 0f)
                {
                    _udpReconnectTimer = 1.5f;
                }
            }
        }
        finally
        {
            _ = _udpGate.Release();
        }
    }

    private void CancelSessionOperations()
    {
        CancellationTokenSource? cts = Interlocked.Exchange(ref _sessionCts, null);
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            cts.Dispose();
        }
    }

    private async Task TearDownTransportsAsync()
    {
        this.CancelSessionOperations();
        await this.TearDownUdpAsync().ConfigureAwait(false);
        _udpReconnectAttempt = 0;
        _udpReconnectTimer = 0f;

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
        await _udpGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.TearDownUdpLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _ = _udpGate.Release();
        }
    }

    private async Task TearDownUdpLockedAsync()
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
                _ = this.HandleUdpFaultAsync(udp);
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
            _ = this.HandleTcpFaultAsync(tcp);
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
                _requestedChunks[Chunk2D.PackKey(cx, cy)] = packet.Revision;

                // Imported on the main thread so the renderer never reads a chunk mid-import (audit TS-2).
                this.PostWorldMutation(() =>
                {
                    if (_chunkWorld is not null)
                    {
                        _chunkWorld.ImportDelta(domainDelta);
                        this.OnChunkDeltaImportedLocked(domainDelta);
                    }

                    _dirtyChunks.Add((cx, cy));
                });
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
            lock (_worldGate)
            {
                _localHealth = pkt.CurrentHealth;
                _localMaxHealth = pkt.MaxHealth > 0f ? pkt.MaxHealth : 100f;
                _localAppearancePreset = (byte)pkt.ArchetypeId;
                _localLevel = Math.Max((ushort)1, pkt.Level);
            }

            return;
        }

        RemoteEntityState state = new()
        {
            NetworkId = pkt.NetworkId,
            Kind = pkt.Kind,
            ArchetypeId = pkt.ArchetypeId,
            Name = pkt.Name,
            Position = new Vector2(pkt.PositionX, pkt.PositionY),
            Velocity = new Vector2(pkt.VelocityX, pkt.VelocityY),
            Radius = pkt.Radius > 0f ? pkt.Radius : 7f,
            CurrentHealth = pkt.CurrentHealth,
            MaxHealth = pkt.MaxHealth,
            EquippedHeadId = pkt.EquippedHeadId,
            EquippedChestId = pkt.EquippedChestId,
            EquippedLegsId = pkt.EquippedLegsId,
            EquippedFeetId = pkt.EquippedFeetId,
            EquippedWeaponId = pkt.EquippedWeaponId,
            Level = Math.Max((ushort)1, pkt.Level),
            LootCount = pkt.LootCount,
            WeaponUpgradeLevel = pkt.WeaponUpgradeLevel,
            LootCurrency = pkt.LootCurrency,
            LootOwnerNetworkId = pkt.LootOwnerNetworkId,
            LootProtectionRemaining = pkt.LootProtectionMs / 1000f,
        };

        lock (_worldGate)
        {
            _ = state.Interpolation.PushSnapshot(new TransformSnapshot(
                _latestServerTime,
                state.Position,
                state.Velocity));
            _remoteEntities[pkt.NetworkId] = state;
            _remoteViewsDirty = true;
        }
    }

    private void OnEntityDespawn(EntityDespawn pkt)
    {
        lock (_worldGate)
        {
            _ = _remoteEntities.TryRemove(pkt.NetworkId, out _);
            _remoteViewsDirty = true;
            if (pkt.NetworkId == _lockedTargetNetworkId)
            {
                _lockedTargetNetworkId = 0UL;
            }
        }
    }

    private void OnEntityEquipmentChanged(EntityEquipmentChanged pkt)
    {
        lock (_worldGate)
        {
            if (pkt.NetworkId == _localNetworkId)
            {
                _localLevel = Math.Max((ushort)1, pkt.Level);
                if (pkt.MaxHealth > 0f)
                {
                    _localMaxHealth = MathF.Max(1f, pkt.MaxHealth);
                }

                return;
            }

            if (_remoteEntities.TryGetValue(pkt.NetworkId, out RemoteEntityState? state))
            {
                state.EquippedHeadId = pkt.EquippedHeadId;
                state.EquippedChestId = pkt.EquippedChestId;
                state.EquippedLegsId = pkt.EquippedLegsId;
                state.EquippedFeetId = pkt.EquippedFeetId;
                state.EquippedWeaponId = pkt.EquippedWeaponId;
                state.WeaponUpgradeLevel = pkt.WeaponUpgradeLevel;
                state.Level = Math.Max((ushort)1, pkt.Level);
                if (pkt.MaxHealth > 0f)
                {
                    state.MaxHealth = MathF.Max(1f, pkt.MaxHealth);
                }

                _remoteViewsDirty = true;
            }
        }
    }

    private void OnWalletSync(WalletSync pkt)
    {
        lock (_worldGate)
        {
            // Pushes can be reordered; only apply a newer server sequence (ADR-012).
            if (_hasWalletSync && pkt.Sequence < _walletSequence)
            {
                return;
            }

            _hasWalletSync = true;
            _walletSequence = pkt.Sequence;
            _ = Interlocked.Exchange(ref _localGold, Math.Max(0L, pkt.Gold));
            _ = Interlocked.Exchange(ref _localDiamonds, Math.Max(0L, pkt.Diamonds));
        }
    }

    private void OnPlayerStatsSync(PlayerStatsSync pkt)
    {
        lock (_worldGate)
        {
            ushort prevLevel = _localLevel;
            uint prevXp = _localExperience;
            uint xpGain = 0U;
            if (pkt.Level == prevLevel && pkt.Experience > prevXp)
            {
                xpGain = pkt.Experience - prevXp;
            }
            else if (pkt.Level > prevLevel)
            {
                uint prevThresh = CharacterProgressionPolicy.GetXpToNextLevel(prevLevel);
                xpGain = prevThresh - Math.Min(prevXp, prevThresh) + pkt.Experience;
            }

            _localLevel = Math.Max((ushort)1, pkt.Level);
            _localExperience = pkt.Experience;
            _localXpToNext = Math.Max(1U, pkt.XpToNextLevel);
            _localHealth = pkt.CurrentHealth;
            _localMaxHealth = MathF.Max(1f, pkt.MaxHealth);
            _localMana = pkt.CurrentMana;
            _localMaxMana = MathF.Max(1f, pkt.MaxMana);
            _localStamina = pkt.CurrentStamina;
            _localMaxStamina = MathF.Max(1f, pkt.MaxStamina);
            _localDefense = pkt.Defense;
            if (pkt.MaxFood > 0f)
            {
                _localFood = pkt.Food;
                _localMaxFood = pkt.MaxFood;
                _localHungerState = pkt.HungerState;
                _localMalnutrition = pkt.Malnutrition;
                _localMood = pkt.Mood;
                _localFoodPoisoned = pkt.FoodPoisoned;
                _localEatProgress = pkt.EatProgress;
            }

            if (pkt.MaxRest > 0f)
            {
                _localRest = pkt.Rest;
                _localMaxRest = pkt.MaxRest;
                _localRestState = pkt.RestState;
                _localRestMode = pkt.RestMode;
            }

            _localTraits = pkt.Traits;
            _unspentStatPoints = pkt.UnspentStatPoints;
            _unspentSkillPoints = pkt.UnspentSkillPoints;
            _ = CharacterProgressionBinaryCodec.TryDeserializeInto(pkt.ProgressionBlob ?? [], _progression);
            _progression.SetUnlockedSkillMask(_progression.UnlockedSkillMask | pkt.UnlockedSkillMask);

            _skillCooldowns.Clear();
            ushort[] cdIds = pkt.CooldownSkillIds ?? [];
            float[] cdSecs = pkt.CooldownRemainingSeconds ?? [];
            int cdLen = Math.Min(cdIds.Length, cdSecs.Length);
            for (int cIdx = 0; cIdx < cdLen; cIdx++)
            {
                if (cdSecs[cIdx] > 0f)
                {
                    _skillCooldowns[cdIds[cIdx]] = cdSecs[cIdx];
                }
            }

            Vector2 localPos = _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
                ? _simulation.GetInterpolatedPosition(_localPlayer) + _visualOffset
                : Vector2.Zero;

            if (xpGain > 0U && _inventoryLoaded)
            {
                _combatEvents.Add(new CombatFeedbackEvent(
                    localPos + new Vector2(0f, -24f),
                    xpGain,
                    IsCritical: false,
                    IsHeal: true,
                    Label: $"+{xpGain} XP"));
            }

            if (pkt.LeveledUp)
            {
                string msg = $"LEVEL UP! Lv.{_localLevel}";
                _combatEvents.Add(new CombatFeedbackEvent(
                    localPos + new Vector2(0f, -34f),
                    _localLevel,
                    IsCritical: true,
                    IsHeal: true,
                    Label: msg));
                _hudNotifications.Add(msg);
            }
        }
    }

    private void OnSkillCastBroadcast(SkillCastBroadcast pkt)
    {
        lock (_worldGate)
        {
            if (pkt.CasterNetworkId != _localNetworkId &&
                _remoteEntities.TryGetValue(pkt.CasterNetworkId, out RemoteEntityState? caster))
            {
                Vector2 delta = new(pkt.AimX - caster.Position.X, pkt.AimY - caster.Position.Y);
                if (delta.LengthSquared() > 1e-4f)
                {
                    caster.FacingDir = Vector2.Normalize(delta);
                }

                bool isProjectile = false;
                if (AbilityInputEncoding.TryDecodePlayerProjectileCast(pkt.AbilitySlot, out byte projArch))
                {
                    isProjectile = true;
                    caster.ActiveActionKind = projArch is 2 or 6 ? ActionAnimationKind.StaffCast : ActionAnimationKind.BowShot;
                }
                else if (AbilityInputEncoding.TryDecodeMonsterProjectileCast(pkt.AbilitySlot, out byte mProjArch))
                {
                    isProjectile = true;
                    caster.ActiveActionKind = mProjArch == 5
                        ? ActionAnimationKind.MonsterBowShot
                        : ActionAnimationKind.MonsterSpellCast;
                }
                else if (AbilityInputEncoding.TryDecodeMonsterMeleeCast(pkt.AbilitySlot, out _))
                {
                    caster.ActiveActionKind = caster.ArchetypeId is WorldActorCatalog.RockGolem or WorldActorCatalog.CrimsonDemon
                        ? ActionAnimationKind.MonsterHeavySlam
                        : ActionAnimationKind.MonsterStrike;
                }
                else if (SkillCatalog.Skills.TryGet(pkt.AbilitySlot, out SkillDefinition? skillDef))
                {
                    caster.ActiveActionKind = MapSkillAnimKind(skillDef.AnimArchetype);
                }
                else
                {
                    caster.ActiveActionKind = ActionAnimationKind.SwordSlash;
                }

                caster.AttackAnimDuration = isProjectile ? 0.28f : 0.45f;
                caster.AttackAnimTimer = caster.AttackAnimDuration;
                _remoteViewsDirty = true;
            }
        }
    }

    private void OnEntityAttackPhase(EntityAttackPhase pkt)
    {
        lock (_worldGate)
        {
            if (!_remoteEntities.TryGetValue(pkt.NetworkId, out RemoteEntityState? attacker))
            {
                return;
            }

            Vector2 dir = new(pkt.DirectionX, pkt.DirectionY);
            bool finiteDir = float.IsFinite(dir.X) && float.IsFinite(dir.Y) && dir.LengthSquared() > 1e-4f;
            if (finiteDir && pkt.Phase != EntityAttackPhase.PhaseNone)
            {
                attacker.FacingDir = Vector2.Normalize(dir);
            }

            attacker.AttackKind = pkt.AttackKind;
            attacker.AttackPhase = pkt.Phase > EntityAttackPhase.PhaseRecovery ? EntityAttackPhase.PhaseNone : pkt.Phase;
            attacker.AttackPhaseElapsed = 0f;
            attacker.AttackPhaseDuration = pkt.PhaseDurationTicks / (float)Math.Max((ushort)1, _tickRate);
            attacker.AttackTarget = float.IsFinite(pkt.TargetX) && float.IsFinite(pkt.TargetY)
                ? new Vector2(pkt.TargetX, pkt.TargetY)
                : attacker.Position;
            attacker.AttackDirection = finiteDir ? Vector2.Normalize(dir) : attacker.FacingDir;
            attacker.AttackAreaRadius = float.IsFinite(pkt.AreaRadius) ? Math.Clamp(pkt.AreaRadius, 0f, 512f) : 0f;
            attacker.AttackHalfAngle = float.IsFinite(pkt.HalfAngleRadians) ? Math.Clamp(pkt.HalfAngleRadians, 0f, MathF.PI) : 0f;
            attacker.AttackShape = pkt.Shape;
            _remoteViewsDirty = true;
        }
    }

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
                        if (state.Velocity.LengthSquared() > 4f)
                        {
                            state.FacingDir = Vector2.Normalize(state.Velocity);
                        }
                    }

                    if (i < pkt.CurrentHealths.Length)
                    {
                        state.CurrentHealth = pkt.CurrentHealths[i];
                    }

                    _ = state.Interpolation.PushSnapshot(new TransformSnapshot(snapshotTime, state.Position, state.Velocity));
                }
            }

            _remoteViewsDirty = true;
        }
    }

    private void OnCombatHit(CombatHit pkt)
    {
        lock (_worldGate)
        {
            _remoteViewsDirty = true;
            Vector2 worldPos = _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
                ? _simulation.GetInterpolatedPosition(_localPlayer) + _visualOffset
                : Vector2.Zero;

            if (pkt.DefenderNetworkId == _localNetworkId)
            {
                _localHealth = pkt.RemainingHealth;
                if (pkt.Damage > 0f)
                {
                    _localHitFlashTimer = 0.16f;
                }

                if (pkt.IsKilled)
                {
                    _lockedTargetNetworkId = 0UL;
                    _hudNotifications.Add("YOU WERE SLAIN - RESPAWNED AT SANCTUARY");
                    _combatEvents.Add(new CombatFeedbackEvent(
                        worldPos + new Vector2(0f, -20f),
                        0f,
                        IsCritical: true,
                        IsHeal: false,
                        Label: "SLAIN! RESPAWNED"));
                }
            }
            else if (_remoteEntities.TryGetValue(pkt.DefenderNetworkId, out RemoteEntityState? defenderState))
            {
                defenderState.CurrentHealth = pkt.RemainingHealth;
                worldPos = defenderState.Position;
                if (pkt.Damage > 0f)
                {
                    defenderState.HitFlashTimer = 0.16f;
                }

                if (pkt.IsKilled && pkt.DefenderNetworkId == _lockedTargetNetworkId)
                {
                    _lockedTargetNetworkId = 0UL;
                }
            }

            if (pkt.AttackerNetworkId != _localNetworkId &&
                _remoteEntities.TryGetValue(pkt.AttackerNetworkId, out RemoteEntityState? attackerState))
            {
                Vector2 aimDelta = worldPos - attackerState.Position;
                if (aimDelta.LengthSquared() > 1e-4f)
                {
                    attackerState.FacingDir = Vector2.Normalize(aimDelta);
                }

                attackerState.ActiveActionKind = attackerState.Kind == EntityKind.Monster
                    ? ActionAnimationKind.MonsterStrike
                    : ActionAnimationKind.SwordSlash;
                attackerState.AttackAnimDuration = 0.30f;
                attackerState.AttackAnimTimer = 0.30f;
            }

            bool isHeal = pkt.Damage < 0f;
            float mag = MathF.Abs(pkt.Damage);
            _combatEvents.Add(new CombatFeedbackEvent(worldPos, mag, pkt.IsCritical, isHeal));
        }
    }

    private void OnInventorySlotChanged(InventorySlotChanged pkt)
    {
        lock (_worldGate)
        {
            if (pkt.SlotIndex >= _inventory.Capacity)
            {
                return;
            }

            ItemSlot previous = _inventory.GetSlot(pkt.SlotIndex);
            ItemSlot rawNext = pkt.ItemId == 0 || pkt.Count <= 0
                ? ItemSlot.Empty
                : new ItemSlot(pkt.ItemId, pkt.Count, 0UL, pkt.Durability, FreshnessSeconds: pkt.FreshnessSeconds);
            ItemSlot next = rawNext.IsEmpty
                ? ItemSlot.Empty
                : ItemEnhancementPolicy.WithUpgradeLevel(in rawNext, pkt.UpgradeLevel);
            _inventory.SetSlot(pkt.SlotIndex, in next);

            Vector2 localPos = _simulation is not null && _simulation.Entities.IsAlive(_localPlayer)
                ? _simulation.GetInterpolatedPosition(_localPlayer) + _visualOffset
                : Vector2.Zero;

            if (_inventoryLoaded && next.ItemId != 0 && next.Quantity > 0)
            {
                byte prevUpg = ItemEnhancementPolicy.GetUpgradeLevel(in previous);
                if (previous.ItemId == next.ItemId && pkt.UpgradeLevel > prevUpg)
                {
                    string forgeMsg = $"+{pkt.UpgradeLevel} {_items.GetDisplayName(next.ItemId)} Forged!";
                    _combatEvents.Add(new CombatFeedbackEvent(
                        localPos + new Vector2(0f, -14f),
                        pkt.UpgradeLevel,
                        IsCritical: true,
                        IsHeal: true,
                        Label: forgeMsg));
                    _hudNotifications.Add(forgeMsg);
                    return;
                }

                int gained = previous.ItemId == next.ItemId
                    ? Math.Max(0, next.Quantity - previous.Quantity)
                    : next.Quantity;

                if (gained > 0)
                {
                    string popup = $"+{gained} {_items.GetDisplayName(next.ItemId)}";
                    _combatEvents.Add(new CombatFeedbackEvent(
                        localPos + new Vector2(0f, -12f),
                        gained,
                        IsCritical: false,
                        IsHeal: true,
                        Label: popup));
                    _hudNotifications.Add(popup);
                }
                else if (previous.ItemId == next.ItemId &&
                         _items.Rules.Items.TryGet(next.ItemId, out ItemArchetype? slotDef) &&
                         slotDef.MaxDurability > 0f)
                {
                    if (previous.Durability > 0f && next.Durability <= 0f)
                    {
                        string brokenMsg = $"{_items.GetDisplayName(next.ItemId)} Worn Out!";
                        _combatEvents.Add(new CombatFeedbackEvent(
                            localPos + new Vector2(0f, -14f),
                            0f,
                            IsCritical: true,
                            IsHeal: false,
                            Label: brokenMsg));
                        _hudNotifications.Add($"{brokenMsg} Visit NPC Guard to mend");
                    }
                    else if (previous.Durability <= 0f && next.Durability > 0f)
                    {
                        string mendMsg = $"{_items.GetDisplayName(next.ItemId)} Mended!";
                        _combatEvents.Add(new CombatFeedbackEvent(
                            localPos + new Vector2(0f, -14f),
                            next.Durability,
                            IsCritical: false,
                            IsHeal: true,
                            Label: mendMsg));
                        _hudNotifications.Add(mendMsg);
                    }
                }
            }
        }
    }

    private void OnMapPropChanged(MapPropChanged pkt)
    {
        // Copy out of the (possibly pooled) packet; the mutation runs later on the main thread (audit TS-2).
        int tileX = pkt.TileX;
        int tileY = pkt.TileY;
        float durability = pkt.Durability;
        bool isDestroyed = pkt.IsDestroyed;
        this.PostWorldMutation(() => this.ApplyMapPropChanged(tileX, tileY, durability, isDestroyed));
    }

    private void ApplyMapPropChanged(int tileX, int tileY, float durability, bool isDestroyed)
    {
        lock (_worldGate)
        {
            if (_chunkWorld is null)
            {
                return;
            }

            _dirtyChunks.Add((Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY)));

            if (_chunkWorld.TrySetPropStateAt(
                tileX,
                tileY,
                durability,
                isDestroyed,
                out MapProp previous,
                out MapProp updated))
            {
                float prevDur = previous.Destructibility.CurrentDurability;
                float nextDur = updated.Destructibility.CurrentDurability;
                float damageDealt = MathF.Max(0f, prevDur - nextDur);
                bool wasTierBlocked = !isDestroyed &&
                    damageDealt <= 0.01f &&
                    previous.Destructibility.IsDestructible;

                Vector2 worldPos = updated.WorldPosition;
                _propImpactEvents.Add(new PropImpactEvent(
                    tileX,
                    tileY,
                    worldPos,
                    updated.Type,
                    updated.Category,
                    damageDealt,
                    nextDur,
                    updated.Destructibility.MaxDurability,
                    updated.Destructibility.HarvestTier,
                    isDestroyed,
                    wasTierBlocked));

                if (wasTierBlocked)
                {
                    ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
                    ToolTargetKind kind = updated.Category == PropCategory.Mineral
                        ? ToolTargetKind.Mineral
                        : ToolTargetKind.Flora;
                    PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                        _items.Rules,
                        in activeSlot,
                        kind,
                        updated.Destructibility.HarvestTier);
                    string warn = eval.Outcome != ToolStrikeOutcome.Optimal
                        ? ToolUsagePolicy.GetMisuseWarning(eval.Outcome, updated.Destructibility.HarvestTier)
                        : $"Need Tier {updated.Destructibility.HarvestTier} Tool!";
                    _combatEvents.Add(new CombatFeedbackEvent(worldPos, 0f, IsCritical: false, IsHeal: false, Label: warn));
                    _hudNotifications.Add(warn);
                }
                else if (damageDealt > 0.5f)
                {
                    ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
                    ToolTargetKind kind = updated.Category == PropCategory.Mineral
                        ? ToolTargetKind.Mineral
                        : ToolTargetKind.Flora;
                    PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                        _items.Rules,
                        in activeSlot,
                        kind,
                        updated.Destructibility.HarvestTier);
                    _combatEvents.Add(new CombatFeedbackEvent(
                        worldPos,
                        damageDealt,
                        IsCritical: isDestroyed,
                        IsHeal: false));
                    if (eval.Outcome == ToolStrikeOutcome.SwordMisuseOnWood)
                    {
                        _hudNotifications.Add(ToolUsagePolicy.GetMisuseWarning(eval.Outcome, updated.Destructibility.HarvestTier));
                    }
                }
            }
        }
    }

    private void OnMapStructureChanged(MapStructureChanged pkt)
    {
        // Copy out of the (possibly pooled) packet; the mutation runs later on the main thread (audit TS-2).
        int tileX = pkt.TileX;
        int tileY = pkt.TileY;
        byte structureType = pkt.StructureType;
        byte doorState = pkt.DoorState;
        this.PostWorldMutation(() => this.ApplyMapStructureChanged(tileX, tileY, structureType, doorState));
    }

    private void ApplyMapStructureChanged(int tileX, int tileY, byte structureType, byte doorState)
    {
        lock (_worldGate)
        {
            if (_chunkWorld is null)
            {
                return;
            }

            _dirtyChunks.Add((Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY)));

            if (structureType == 0)
            {
                _ = _chunkWorld.Buildings.TryDemolishStructure(tileX, tileY);
            }
            else
            {
                _chunkWorld.Buildings.ForcePlaceStructure(tileX, tileY, (StructureType)structureType);
                if (_chunkWorld.Buildings.TryGetStructure(tileX, tileY, out StructureTile placed) &&
                    placed.Category == StructureCategory.Door &&
                    (byte)placed.DoorState != doorState)
                {
                    _ = _chunkWorld.Buildings.ToggleDoor(tileX, tileY, out _);
                }
            }
        }
    }

    /// <summary>Gets whether ground loot <paramref name="s"/> is still reserved for another player (ADR-019); the server decides.</summary>
    private bool IsLootLockedForLocalPlayer(RemoteEntityState s) =>
        s.LootOwnerNetworkId != 0UL && s.LootOwnerNetworkId != _localNetworkId && s.LootProtectionRemaining > 0f;

    private sealed class RemoteEntityState
    {
        public ulong NetworkId { get; init; }
        public EntityKind Kind { get; init; }
        public ushort ArchetypeId { get; init; }
        public string Name { get; init; } = string.Empty;
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector2 FacingDir { get; set; } = new(0f, 1f);
        public float AttackAnimTimer { get; set; }
        public float AttackAnimDuration { get; set; } = 0.30f;
        public float HitFlashTimer { get; set; }
        public float Radius { get; init; }
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; set; }
        public ushort EquippedHeadId { get; set; }
        public ushort EquippedChestId { get; set; }
        public ushort EquippedLegsId { get; set; }
        public ushort EquippedFeetId { get; set; }
        public ushort EquippedWeaponId { get; set; }
        public ushort Level { get; set; } = 1;
        public int LootCount { get; init; }
        public byte WeaponUpgradeLevel { get; set; }
        public LootCurrency LootCurrency { get; init; }
        public ulong LootOwnerNetworkId { get; init; }
        public float LootProtectionRemaining { get; set; }
        public ActionAnimationKind ActiveActionKind { get; set; }
        public byte AttackKind { get; set; }
        public byte AttackPhase { get; set; }
        public float AttackPhaseElapsed { get; set; }
        public float AttackPhaseDuration { get; set; }
        public Vector2 AttackTarget { get; set; }
        public Vector2 AttackDirection { get; set; }
        public float AttackAreaRadius { get; set; }
        public float AttackHalfAngle { get; set; }
        public byte AttackShape { get; set; }
        public SnapshotInterpolationBuffer Interpolation { get; } = new(16);

        public AttackPhaseView BuildAttackView() => new(
            this.AttackKind,
            this.AttackPhase,
            this.AttackPhaseDuration > 0f ? Math.Clamp(this.AttackPhaseElapsed / this.AttackPhaseDuration, 0f, 1f) : 1f,
            this.AttackTarget,
            this.AttackDirection,
            this.AttackAreaRadius,
            this.AttackHalfAngle,
            this.AttackShape);
    }
}
