// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Maps;
using Carbon.Contracts.Maps.C2S;
using Carbon.Contracts.Maps.S2C;
using Carbon.Handlers.Internal;
using Carbon.Maps.Building;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;

namespace Carbon.Handlers.Maps;

/// <summary>
/// Stateless packet handlers for world info, chunk delta streaming, harvesting, and construction.
/// </summary>
[PacketHandler("Carbon.Maps")]
public static partial class MapHandlers
{
    [Inject] private static IWorldMapService s_world = null!;
    [Inject] private static ISessionTransportRegistry s_transports = null!;

    /// <summary>Opcode <c>0x1101</c>. Returns deterministic world seed, version, and starter spawn coordinates.</summary>
    [PacketOpcode(MapOpCode.WorldInfoRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleWorldInfoAsync(IPacketContext<MapWorldInfoRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Vector2 spawn = s_world.DefaultSpawnWorldPosition;
        PacketScope<MapWorldInfo> lease = PacketFactory<MapWorldInfo>.Acquire();
        try
        {
            MapWorldInfo reply = lease.Value;
            reply.Seed = s_world.WorldSeed;
            reply.GeneratorVersion = s_world.GeneratorVersion;
            reply.TileSize = s_world.TileSize;
            reply.Frequency = s_world.Frequency;
            reply.SpawnTileX = (int)MathF.Floor(spawn.X / s_world.TileSize);
            reply.SpawnTileY = (int)MathF.Floor(spawn.Y / s_world.TileSize);
            return context.Sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Opcode <c>0x1102</c>. Streams chunk delta state when newer than the client's cached revision.</summary>
    [PacketOpcode(MapOpCode.ChunkRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleChunkRequestAsync(IPacketContext<MapChunkRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while it is suspended (audit TS-4).
        IPacketSender sender = context.Sender;
        MapChunkRequest req = context.Packet;
        int chunkX = req.ChunkX;
        int chunkY = req.ChunkY;

        MapChunkDelta? snapshot = null;
        try
        {
            if (s_world.TryCreateChunkDeltaPacket(chunkX, chunkY, req.KnownRevision, out snapshot) && snapshot is not null)
            {
                using PacketScope<MapChunkDelta> lease = PacketFactory<MapChunkDelta>.Acquire();
                MapChunkDelta row = lease.Value;
                row.IsEndOfStream = false;
                row.IsTerminator = false;
                row.ChunkX = snapshot.ChunkX;
                row.ChunkY = snapshot.ChunkY;
                row.Revision = snapshot.Revision;
                row.TileIndices = snapshot.TileIndices;
                row.TileBiomes = snapshot.TileBiomes;
                row.TileHeightLevels = snapshot.TileHeightLevels;
                row.TileVariants = snapshot.TileVariants;
                row.TileTraits = snapshot.TileTraits;
                row.PropIndices = snapshot.PropIndices;
                row.PropDurabilities = snapshot.PropDurabilities;
                row.PropDestroyed = snapshot.PropDestroyed;
                row.StructureIndices = snapshot.StructureIndices;
                row.StructureTypes = snapshot.StructureTypes;
                row.StructureDurabilities = snapshot.StructureDurabilities;
                row.StructureDoorStates = snapshot.StructureDoorStates;
                row.StructureConnectivities = snapshot.StructureConnectivities;
                row.SiteIndices = snapshot.SiteIndices;
                row.SiteTypes = snapshot.SiteTypes;
                row.SiteRotations = snapshot.SiteRotations;
                row.SiteStages = snapshot.SiteStages;
                row.SiteSizes = snapshot.SiteSizes;
                row.SiteWorkDone = snapshot.SiteWorkDone;
                row.SiteOwnerIds = snapshot.SiteOwnerIds;
                row.RecordIndices = snapshot.RecordIndices;
                row.RecordOwnerIds = snapshot.RecordOwnerIds;
                row.RecordAnchorOffsets = snapshot.RecordAnchorOffsets;
                row.RecordRotations = snapshot.RecordRotations;
                row.RecordFloorsBeneath = snapshot.RecordFloorsBeneath;
                await sender.ReplyAsync(row).ConfigureAwait(false);
            }
        }
        finally
        {
            snapshot?.Dispose();
        }

        using PacketScope<MapChunkDelta> termLease = PacketFactory<MapChunkDelta>.Acquire();
        MapChunkDelta terminator = termLease.Value;
        terminator.IsEndOfStream = true;
        terminator.IsTerminator = true;
        terminator.ChunkX = chunkX;
        terminator.ChunkY = chunkY;
        await sender.ReplyAsync(terminator).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x1103</c>. Applies tool-tiered harvesting damage to a natural prop within range.</summary>
    [PacketOpcode(MapOpCode.PropHarvest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandlePropHarvestAsync(IPacketContext<MapPropHarvest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MapPropHarvest req = context.Packet;
        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitHarvestPropAsync(connectionId, req.TileX, req.TileY).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, MapOpCode.PropHarvest, outcome.Revision).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x1104</c>. Places a player structure on a valid walkable tile within range.</summary>
    [PacketOpcode(MapOpCode.StructurePlace)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleStructurePlaceAsync(IPacketContext<MapStructurePlace> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MapStructurePlace req = context.Packet;
        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitPlaceStructureAsync(
            connectionId,
            req.TileX,
            req.TileY,
            (StructureType)req.StructureType,
            (StructureRotation)(req.Rotation & 0x03)).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, MapOpCode.StructurePlace, outcome.Revision).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x1105</c>. Demolishes an existing constructed structure within range.</summary>
    [PacketOpcode(MapOpCode.StructureDemolish)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleStructureDemolishAsync(IPacketContext<MapStructureDemolish> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MapStructureDemolish req = context.Packet;
        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitDemolishStructureAsync(connectionId, req.TileX, req.TileY).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, MapOpCode.StructureDemolish, outcome.Revision).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x1106</c>. Toggles a constructed door open or closed within range.</summary>
    [PacketOpcode(MapOpCode.DoorToggle)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleDoorToggleAsync(IPacketContext<MapDoorToggle> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MapDoorToggle req = context.Packet;
        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitToggleDoorAsync(connectionId, req.TileX, req.TileY).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, MapOpCode.DoorToggle, outcome.Revision).ConfigureAwait(false);
    }
}
