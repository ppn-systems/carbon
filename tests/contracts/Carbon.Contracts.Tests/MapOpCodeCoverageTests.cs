// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Carbon.Contracts.Common;
using Carbon.Contracts.Maps;
using Carbon.Contracts.Maps.C2S;
using Carbon.Contracts.Maps.S2C;
using Nalix.Abstractions.Networking.Packets;
using Xunit;

namespace Carbon.Contracts.Tests;

public sealed class MapOpCodeCoverageTests
{
    private const ushort MapRangeStart = 0x1100;
    private const ushort MapRangeEnd = 0x11FF;

    [Fact]
    public void MapPackets_EveryPacket_DeclaresOpCodeInsideMapRange()
    {
        foreach ((Type type, ushort opCode) in PacketOpCodes(typeof(MapOpCode).Assembly))
        {
            Assert.True(
                opCode is >= MapRangeStart and <= MapRangeEnd,
                $"{type.Name} uses 0x{opCode:X4}, outside the Map band 0x{MapRangeStart:X4}-0x{MapRangeEnd:X4}.");
        }
    }

    [Fact]
    public void AllPackets_OpCodes_AreUnique()
    {
        List<(Type Type, ushort OpCode)> all =
        [
            .. PacketOpCodes(typeof(MapOpCode).Assembly),
            .. PacketOpCodes(typeof(SharedOpCode).Assembly),
        ];

        IGrouping<ushort, (Type Type, ushort OpCode)>? duplicate = all.GroupBy(p => p.OpCode).FirstOrDefault(g => g.Count() > 1);
        Assert.True(duplicate is null, $"Duplicate opcode 0x{duplicate?.Key:X4}: {string.Join(", ", duplicate?.Select(p => p.Type.Name) ?? [])}");
    }

    [Fact]
    public void MapOpCode_EveryConstant_IsUsedByExactlyOnePacket()
    {
        HashSet<ushort> used = [.. PacketOpCodes(typeof(MapOpCode).Assembly).Select(p => p.OpCode)];
        IEnumerable<ushort> constants = typeof(MapOpCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (ushort)f.GetValue(null)!);

        Assert.All(constants, c => Assert.Contains(c, used));
    }

    [Fact]
    public void MapChunkDelta_RoundTrip_PreservesArrays()
    {
        using MapChunkDelta original = new()
        {
            ChunkX = -3,
            ChunkY = 7,
            Revision = 42,
            TileIndices = [1, 1023],
            TileBiomes = [2, 5],
            TileHeightLevels = [0, 2],
            TileVariants = [3, 1],
            TileTraits = [(byte)MapTileTraits.Solid, 0],
            PropIndices = [17],
            PropDurabilities = [12.5f],
            PropDestroyed = [true],
            StructureIndices = [42, 43],
            StructureTypes = [1, 3],
            StructureDurabilities = [150f, 100f],
            StructureDoorStates = [0, 1],
            StructureConnectivities = [2, 0],
            SiteIndices = [5],
            SiteTypes = [11],
            SiteRotations = [1],
            SiteStages = [2],
            SiteSizes = [0x21],
            SiteWorkDone = [37.5f],
            SiteOwnerIds = [ulong.MaxValue],
            RecordIndices = [42],
            RecordOwnerIds = [9],
            RecordAnchorOffsets = [0x10],
            RecordRotations = [3],
            RecordFloorsBeneath = [5],
        };

        byte[] bytes = original.Serialize();
        using MapChunkDelta copy = MapChunkDelta.Deserialize(bytes);

        Assert.Equal(original.ChunkX, copy.ChunkX);
        Assert.Equal(original.ChunkY, copy.ChunkY);
        Assert.Equal(original.Revision, copy.Revision);
        Assert.Equal(original.TileIndices, copy.TileIndices);
        Assert.Equal(original.TileTraits, copy.TileTraits);
        Assert.Equal(original.PropDurabilities, copy.PropDurabilities);
        Assert.Equal(original.PropDestroyed, copy.PropDestroyed);
        Assert.Equal(original.StructureIndices, copy.StructureIndices);
        Assert.Equal(original.StructureTypes, copy.StructureTypes);
        Assert.Equal(original.StructureDurabilities, copy.StructureDurabilities);
        Assert.Equal(original.StructureDoorStates, copy.StructureDoorStates);
        Assert.Equal(original.StructureConnectivities, copy.StructureConnectivities);
        Assert.Equal(original.SiteIndices, copy.SiteIndices);
        Assert.Equal(original.SiteTypes, copy.SiteTypes);
        Assert.Equal(original.SiteRotations, copy.SiteRotations);
        Assert.Equal(original.SiteStages, copy.SiteStages);
        Assert.Equal(original.SiteSizes, copy.SiteSizes);
        Assert.Equal(original.SiteWorkDone, copy.SiteWorkDone);
        Assert.Equal(original.SiteOwnerIds, copy.SiteOwnerIds);
        Assert.Equal(original.RecordIndices, copy.RecordIndices);
        Assert.Equal(original.RecordOwnerIds, copy.RecordOwnerIds);
        Assert.Equal(original.RecordAnchorOffsets, copy.RecordAnchorOffsets);
        Assert.Equal(original.RecordRotations, copy.RecordRotations);
        Assert.Equal(original.RecordFloorsBeneath, copy.RecordFloorsBeneath);
    }

    [Fact]
    public void MapStructurePlace_RoundTrip_PreservesFields()
    {
        using MapStructurePlace original = new() { TileX = -100, TileY = 250, StructureType = 3 };

        using MapStructurePlace copy = MapStructurePlace.Deserialize(original.Serialize());

        Assert.Equal(original.TileX, copy.TileX);
        Assert.Equal(original.TileY, copy.TileY);
        Assert.Equal(original.StructureType, copy.StructureType);
    }

    [Fact]
    public void MapConstructionChanged_RoundTrip_PreservesFields()
    {
        using MapConstructionChanged original = new()
        {
            TileX = -40,
            TileY = 77,
            StructureType = 11,
            Rotation = 1,
            Stage = 2,
            Size = 0x21,
            Progress = 0.4f,
            IsDeconstruction = true,
        };

        using MapConstructionChanged copy = MapConstructionChanged.Deserialize(original.Serialize());

        Assert.Equal(original.TileX, copy.TileX);
        Assert.Equal(original.TileY, copy.TileY);
        Assert.Equal(original.StructureType, copy.StructureType);
        Assert.Equal(original.Rotation, copy.Rotation);
        Assert.Equal(original.Stage, copy.Stage);
        Assert.Equal(original.Size, copy.Size);
        Assert.Equal(original.Progress, copy.Progress);
        Assert.Equal(original.IsDeconstruction, copy.IsDeconstruction);
    }

    private static IEnumerable<(Type Type, ushort OpCode)> PacketOpCodes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPacketStaticOpcode).IsAssignableFrom(t))
            .Select(t => (t, (ushort)t.GetProperty("StaticOpCode", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!));
}
