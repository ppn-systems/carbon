// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
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

        int count = entries.Length;
        ulong[] ids = count == 0 ? [] : new ulong[count];
        byte[] masks = count == 0 ? [] : new byte[count];
        float[] px = count == 0 ? [] : new float[count];
        float[] py = count == 0 ? [] : new float[count];
        float[] vx = count == 0 ? [] : new float[count];
        float[] vy = count == 0 ? [] : new float[count];
        float[] hp = count == 0 ? [] : new float[count];

        for (int i = 0; i < count; i++)
        {
            ref readonly EntityDeltaEntry e = ref entries[i];
            ids[i] = e.NetworkId;
            masks[i] = e.DirtyMask;
            px[i] = e.PositionX;
            py[i] = e.PositionY;
            vx[i] = e.VelocityX;
            vy[i] = e.VelocityY;
            hp[i] = e.CurrentHealth;
        }

        if (dgram is not null)
        {
            _ = DispatchEntityDeltaBatchDatagramAsync(
                dgram,
                serverTick,
                lastProcessedClientTick,
                selfX,
                selfY,
                ids,
                masks,
                px,
                py,
                vx,
                vy,
                hp,
                _logger);
            return;
        }

        _ = DispatchEntityDeltaBatchAsync(
            streamConn!,
            serverTick,
            lastProcessedClientTick,
            selfX,
            selfY,
            ids,
            masks,
            px,
            py,
            vx,
            vy,
            hp,
            _logger);
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
    public void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled)
    {
        for (int i = 0; i < recipientConnectionIds.Length; i++)
        {
            ulong recipientId = recipientConnectionIds[i];
            if (_transportRegistry.TryGetDatagramConnection(recipientId, out DatagramConnection? dg) &&
                dg is not null &&
                dg.Transport is IDatagramTransport dgramTransport)
            {
                _ = DispatchCombatHitDatagramAsync(dgramTransport, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, _logger);
            }
            else if (this.TryResolveConnection(recipientId, out IConnection? conn) && conn is not null)
            {
                _ = DispatchCombatHitAsync(conn, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchInventorySlotChangedAsync(conn, slotIndex, itemId, quantity, durability, upgradeLevel, _logger);
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
        ulong[] ids,
        byte[] masks,
        float[] px,
        float[] py,
        float[] vx,
        float[] vy,
        float[] hp,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntityDeltaBatch> lease = PacketFactory<EntityDeltaBatch>.Acquire();
            PopulateEntityDeltaBatch(lease.Value, serverTick, lastProcessedClientTick, selfX, selfY, ids, masks, px, py, vx, vy, hp);

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(lease.Value, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityDeltaBatch), ex);
        }
    }

    private static async Task DispatchEntityDeltaBatchDatagramAsync(
        IDatagramTransport transport,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        ulong[] ids,
        byte[] masks,
        float[] px,
        float[] py,
        float[] vx,
        float[] vy,
        float[] hp,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntityDeltaBatch> lease = PacketFactory<EntityDeltaBatch>.Acquire();
            PopulateEntityDeltaBatch(lease.Value, serverTick, lastProcessedClientTick, selfX, selfY, ids, masks, px, py, vx, vy, hp);
            await SendSerializedDatagramAsync(transport, lease.Value, DeliveryMethod.UnreliableSequenced, channel: 1).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityDeltaBatch), ex);
        }
    }

    private static void PopulateEntityDeltaBatch(
        EntityDeltaBatch pkt,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        ulong[] ids,
        byte[] masks,
        float[] px,
        float[] py,
        float[] vx,
        float[] vy,
        float[] hp)
    {
        pkt.ServerTick = serverTick;
        pkt.LastProcessedClientTick = lastProcessedClientTick;
        pkt.SelfPositionX = selfX;
        pkt.SelfPositionY = selfY;
        pkt.NetworkIds = ids;
        pkt.DirtyMasks = masks;
        pkt.PositionsX = px;
        pkt.PositionsY = py;
        pkt.VelocitiesX = vx;
        pkt.VelocitiesY = vy;
        pkt.CurrentHealths = hp;
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
        ILogger logger)
    {
        try
        {
            using PacketScope<CombatHit> lease = PacketFactory<CombatHit>.Acquire();
            PopulateCombatHit(lease.Value, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled);

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
        ILogger logger)
    {
        try
        {
            using PacketScope<CombatHit> lease = PacketFactory<CombatHit>.Acquire();
            PopulateCombatHit(lease.Value, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled);
            await SendSerializedDatagramAsync(transport, lease.Value, DeliveryMethod.ReliableUnordered, channel: 2).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(CombatHit), ex);
        }
    }

    private static void PopulateCombatHit(
        CombatHit pkt,
        ulong attackerNetworkId,
        ulong defenderNetworkId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled)
    {
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

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(PlayerStatsSync), ex);
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

    private static void LogSendFailure(ILogger logger, string packetName, Exception ex)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(ex, "Outbound push failed for {PacketType}", packetName);
        }
    }
}
