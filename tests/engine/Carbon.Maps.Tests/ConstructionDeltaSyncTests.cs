// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class ConstructionDeltaSyncTests
{
    private const ulong OwnerId = 77;

    [Fact]
    public void ChunkDelta_SerializeAndImport_RestoresStructuresSitesAndOwnership()
    {
        ChunkWorld2D source = new(new ProceduralWorldGenerator(1337UL));
        BuildingService service = BuildingService.ForWorld(source, ConstructionFixtures.Catalog, new BuildPermissionTable());
        (int wallX, int wallY, int benchX, int benchY) = FindBuildableSpot(service, source);
        BuildActor actor = new(OwnerId, wallX, wallY, ToolTier: 3);

        service.PlaceBlueprint(actor, wallX, wallY, StructureType.WoodWall, StructureRotation.North).IsSuccess.Should().BeTrue();
        service.DeliverMaterials(actor, new FakeInventory().With("wood_log", 3), wallX, wallY);
        service.ApplyConstructionWork(actor, wallX, wallY, 1000f).Stage.Should().Be(ConstructionStage.Complete);
        service.PlaceBlueprint(actor, benchX, benchY, StructureType.Workbench, StructureRotation.North).IsSuccess.Should().BeTrue();
        service.DeliverMaterials(actor, new FakeInventory().With("wood_log", 3), benchX, benchY);
        service.ApplyConstructionWork(actor, benchX, benchY, 1f).Code.Should().Be(BuildResultCode.MaterialsNotDelivered);

        ChunkWorld2D restored = new(new ProceduralWorldGenerator(1337UL));
        foreach (ChunkDelta delta in source.Deltas)
        {
            restored.ImportDelta(ChunkDelta.Deserialize(delta.Serialize()));
        }

        restored.Buildings.TryGetStructure(wallX, wallY, out StructureTile wall).Should().BeTrue();
        wall.Type.Should().Be(StructureType.WoodWall);
        restored.Construction.TryGetRecord(wallX, wallY, out StructureRecord record).Should().BeTrue();
        record.OwnerId.Should().Be(OwnerId);
        restored.Construction.TryGetSiteAt(benchX + 1, benchY, out int anchorX, out int anchorY, out ConstructionSite site)
            .Should().BeTrue("the 2x1 bench blueprint reserves its second tile after import");
        (anchorX, anchorY).Should().Be((benchX, benchY));
        site.Stage.Should().Be(ConstructionStage.Blueprint);
        site.OwnerId.Should().Be(OwnerId);
        site.GetDelivered(0).Should().Be(3);

        // The restored world keeps enforcing the lifecycle: the remaining 5 planks finish the frame.
        BuildingService restoredService = BuildingService.ForWorld(restored, ConstructionFixtures.Catalog, new BuildPermissionTable());
        FakeInventory remaining = new FakeInventory().With("wood_log", 10);
        restoredService.DeliverMaterials(actor, remaining, benchX, benchY).Stage.Should().Be(ConstructionStage.Frame);
        remaining.CountOf("wood_log").Should().Be(7, "the bench costs 6 logs and 3 were delivered before the restart");
    }

    [Fact]
    public void ChunkDeltaPacketMapper_RoundTrip_PreservesSitesAndRecords()
    {
        ChunkDelta delta = new(-2, 3);
        ConstructionSite site = new(StructureType.WoodBed, StructureRotation.East, OwnerId, width: 2, height: 1, costEntries: 1);
        site.WorkDone.Should().Be(0f);
        delta.SetSite(4, 5, site);
        delta.SetStructure(10, 10, BuildingGrid.CreateBlueprint(StructureType.Stove));
        delta.SetStructureRecord(10, 10, new StructureRecord(OwnerId, 0, 0, StructureRotation.North, StructureType.StoneFloor));
        delta.SetStructureRecord(11, 10, new StructureRecord(OwnerId, 1, 0, StructureRotation.West, StructureType.None));

        using MapChunkDelta packet = ChunkDeltaPacketMapper.ToPacket(delta);
        using MapChunkDelta received = MapChunkDelta.Deserialize(packet.Serialize());
        ChunkDelta decoded = ChunkDeltaPacketMapper.FromPacket(received);

        decoded.Revision.Should().Be(delta.Revision);
        decoded.Sites.Should().ContainKey(ChunkDelta.ToLocalIndex(4, 5));
        ConstructionSite decodedSite = decoded.Sites[ChunkDelta.ToLocalIndex(4, 5)];
        decodedSite.Type.Should().Be(StructureType.WoodBed);
        decodedSite.Rotation.Should().Be(StructureRotation.East);
        (decodedSite.Width, decodedSite.Height).Should().Be((2, 1));
        decodedSite.OwnerId.Should().Be(OwnerId);
        decodedSite.Stage.Should().Be(ConstructionStage.Blueprint);
        decoded.StructureRecords.Should().BeEquivalentTo(delta.StructureRecords);
    }

    private static (int WallX, int WallY, int BenchX, int BenchY) FindBuildableSpot(BuildingService service, ChunkWorld2D world)
    {
        (int spawnX, int spawnY) = world.SpawnTile;
        for (int radius = 0; radius < 64; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = spawnX + dx;
                    int y = spawnY + dy;
                    if (service.ValidatePlacement(x, y, StructureType.WoodWall, StructureRotation.North) == BuildResultCode.Ok &&
                        service.ValidatePlacement(x + 1, y, StructureType.Workbench, StructureRotation.North) == BuildResultCode.Ok &&
                        Chunk2D.ToChunkCoord(x) == Chunk2D.ToChunkCoord(x + 2))
                    {
                        return (x, y, x + 1, y);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No buildable spot near spawn.");
    }
}
