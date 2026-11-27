// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.S2C;
using Microsoft.Extensions.Logging;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Datagrams;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;
using Nalix.Network.Connections;
using Nalix.Runtime.Extensions;

namespace Carbon.Host;

/// <summary>
/// Pushes world simulation AoI updates and map edits onto live transport connections
/// (routing high-frequency deltas and combat hits over Authenticated UDP when attached, with TCP fallback).
/// </summary>
internal sealed class WorldOutboundSink : IWorldOutboundSink
{
    private readonly SessionTransportRegistry _transportRegistry;
    private readonly ILogger<WorldOutboundSink> _logger;
    private readonly DeltaColumnPool _deltaColumns = new();
    private volatile IConnectionHub? _hub;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldOutboundSink"/> class.
    /// </summary>
    public WorldOutboundSink(SessionTransportRegistry transportRegistry, ILogger<WorldOutboundSink> logger)
    {
        _transportRegistry = transportRegistry ?? throw new ArgumentNullException(nameof(transportRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Binds the live connection hub created by the host builder.</summary>
    public void AttachHub(IConnectionHub hub) =>
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));

    /// <inheritdoc/>
    public void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn)
    {
        if (!this.TryResolveConnection(connectionId, out IConnection? conn) || conn is null)
        {
            return;
        }

        _ = DispatchEntitySpawnAsync(conn, spawn, _logger);
    }

    /// <inheritdoc/>
    public void SendEntityDespawn(ulong connectionId, ulong networkId, ulong serverTick)
    {
        if (!this.TryResolveConnection(connectionId, out IConnection? conn) || conn is null)
        {
            return;
        }

        _ = DispatchEntityDespawnAsync(conn, networkId, serverTick, _logger);
    }

    /// <inheritdoc/>
    public void SendEntityDeltaBatch(
        ulong connectionId,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        ReadOnlySpan<EntityDeltaEntry> entries)
    {
        IDatagramTransport? dgram = null;
        IConnection? streamConn = null;

        if (_transportRegistry.TryGetDatagramConnection(connectionId, out DatagramConnection? dg) &&
            dg is not null &&
            dg.Transport is IDatagramTransport datagramTransport)
        {
            dgram = datagramTransport;
        }
        else if (!this.TryResolveConnection(connectionId, out streamConn) || streamConn is null)
        {
            return;
        }

        // PERF-3: exact-length columns come from a per-count pool and go back once the send completes.
        DeltaColumns columns = _deltaColumns.Rent(entries);
        if (dgram is not null)
        {
            _ = DispatchEntityDeltaBatchDatagramAsync(dgram, serverTick, lastProcessedClientTick, selfX, selfY, columns, _deltaColumns, _logger);
            return;
        }

        _ = DispatchEntityDeltaBatchAsync(streamConn!, serverTick, lastProcessedClientTick, selfX, selfY, columns, _deltaColumns, _logger);
    }

