// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.C2S;
using Carbon.Contracts.Maps.S2C;
using Carbon.Desktop.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Networking;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Prediction.Remote;
using Nalix.Abstractions.Networking.Datagrams;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Networking.Protocols;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking;

public sealed partial class NalixMultiplayerSession
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

            _tcpSubscriptions.Add(tcp.On<EntitySpawn>(this.OnEntitySpawn));
            _tcpSubscriptions.Add(tcp.On<EntityDespawn>(this.OnEntityDespawn));
            _tcpSubscriptions.Add(tcp.On<EntityDeltaBatch>(this.OnEntityDeltaBatch));
            _tcpSubscriptions.Add(tcp.On<CombatHit>(this.OnCombatHit));
            _tcpSubscriptions.Add(tcp.On<InventorySlotChanged>(this.OnInventorySlotChanged));
            _tcpSubscriptions.Add(tcp.On<EntityEquipmentChanged>(this.OnEntityEquipmentChanged));
            _tcpSubscriptions.Add(tcp.On<PlayerStatsSync>(this.OnPlayerStatsSync));
            _tcpSubscriptions.Add(tcp.On<SkillCastBroadcast>(this.OnSkillCastBroadcast));
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
            _udpSubscriptions.Add(udp.On<EntityEquipmentChanged>(this.OnEntityEquipmentChanged));
            _udpSubscriptions.Add(udp.On<SkillCastBroadcast>(this.OnSkillCastBroadcast));

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
                    _dirtyChunks.Add((cx, cy));
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
                _combatEvents.Add(new ClientCombatEvent(
                    localPos + new Vector2(0f, -24f),
                    xpGain,
                    IsCritical: false,
                    IsHeal: true,
                    Label: $"+{xpGain} XP"));
            }

            if (pkt.LeveledUp)
            {
                string msg = $"LEVEL UP! Lv.{_localLevel}";
                _combatEvents.Add(new ClientCombatEvent(
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
                    caster.ActiveActionKind = projArch is 2 or 6 ? ClientActionKind.StaffCast : ClientActionKind.BowShot;
                }
                else if (AbilityInputEncoding.TryDecodeMonsterProjectileCast(pkt.AbilitySlot, out _))
                {
                    isProjectile = true;
                    caster.ActiveActionKind = ClientActionKind.MonsterStrike;
                }
                else if (SkillCatalog.Skills.TryGet(pkt.AbilitySlot, out SkillDefinition? skillDef))
                {
                    caster.ActiveActionKind = MapSkillAnimKind(skillDef.AnimArchetype);
                }
                else
                {
                    caster.ActiveActionKind = ClientActionKind.SwordSlash;
                }

                caster.AttackAnimDuration = isProjectile ? 0.28f : 0.45f;
                caster.AttackAnimTimer = caster.AttackAnimDuration;
                _remoteViewsDirty = true;
            }
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
                    _combatEvents.Add(new ClientCombatEvent(
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
                    ? ClientActionKind.MonsterStrike
                    : ClientActionKind.SwordSlash;
                attackerState.AttackAnimDuration = 0.30f;
                attackerState.AttackAnimTimer = 0.30f;
            }

            bool isHeal = pkt.Damage < 0f;
            float mag = MathF.Abs(pkt.Damage);
            _combatEvents.Add(new ClientCombatEvent(worldPos, mag, pkt.IsCritical, isHeal));
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
                : new ItemSlot(pkt.ItemId, pkt.Count, 0UL, pkt.Durability);
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
                    string forgeMsg = $"+{pkt.UpgradeLevel} {StarterGameCatalog.GetDisplayName(next.ItemId)} Forged!";
                    _combatEvents.Add(new ClientCombatEvent(
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
                    string popup = $"+{gained} {StarterGameCatalog.GetDisplayName(next.ItemId)}";
                    _combatEvents.Add(new ClientCombatEvent(
                        localPos + new Vector2(0f, -12f),
                        gained,
                        IsCritical: false,
                        IsHeal: true,
                        Label: popup));
                    _hudNotifications.Add(popup);
                }
                else if (previous.ItemId == next.ItemId &&
                         StarterGameCatalog.Items.TryGet(next.ItemId, out ItemArchetype? slotDef) &&
                         slotDef.MaxDurability > 0f)
                {
                    if (previous.Durability > 0f && next.Durability <= 0f)
                    {
                        string brokenMsg = $"{StarterGameCatalog.GetDisplayName(next.ItemId)} Worn Out!";
                        _combatEvents.Add(new ClientCombatEvent(
                            localPos + new Vector2(0f, -14f),
                            0f,
                            IsCritical: true,
                            IsHeal: false,
                            Label: brokenMsg));
                        _hudNotifications.Add($"{brokenMsg} Visit NPC Guard to mend");
                    }
                    else if (previous.Durability <= 0f && next.Durability > 0f)
                    {
                        string mendMsg = $"{StarterGameCatalog.GetDisplayName(next.ItemId)} Mended!";
                        _combatEvents.Add(new ClientCombatEvent(
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
        lock (_worldGate)
        {
            if (_chunkWorld is null)
            {
                return;
            }

            _dirtyChunks.Add((Chunk2D.ToChunkCoord(pkt.TileX), Chunk2D.ToChunkCoord(pkt.TileY)));

            if (_chunkWorld.TrySetPropStateAt(
                pkt.TileX,
                pkt.TileY,
                pkt.Durability,
                pkt.IsDestroyed,
                out MapProp previous,
                out MapProp updated))
            {
                float prevDur = previous.Destructibility.CurrentDurability;
                float nextDur = updated.Destructibility.CurrentDurability;
                float damageDealt = MathF.Max(0f, prevDur - nextDur);
                bool wasTierBlocked = !pkt.IsDestroyed &&
                    damageDealt <= 0.01f &&
                    previous.Destructibility.IsDestructible;

                Vector2 worldPos = updated.WorldPosition;
                _propImpactEvents.Add(new ClientPropImpactEvent(
                    pkt.TileX,
                    pkt.TileY,
                    worldPos,
                    updated.Type,
                    updated.Category,
                    damageDealt,
                    nextDur,
                    updated.Destructibility.MaxDurability,
                    updated.Destructibility.HarvestTier,
                    pkt.IsDestroyed,
                    wasTierBlocked));

                if (wasTierBlocked)
                {
                    ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
                    ToolTargetKind kind = updated.Category == PropCategory.Mineral
                        ? ToolTargetKind.Mineral
                        : ToolTargetKind.Flora;
                    PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                        in activeSlot,
                        kind,
                        updated.Destructibility.HarvestTier);
                    string warn = eval.Outcome != ToolStrikeOutcome.Optimal
                        ? ToolUsagePolicy.GetMisuseWarning(eval.Outcome, updated.Destructibility.HarvestTier)
                        : $"Need Tier {updated.Destructibility.HarvestTier} Tool!";
                    _combatEvents.Add(new ClientCombatEvent(worldPos, 0f, IsCritical: false, IsHeal: false, Label: warn));
                    _hudNotifications.Add(warn);
                }
                else if (damageDealt > 0.5f)
                {
                    ItemSlot activeSlot = _inventory.GetSlot(_selectedSlot);
                    ToolTargetKind kind = updated.Category == PropCategory.Mineral
                        ? ToolTargetKind.Mineral
                        : ToolTargetKind.Flora;
                    PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                        in activeSlot,
                        kind,
                        updated.Destructibility.HarvestTier);
                    _combatEvents.Add(new ClientCombatEvent(
                        worldPos,
                        damageDealt,
                        IsCritical: pkt.IsDestroyed,
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
        lock (_worldGate)
        {
            if (_chunkWorld is null)
            {
                return;
            }

            _dirtyChunks.Add((Chunk2D.ToChunkCoord(pkt.TileX), Chunk2D.ToChunkCoord(pkt.TileY)));

            if (pkt.StructureType == 0)
            {
                _ = _chunkWorld.Buildings.TryDemolishStructure(pkt.TileX, pkt.TileY);
            }
            else
            {
                _chunkWorld.Buildings.ForcePlaceStructure(pkt.TileX, pkt.TileY, (StructureType)pkt.StructureType);
                if (_chunkWorld.Buildings.TryGetStructure(pkt.TileX, pkt.TileY, out StructureTile placed) &&
                    placed.Category == StructureCategory.Door &&
                    (byte)placed.DoorState != pkt.DoorState)
                {
                    _ = _chunkWorld.Buildings.ToggleDoor(pkt.TileX, pkt.TileY, out _);
                }
            }
        }
    }

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
        public ClientActionKind ActiveActionKind { get; set; }
        public SnapshotInterpolationBuffer Interpolation { get; } = new(16);
    }
}
