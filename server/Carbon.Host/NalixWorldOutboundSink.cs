// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Items.S2C;
using Carbon.Contracts.Maps.S2C;
using Microsoft.Extensions.Logging;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;

namespace Carbon.Host;

/// <summary>
/// Pushes world simulation AoI updates and map edits onto live Nalix transport connections
/// using pooled packet leases and non-blocking dispatch.
/// </summary>
internal sealed class NalixWorldOutboundSink : IWorldOutboundSink
{
    private readonly ILogger<NalixWorldOutboundSink> _logger;
    private volatile IConnectionHub? _hub;

    /// <summary>
    /// Initializes a new instance of the <see cref="NalixWorldOutboundSink"/> class.
    /// </summary>
    public NalixWorldOutboundSink(ILogger<NalixWorldOutboundSink> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Binds the live connection hub created by the Nalix host builder.</summary>
    public void AttachHub(IConnectionHub hub) =>
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));

    /// <inheritdoc/>
    public void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn)
    {
        if (!this.TryResolveConnection(connectionId, out IConnection? conn) || conn is null)
        {
            return;
        }

        _ = DispatchEntitySpawnAsync(
            conn,
            spawn.NetworkId,
            spawn.Kind,
            spawn.ArchetypeId,
            spawn.Name,
            spawn.PositionX,
            spawn.PositionY,
            spawn.VelocityX,
            spawn.VelocityY,
            spawn.Radius,
            spawn.CurrentHealth,
            spawn.MaxHealth,
            _logger);
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
        if (!this.TryResolveConnection(connectionId, out IConnection? conn) || conn is null)
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

        _ = DispatchEntityDeltaBatchAsync(
            conn,
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
            if (this.TryResolveConnection(recipientConnectionIds[i], out IConnection? conn) && conn is not null)
            {
                _ = DispatchCombatHitAsync(conn, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, _logger);
            }
        }
    }

    /// <inheritdoc/>
    public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability)
    {
        if (this.TryResolveConnection(connectionId, out IConnection? conn) && conn is not null)
        {
            _ = DispatchInventorySlotChangedAsync(conn, slotIndex, itemId, quantity, durability, _logger);
        }
    }

    private bool TryResolveConnection(ulong connectionId, out IConnection? connection)
    {
        IConnectionHub? hub = _hub;
        if (hub is null)
        {
            connection = null;
            return false;
        }

        connection = hub.GetConnection(connectionId);
        return connection is not null;
    }

    private static async Task DispatchEntitySpawnAsync(
        IConnection connection,
        ulong networkId,
        EntityKind kind,
        uint archetypeId,
        string name,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        float radius,
        float currentHealth,
        float maxHealth,
        ILogger logger)
    {
        try
        {
            using PacketScope<EntitySpawn> lease = PacketFactory<EntitySpawn>.Acquire();
            EntitySpawn pkt = lease.Value;
            pkt.NetworkId = networkId;
            pkt.Kind = kind;
            pkt.ArchetypeId = (ushort)archetypeId;
            pkt.Name = name;
            pkt.PositionX = positionX;
            pkt.PositionY = positionY;
            pkt.VelocityX = velocityX;
            pkt.VelocityY = velocityY;
            pkt.Radius = radius;
            pkt.CurrentHealth = currentHealth;
            pkt.MaxHealth = maxHealth;

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
            EntityDeltaBatch pkt = lease.Value;
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

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(EntityDeltaBatch), ex);
        }
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
            CombatHit pkt = lease.Value;
            pkt.AttackerNetworkId = attackerNetworkId;
            pkt.DefenderNetworkId = defenderNetworkId;
            pkt.Damage = damage;
            pkt.RemainingHealth = remainingHealth;
            pkt.IsCritical = isCritical;
            pkt.IsKilled = isKilled;

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(CombatHit), ex);
        }
    }

    private static async Task DispatchInventorySlotChangedAsync(
        IConnection connection,
        ushort slotIndex,
        uint itemId,
        int quantity,
        float durability,
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

            bool encrypt = connection.Algorithm != CipherSuiteType.None;
            await connection.SendAsync(pkt, enableEncrypt: encrypt, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogSendFailure(logger, nameof(InventorySlotChanged), ex);
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