    /// <inheritdoc/>
    public void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, uint revision, float durability, bool isDestroyed)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchPropChangedAsync(conn, tileX, tileY, revision, durability, isDestroyed, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendStructureChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, float durability, byte doorState, byte connectivity)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchStructureChangedAsync(conn, tileX, tileY, structureType, durability, doorState, connectivity, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled) =>
        this.SendCombatHit(recipientConnectionIds, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, statusEffectId: 0, statusEffectLevel: 0);

    /// <inheritdoc/>
    public void SendCombatHit(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong attackerNetworkId,
        ulong defenderNetworkId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled,
        ushort statusEffectId,
        byte statusEffectLevel)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            ulong recipientId = recipientConnectionIds[i];
            if (_transportRegistry.TryGetDatagramConnection(recipientId, out DatagramConnection? dg) &&
                dg is not null &&
                dg.Transport is IDatagramTransport dgramTransport)
            {
                _ = DispatchCombatHitDatagramAsync(dgramTransport, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, statusEffectId, statusEffectLevel, _logger);
            }
            else if (this.TryResolveConnection(recipientId, out IConnection? conn) && conn is not null)
            {
                _ = DispatchCombatHitAsync(conn, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, statusEffectId, statusEffectLevel, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0, float freshnessSeconds = 0f)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchInventorySlotChangedAsync(conn, slotIndex, itemId, quantity, durability, upgradeLevel, freshnessSeconds, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendEntityEquipmentChanged(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong entityNetworkId,
        ushort headId,
        ushort chestId,
        ushort legsId,
        ushort feetId,
        ushort weaponId,
        byte weaponUpgrade,
        ushort level,
        float maxHealth = 0f)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchEquipmentChangedAsync(conn, entityNetworkId, headId, chestId, legsId, feetId, weaponId, weaponUpgrade, level, maxHealth, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendPlayerStatsSync(ulong connectionId, in PlayerStatsSnapshot snapshot)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchPlayerStatsSyncAsync(conn, snapshot, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendEntityAttackPhase(ReadOnlySpan<ulong> recipientConnectionIds, in AttackPhaseSnapshot snapshot)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchAttackPhaseAsync(conn, snapshot, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendWalletSync(ulong connectionId, long gold, long diamonds, ulong sequence)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchWalletSyncAsync(conn, gold, diamonds, sequence, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendStaminaStatus(ulong connectionId, bool exhausted, uint refusalCount)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchStaminaStatusAsync(conn, exhausted, refusalCount, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendTraderStock(ulong connectionId, TraderStockSnapshot stock)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchTraderStockAsync(conn, stock, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendPlayerDeath(ulong connectionId, in PlayerDeathSnapshot death)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchPlayerDeathAsync(conn, death, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendPlayerOxygen(ulong connectionId, in PlayerOxygenSnapshot oxygen)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchPlayerOxygenAsync(conn, oxygen, _logger);
        }
    }

    /// <inheritdoc/>
    public void SendStatusEffects(ulong connectionId, ReadOnlySpan<StatusEffectEntry> entries)
    {
        if (!this.TryResolveConnection(connectionId, out IConnection? conn) || conn is null)
        {
            return;
        }

        // Change-driven (ADR-029 D9), never per tick: the entries are copied because the span does not outlive the call.
        _ = DispatchStatusEffectsAsync(conn, entries.ToArray(), _logger);
    }

    /// <inheritdoc/>
    public void SendSkillCastBroadcast(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong casterNetworkId,
        byte abilitySlot,
        float originX,
        float originY,
        float aimX,
        float aimY)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchSkillCastAsync(conn, casterNetworkId, abilitySlot, originX, originY, aimX, aimY, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendEnhancementAnnouncement(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong playerNetworkId,
        string playerName,
        ushort itemId,
        EnhancementOutcome outcome,
        byte level)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchEnhancementAnnouncementAsync(conn, playerNetworkId, playerName, itemId, outcome, level, _logger);
            }
        }
    }

    private static async Task DispatchEnhancementAnnouncementAsync(
        IConnection connection,
        ulong playerNetworkId,
        string playerName,
        ushort itemId,
        EnhancementOutcome outcome,
        byte level,
        ILogger logger)
    {
        try
        {
            using PacketScope<EnhancementAnnouncement> lease = PacketFactory<EnhancementAnnouncement>.Acquire();
            EnhancementAnnouncement pkt = lease.Value;
            pkt.PlayerNetworkId = playerNetworkId;
            pkt.PlayerName = playerName ?? string.Empty;
            pkt.ItemId = itemId;
            pkt.Outcome = outcome;
            pkt.Level = level;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EnhancementAnnouncement), ex);
        }
    }

    private bool TryResolveConnection(ulong connectionId, out IConnection? connection)
    {
        IConnectionHub? hub = _hub ?? _transportRegistry.ResolveHub();
        if (hub is null)
        {
            connection = null;
            return false;
        }

        _hub = hub;
        connection = hub.GetConnection(connectionId);
        if (connection is null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("TryResolveConnection miss for ConnectionId={ConnectionId}", connectionId);
        }

        return connection is not null;
    }

    private static async Task DispatchEntitySpawnAsync(
        IConnection connection,
        EntitySpawnSnapshot spawn,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntitySpawn> lease = PacketFactory<EntitySpawn>.Acquire();
            EntitySpawn pkt = lease.Value;
            pkt.NetworkId = spawn.NetworkId;
            pkt.Kind = spawn.Kind;
            pkt.ArchetypeId = spawn.ArchetypeId;
            pkt.Name = spawn.Name;
            pkt.PositionX = spawn.PositionX;
            pkt.PositionY = spawn.PositionY;
            pkt.VelocityX = spawn.VelocityX;
            pkt.VelocityY = spawn.VelocityY;
            pkt.Radius = spawn.Radius;
            pkt.CurrentHealth = spawn.CurrentHealth;
            pkt.MaxHealth = spawn.MaxHealth;
            pkt.EquippedHeadId = spawn.EquippedHeadId;
            pkt.EquippedChestId = spawn.EquippedChestId;
            pkt.EquippedLegsId = spawn.EquippedLegsId;
            pkt.EquippedFeetId = spawn.EquippedFeetId;
            pkt.EquippedWeaponId = spawn.EquippedWeaponId;
            pkt.Level = spawn.Level;
            pkt.LootCount = spawn.LootCount;
            pkt.WeaponUpgradeLevel = spawn.WeaponUpgradeLevel;
            pkt.LootCurrency = spawn.LootCurrency;
            pkt.LootOwnerNetworkId = spawn.LootOwnerNetworkId;
            pkt.LootProtectionMs = spawn.LootProtectionMs;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntitySpawn), ex);
        }
    }

    private static async Task DispatchEntityDespawnAsync(
        IConnection connection,
        ulong networkId,
        ulong serverTick,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntityDespawn> lease = PacketFactory<EntityDespawn>.Acquire();
            EntityDespawn pkt = lease.Value;
            pkt.NetworkId = networkId;
            pkt.ServerTick = serverTick;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityDespawn), ex);
        }
    }

    private static async Task DispatchEntityDeltaBatchAsync(
        IConnection connection,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        DeltaColumns columns,
        DeltaColumnPool pool,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntityDeltaBatch> lease = PacketFactory<EntityDeltaBatch>.Acquire();
            PopulateEntityDeltaBatch(lease.Value, serverTick, lastProcessedClientTick, selfX, selfY, columns);
            try
            {
                bool encrypt = connection.Algorithm != CipherSuiteType.None;
                await connection.SendAsync(lease.Value, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                ClearColumns(lease.Value);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityDeltaBatch), ex);
        }
        finally
        {
            pool.Return(columns);
        }
    }

    private static async Task DispatchEntityDeltaBatchDatagramAsync(
        IDatagramTransport transport,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        DeltaColumns columns,
        DeltaColumnPool pool,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntityDeltaBatch> lease = PacketFactory<EntityDeltaBatch>.Acquire();
            PopulateEntityDeltaBatch(lease.Value, serverTick, lastProcessedClientTick, selfX, selfY, columns);
            try
            {
                await SendSerializedDatagramAsync(transport, lease.Value, DeliveryMethod.UnreliableSequenced, channel: 1).ConfigureAwait(false);
            }
            finally
            {
                ClearColumns(lease.Value);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityDeltaBatch), ex);
        }
        finally
        {
            pool.Return(columns);
        }
    }

    internal static void PopulateEntityDeltaBatch(
        EntityDeltaBatch pkt,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        DeltaColumns columns)
    {
        pkt.ServerTick = serverTick;
        pkt.LastProcessedClientTick = lastProcessedClientTick;
        pkt.SelfPositionX = selfX;
        pkt.SelfPositionY = selfY;
        pkt.NetworkIds = columns.Ids;
        pkt.DirtyMasks = columns.Masks;
        pkt.PositionsX = columns.PositionsX;
        pkt.PositionsY = columns.PositionsY;
        pkt.VelocitiesX = columns.VelocitiesX;
        pkt.VelocitiesY = columns.VelocitiesY;
        pkt.CurrentHealths = columns.Healths;
    }

    // The columns go back to the pool after the send; the (possibly pooled) packet must not keep pointing at them.
    internal static void ClearColumns(EntityDeltaBatch pkt)
    {
        pkt.NetworkIds = [];
        pkt.DirtyMasks = [];
        pkt.PositionsX = [];
        pkt.PositionsY = [];
        pkt.VelocitiesX = [];
        pkt.VelocitiesY = [];
        pkt.CurrentHealths = [];
    }

    private static async Task DispatchPropChangedAsync(
        IConnection connection,
        int tileX,
        int tileY,
        uint revision,
        float durability,
        bool isDestroyed,
        ILogger logger)
    {
        try
        {
            using PacketScope<MapPropChanged> lease = PacketFactory<MapPropChanged>.Acquire();
            MapPropChanged pkt = lease.Value;
            pkt.TileX = tileX;
            pkt.TileY = tileY;
            pkt.Revision = revision;
            pkt.Durability = durability;
            pkt.IsDestroyed = isDestroyed;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(MapPropChanged), ex);
        }
    }

    /// <inheritdoc/>
    public void SendConstructionChanged(
        ReadOnlySpan<ulong> recipientConnectionIds,
        int tileX,
        int tileY,
        byte structureType,
        byte rotation,
        byte stage,
        byte size,
        float progress,
        bool isDeconstruction)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchConstructionChangedAsync(conn, tileX, tileY, structureType, rotation, stage, size, progress, isDeconstruction, _logger);
            }
        }
    }

    private static async Task DispatchConstructionChangedAsync(
        IConnection connection,
        int tileX,
        int tileY,
        byte structureType,
        byte rotation,
        byte stage,
        byte size,
        float progress,
        bool isDeconstruction,
        ILogger logger)
    {
        try
        {
            using PacketScope<MapConstructionChanged> lease = PacketFactory<MapConstructionChanged>.Acquire();
            MapConstructionChanged pkt = lease.Value;
            pkt.TileX = tileX;
            pkt.TileY = tileY;
            pkt.StructureType = structureType;
            pkt.Rotation = rotation;
            pkt.Stage = stage;
            pkt.Size = size;
            pkt.Progress = progress;
            pkt.IsDeconstruction = isDeconstruction;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(MapConstructionChanged), ex);
        }
    }

    private static async Task DispatchStructureChangedAsync(
        IConnection connection,
        int tileX,
        int tileY,
        byte structureType,
        float durability,
        byte doorState,
        byte connectivity,
        ILogger logger)
    {
        try
        {
            using PacketScope<MapStructureChanged> lease = PacketFactory<MapStructureChanged>.Acquire();
            MapStructureChanged pkt = lease.Value;
            pkt.TileX = tileX;
            pkt.TileY = tileY;
            pkt.StructureType = structureType;
            pkt.Durability = durability;
            pkt.DoorState = doorState;
            pkt.Connectivity = connectivity;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(MapStructureChanged), ex);
        }
    }

    private static async Task DispatchCombatHitAsync(
        IConnection connection,
        ulong attackerNetworkId,
        ulong defenderNetworkId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled,
        ushort statusEffectId,
        byte statusEffectLevel,
        ILogger logger)
    {
        try
        {
            using PacketScope<CombatHit> lease = PacketFactory<CombatHit>.Acquire();
            PopulateCombatHit(lease.Value, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, statusEffectId, statusEffectLevel);

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(lease.Value, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(CombatHit), ex);
        }
    }

    private static async Task DispatchCombatHitDatagramAsync(
        IDatagramTransport transport,
        ulong attackerNetworkId,
        ulong defenderNetworkId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled,
        ushort statusEffectId,
        byte statusEffectLevel,
        ILogger logger)
    {
        try
        {
            using PacketScope<CombatHit> lease = PacketFactory<CombatHit>.Acquire();
            PopulateCombatHit(lease.Value, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, statusEffectId, statusEffectLevel);
            await SendSerializedDatagramAsync(transport, lease.Value, DeliveryMethod.ReliableUnordered, channel: 2).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(CombatHit), ex);
        }
    }

    /// <summary>Fills a pooled combat hit; the status fields are reset on every use, so a recycled packet never keeps a level.</summary>
    internal static void PopulateCombatHit(
        CombatHit pkt,
        ulong attackerNetworkId,
        ulong defenderNetworkId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled,
        ushort statusEffectId,
        byte statusEffectLevel)
    {
        pkt.StatusEffectId = statusEffectId;
        pkt.StatusEffectLevel = statusEffectLevel;
        pkt.AttackerNetworkId = attackerNetworkId;
        pkt.DefenderNetworkId = defenderNetworkId;
        pkt.Damage = damage;
        pkt.RemainingHealth = remainingHealth;
        pkt.IsCritical = isCritical;
        pkt.IsKilled = isKilled;
    }

    private static async Task SendSerializedDatagramAsync(
        IDatagramTransport transport,
        IPacket packet,
        DeliveryMethod method,
        byte channel)
    {
        int length = packet.Length;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int written = packet.Serialize(buffer.AsSpan(0, length));
            await transport.SendAsync(buffer.AsMemory(0, written), method, channel, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task DispatchInventorySlotChangedAsync(
        IConnection connection,
        ushort slotIndex,
        uint itemId,
        int quantity,
        float durability,
        byte upgradeLevel,
        float freshnessSeconds,
        ILogger logger)
    {
        try
        {
            using PacketScope<InventorySlotChanged> lease = PacketFactory<InventorySlotChanged>.Acquire();
            InventorySlotChanged pkt = lease.Value;
            pkt.SlotIndex = slotIndex;
            pkt.ItemId = (ushort)itemId;
            pkt.Count = quantity;
            pkt.Durability = durability;
            pkt.UpgradeLevel = upgradeLevel;
            pkt.FreshnessSeconds = freshnessSeconds;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(InventorySlotChanged), ex);
        }
    }

    private static async Task DispatchEquipmentChangedAsync(
        IConnection connection,
        ulong entityNetworkId,
        ushort headId,
        ushort chestId,
        ushort legsId,
        ushort feetId,
        ushort weaponId,
        byte weaponUpgrade,
        ushort level,
        float maxHealth,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntityEquipmentChanged> lease = PacketFactory<EntityEquipmentChanged>.Acquire();
            EntityEquipmentChanged pkt = lease.Value;
            pkt.NetworkId = entityNetworkId;
            pkt.EquippedHeadId = headId;
            pkt.EquippedChestId = chestId;
            pkt.EquippedLegsId = legsId;
            pkt.EquippedFeetId = feetId;
            pkt.EquippedWeaponId = weaponId;
            pkt.WeaponUpgradeLevel = weaponUpgrade;
            pkt.Level = level;
            pkt.MaxHealth = maxHealth;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityEquipmentChanged), ex);
        }
    }

    private static async Task DispatchPlayerStatsSyncAsync(
        IConnection connection,
        PlayerStatsSnapshot snapshot,
        ILogger logger)
    {
        try
        {
            using PacketScope<PlayerStatsSync> lease = PacketFactory<PlayerStatsSync>.Acquire();
            PlayerStatsSync pkt = lease.Value;
            pkt.Level = snapshot.Level;
            pkt.Experience = snapshot.Experience;
            pkt.XpToNextLevel = snapshot.XpToNextLevel;
            pkt.CurrentHealth = snapshot.CurrentHealth;
            pkt.MaxHealth = snapshot.MaxHealth;
            pkt.CurrentMana = snapshot.CurrentMana;
            pkt.MaxMana = snapshot.MaxMana;
            pkt.CurrentStamina = snapshot.CurrentStamina;
            pkt.MaxStamina = snapshot.MaxStamina;
            pkt.Defense = snapshot.Defense;
            pkt.LeveledUp = snapshot.LeveledUp;
            pkt.Traits = snapshot.Traits;
            pkt.UnlockedSkillMask = snapshot.UnlockedSkillMask;
            pkt.UnspentStatPoints = snapshot.UnspentStatPoints;
            pkt.UnspentSkillPoints = snapshot.UnspentSkillPoints;
            pkt.ProgressionBlob = snapshot.ProgressionBlob;
            pkt.CooldownSkillIds = snapshot.CooldownSkillIds;
            pkt.CooldownRemainingSeconds = snapshot.CooldownRemainingSeconds;
            pkt.Food = snapshot.Needs.Food;
            pkt.MaxFood = snapshot.Needs.MaxFood;
            pkt.HungerState = snapshot.Needs.HungerState;
            pkt.Malnutrition = snapshot.Needs.Malnutrition;
            pkt.Mood = snapshot.Needs.Mood;
            pkt.FoodPoisoned = snapshot.Needs.FoodPoisoned;
            pkt.EatProgress = snapshot.Needs.EatProgress;
            pkt.Rest = snapshot.Needs.Rest;
            pkt.MaxRest = snapshot.Needs.MaxRest;
            pkt.RestState = snapshot.Needs.RestState;
            pkt.RestMode = snapshot.Needs.RestMode;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(PlayerStatsSync), ex);
        }
    }

    private static async Task DispatchAttackPhaseAsync(IConnection connection, AttackPhaseSnapshot snapshot, ILogger logger)
    {
        try
        {
            using PacketScope<EntityAttackPhase> lease = PacketFactory<EntityAttackPhase>.Acquire();
            EntityAttackPhase pkt = lease.Value;
            pkt.NetworkId = snapshot.NetworkId;
            pkt.ServerTick = snapshot.ServerTick;
            pkt.AttackKind = snapshot.AttackKind;
            pkt.Phase = snapshot.Phase;
            pkt.Shape = snapshot.Shape;
            pkt.Interrupted = snapshot.Interrupted;
            pkt.TargetX = snapshot.TargetX;
            pkt.TargetY = snapshot.TargetY;
            pkt.DirectionX = snapshot.DirectionX;
            pkt.DirectionY = snapshot.DirectionY;
            pkt.AreaRadius = snapshot.AreaRadius;
            pkt.HalfAngleRadians = snapshot.HalfAngleRadians;
            pkt.PhaseDurationTicks = snapshot.PhaseDurationTicks;
            pkt.Progress = snapshot.Progress;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityAttackPhase), ex);
        }
    }

    private static async Task DispatchSkillCastAsync(
        IConnection connection,
        ulong casterNetworkId,
        byte abilitySlot,
        float originX,
        float originY,
        float aimX,
        float aimY,
        ILogger logger)
    {
        try
        {
            using PacketScope<SkillCastBroadcast> lease = PacketFactory<SkillCastBroadcast>.Acquire();
            SkillCastBroadcast pkt = lease.Value;
            pkt.CasterNetworkId = casterNetworkId;
            pkt.AbilitySlot = abilitySlot;
            pkt.OriginX = originX;
            pkt.OriginY = originY;
            pkt.AimX = aimX;
            pkt.AimY = aimY;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(SkillCastBroadcast), ex);
        }
    }

    private static async Task DispatchWalletSyncAsync(IConnection connection, long gold, long diamonds, ulong sequence, ILogger logger)
    {
        try
        {
            using PacketScope<WalletSync> lease = PacketFactory<WalletSync>.Acquire();
            WalletSync pkt = lease.Value;
            pkt.Gold = gold;
            pkt.Diamonds = diamonds;
            pkt.Sequence = sequence;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(WalletSync), ex);
        }
    }

    private static async Task DispatchStaminaStatusAsync(IConnection connection, bool exhausted, uint refusalCount, ILogger logger)
    {
        try
        {
            using PacketScope<StaminaStatusSync> lease = PacketFactory<StaminaStatusSync>.Acquire();
            StaminaStatusSync pkt = lease.Value;
            pkt.Exhausted = exhausted;
            pkt.RefusalCount = refusalCount;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(StaminaStatusSync), ex);
        }
    }

    private static async Task DispatchPlayerDeathAsync(IConnection connection, PlayerDeathSnapshot death, ILogger logger)
    {
        try
        {
            using PacketScope<PlayerDeath> lease = PacketFactory<PlayerDeath>.Acquire();
            PlayerDeath pkt = lease.Value;
            pkt.Cause = death.Cause;
            pkt.KillerArchetypeId = death.KillerArchetypeId;
            pkt.KillerNetworkId = death.KillerNetworkId;
            pkt.GoldDropped = death.GoldDropped;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(PlayerDeath), ex);
        }
    }

    private static async Task DispatchPlayerOxygenAsync(IConnection connection, PlayerOxygenSnapshot oxygen, ILogger logger)
    {
        try
        {
            using PacketScope<PlayerOxygenSync> lease = PacketFactory<PlayerOxygenSync>.Acquire();
            PlayerOxygenSync pkt = lease.Value;
            pkt.SwimState = oxygen.SwimState;
            pkt.DepthTier = oxygen.DepthTier;
            pkt.OxygenMs = oxygen.OxygenMs;
            pkt.MaxOxygenMs = oxygen.MaxOxygenMs;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(PlayerOxygenSync), ex);
        }
    }

    private static async Task DispatchTraderStockAsync(IConnection connection, TraderStockSnapshot stock, ILogger logger)
    {
        try
        {
            using PacketScope<TraderStock> lease = PacketFactory<TraderStock>.Acquire();
            TraderStock pkt = lease.Value;
            pkt.TraderNetworkId = stock.TraderNetworkId;
            pkt.Kind = stock.Kind;
            pkt.Epoch = stock.Epoch;
            pkt.SecondsUntilRestock = stock.SecondsUntilRestock;
            pkt.ItemIds = stock.ItemIds;
            pkt.UnitPrices = stock.UnitPrices;
            pkt.Stocked = stock.Stocked;
            pkt.Remaining = stock.Remaining;
            pkt.SellItemIds = stock.SellItemIds;
            pkt.SellPrices = stock.SellPrices;
            pkt.CanRepair = stock.CanRepair;
            pkt.RepairSlots = stock.RepairSlots;
            pkt.RepairCosts = stock.RepairCosts;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(TraderStock), ex);
        }
    }

    /// <summary>Copies the owner's effect list (ADR-029 D9, with levels per ADR-036 D4) into the packet's parallel arrays.</summary>
    internal static void PopulateStatusEffectSync(StatusEffectSync pkt, ReadOnlySpan<StatusEffectEntry> entries)
    {
        ushort[] ids = new ushort[entries.Length];
        byte[] stacks = new byte[entries.Length];
        uint[] remaining = new uint[entries.Length];
        uint[] duration = new uint[entries.Length];
        float[] magnitudes = new float[entries.Length];
        byte[] sourceKinds = new byte[entries.Length];
        ushort[] sourceIds = new ushort[entries.Length];
        byte[] levels = new byte[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            StatusEffectEntry entry = entries[i];
            ids[i] = entry.EffectId;
            stacks[i] = entry.Stacks;
            remaining[i] = entry.RemainingMs;
            duration[i] = entry.DurationMs;
            magnitudes[i] = entry.Magnitude;
            sourceKinds[i] = entry.SourceKind;
            sourceIds[i] = entry.SourceId;
            levels[i] = entry.Level;
        }

        pkt.EffectIds = ids;
        pkt.Stacks = stacks;
        pkt.RemainingMs = remaining;
        pkt.DurationMs = duration;
        pkt.Magnitudes = magnitudes;
        pkt.SourceKinds = sourceKinds;
        pkt.SourceIds = sourceIds;
        pkt.Levels = levels;
    }

    private static async Task DispatchStatusEffectsAsync(IConnection connection, StatusEffectEntry[] entries, ILogger logger)
    {
        try
        {
            using PacketScope<StatusEffectSync> lease = PacketFactory<StatusEffectSync>.Acquire();
            StatusEffectSync pkt = lease.Value;
            PopulateStatusEffectSync(pkt, entries);

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(StatusEffectSync), ex);
        }
    }

    private static void LogSendFailure(ILogger logger, string packetName, Exception ex)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(ex, "Outbound push failed for {PacketType}", packetName);
        }
    }
}
