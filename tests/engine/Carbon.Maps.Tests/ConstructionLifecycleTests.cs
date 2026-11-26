// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using Carbon.Contracts.Common;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class ConstructionLifecycleTests
{
    private const ulong OwnerId = 1;
    private const ulong StrangerId = 2;

    private readonly BuildingGrid _grid = new();
    private readonly ConstructionLedger _ledger = new();
    private readonly FakeConstructionEnvironment _environment = new();
    private readonly BuildPermissionTable _permissions = new();
    private readonly BuildingService _service;

    public ConstructionLifecycleTests() =>
        _service = new BuildingService(_grid, _ledger, ConstructionFixtures.Catalog, _environment, _permissions);

    private static BuildActor Owner(int x = 0, int y = 0, byte toolTier = 3) => new(OwnerId, x, y, toolTier);

    private static BuildActor Stranger(int x = 0, int y = 0) => new(StrangerId, x, y, 3);

    private static FakeInventory RichInventory() => new FakeInventory()
        .With("wood_log", 100).With("wood_plank", 100).With("stone_chunk", 100).With("stone_brick", 100)
        .With("iron_ore", 100).With("iron_ingot", 100);

    [Fact]
    public void PlaceBlueprint_OnWater_ReturnsBlockedByWater()
    {
        _environment.SetBiome(1, 0, BiomeType.ShallowWater);

        BuildMutationResult result = _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North);

        result.Code.Should().Be(BuildResultCode.BlockedByWater);
        result.ToOpStatus().Should().Be(OpStatus.Invalid);
        _ledger.SiteCount.Should().Be(0, "a rejected placement must not leave a blueprint");
    }

    [Fact]
    public void PlaceBlueprint_OnCliffOrPartlyOverWater_IsRejected()
    {
        _environment.SetCliff(1, 0);
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByCliff);

        // A 2x1 workbench whose second tile is water is rejected as a whole.
        _environment.SetBiome(3, 1, BiomeType.DeepOcean);
        _service.PlaceBlueprint(Owner(), 2, 1, StructureType.Workbench, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByWater);
    }

    [Fact]
    public void PlaceBlueprint_StoneWallOnSand_ReturnsInsufficientAffordance()
    {
        _environment.SetBiome(1, 0, BiomeType.BeachSand);

        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.StoneWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.InsufficientAffordance);
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.Ok, "medium affordance sand bears wooden walls");
    }

    [Fact]
    public void PlaceBlueprint_OverlappingSiteStructurePropOrEntity_IsRejected()
    {
        _service.PlaceBlueprint(Owner(), 0, 1, StructureType.Workbench, StructureRotation.North).IsSuccess.Should().BeTrue();
        _service.PlaceBlueprint(Owner(), 1, 1, StructureType.WoodWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByConstructionSite, "the workbench footprint covers (1,1)");

        this.BuildComplete(1, -1, StructureType.WoodWall);
        _service.PlaceBlueprint(Owner(), 1, -1, StructureType.WoodChest, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByExistingStructure);

        _environment.SetProp(-1, 0);
        _service.PlaceBlueprint(Owner(), -1, 0, StructureType.WoodWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByNaturalObstacle);

        _environment.SetEntity(-1, 1, present: true);
        _service.PlaceBlueprint(Owner(), -1, 1, StructureType.WoodWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByEntity);
        _service.PlaceBlueprint(Owner(), -1, 1, StructureType.WoodFloor, StructureRotation.North).Code
            .Should().Be(BuildResultCode.Ok, "floors do not block entities");
    }

    [Fact]
    public void PlaceDeliverWorkComplete_WoodWall_ConsumesMaterialsAndRegistersOwnedStructure()
    {
        FakeInventory inventory = new FakeInventory().With("wood_log", 7);

        BuildMutationResult placed = _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North);
        placed.Stage.Should().Be(ConstructionStage.Blueprint);
        inventory.CountOf("wood_log").Should().Be(7, "blueprints consume nothing");
        _grid.TryGetStructure(1, 0, out _).Should().BeFalse("a blueprint is not a structure yet");

        _service.ApplyConstructionWork(Owner(), 1, 0, 10f).Code.Should().Be(BuildResultCode.MaterialsNotDelivered);

        BuildMutationResult delivered = _service.DeliverMaterials(Owner(), inventory, 1, 0);
        delivered.Stage.Should().Be(ConstructionStage.Frame);
        inventory.CountOf("wood_log").Should().Be(4, "a wood wall costs 3 wood logs");

        // 120 work points at 10 per tick: 11 ticks keep it a frame, the 12th completes it.
        for (int tick = 0; tick < 11; tick++)
        {
            _service.ApplyConstructionWork(Owner(), 1, 0, 10f).Stage.Should().Be(ConstructionStage.Frame);
        }

        BuildMutationResult completed = _service.ApplyConstructionWork(Owner(), 1, 0, 10f);
        completed.Stage.Should().Be(ConstructionStage.Complete);
        completed.Progress.Should().Be(1f);
        _ledger.SiteCount.Should().Be(0);
        _grid.TryGetStructure(1, 0, out StructureTile wall).Should().BeTrue();
        wall.Type.Should().Be(StructureType.WoodWall);
        wall.MaxDurability.Should().Be(150f);
        _grid.IsSolidAt(1, 0).Should().BeTrue();
        _ledger.TryGetRecord(1, 0, out StructureRecord record).Should().BeTrue();
        record.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public void DeliverMaterials_WithEmptyInventory_ReturnsInsufficientMaterialsAndPartialDeliveryStaysBlueprint()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North);

        BuildMutationResult none = _service.DeliverMaterials(Owner(), new FakeInventory(), 1, 0);
        none.Code.Should().Be(BuildResultCode.InsufficientMaterials);
        none.ToOpStatus().Should().Be(OpStatus.Conflict);

        FakeInventory partial = new FakeInventory().With("wood_log", 2);
        BuildMutationResult some = _service.DeliverMaterials(Owner(), partial, 1, 0);
        some.Code.Should().Be(BuildResultCode.Ok);
        some.Stage.Should().Be(ConstructionStage.Blueprint, "1 wood log is still missing");
        partial.CountOf("wood_log").Should().Be(0);

        _service.DeliverMaterials(Owner(), new FakeInventory().With("wood_log", 10), 1, 0).Stage
            .Should().Be(ConstructionStage.Frame);
    }

    [Fact]
    public void CancelBlueprint_AfterPartialDelivery_RefundsEveryDeliveredUnit()
    {
        this.BuildComplete(1, -1, StructureType.StoneWall);
        this.BuildComplete(1, 1, StructureType.StoneWall);
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.StoneDoor, StructureRotation.North).IsSuccess.Should().BeTrue();

        FakeInventory inventory = new FakeInventory().With("stone_chunk", 3).With("wood_log", 1);
        _service.DeliverMaterials(Owner(), inventory, 1, 0).Stage.Should().Be(ConstructionStage.Blueprint);

        FakeInventory refund = new();
        BuildMutationResult cancelled = _service.CancelBlueprint(Owner(), 1, 0, refund);

        cancelled.Stage.Should().Be(ConstructionStage.Removed);
        refund.CountOf("stone_chunk").Should().Be(3);
        refund.CountOf("wood_log").Should().Be(1);
        _ledger.SiteCount.Should().Be(0);
    }

    [Fact]
    public void PlaceBlueprint_DoorWithoutOppositeWalls_ReturnsRequiresWallsOnSides()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodDoor, StructureRotation.North).Code
            .Should().Be(BuildResultCode.RequiresWallsOnSides);

        // Wall blueprints count, so a doorway can be planned together with its walls.
        _service.PlaceBlueprint(Owner(), 0, 0, StructureType.WoodWall, StructureRotation.North);
        _service.PlaceBlueprint(Owner(), 2, 0, StructureType.WoodWall, StructureRotation.North);
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodDoor, StructureRotation.North).Code
            .Should().Be(BuildResultCode.Ok);
    }

    [Fact]
    public void ApplyDeconstructionWork_CompletedWallOfRoom_RefundsFractionAndUpdatesAutotileAndRooms()
    {
        // 3x3 ring of wood walls around the interior tile (1,1).
        for (int y = 0; y <= 2; y++)
        {
            for (int x = 0; x <= 2; x++)
            {
                if (x != 1 || y != 1)
                {
                    this.BuildComplete(x, y, StructureType.WoodWall, actorX: 1, actorY: 1);
                }
            }
        }

        _grid.IsRoofedAt(1, 1).Should().BeTrue("the interior is enclosed and supported");
        _grid.TryGetStructure(0, 0, out StructureTile corner).Should().BeTrue();
        corner.Connectivity.Should().Be(WallConnectivityMask.East | WallConnectivityMask.South);

        FakeInventory refund = new();
        BuildMutationResult progress = _service.ApplyDeconstructionWork(Owner(1, 1), 1, 0, 30f, refund);
        progress.Stage.Should().Be(ConstructionStage.Complete, "deconstruction needs 60 work (50% of 120)");
        progress.Progress.Should().BeApproximately(0.5f, 0.001f);

        BuildMutationResult removed = _service.ApplyDeconstructionWork(Owner(1, 1), 1, 0, 30f, refund);

        removed.Stage.Should().Be(ConstructionStage.Removed);
        refund.CountOf("wood_log").Should().Be(2, "75% of 3 wood logs, rounded down");
        _grid.TryGetStructure(1, 0, out _).Should().BeFalse();
        _ledger.TryGetRecord(1, 0, out _).Should().BeFalse();
        _grid.TryGetStructure(0, 0, out corner).Should().BeTrue();
        corner.Connectivity.Should().Be(WallConnectivityMask.South, "the east neighbor wall was removed");
        _grid.IsRoofedAt(1, 1).Should().BeFalse("the room is no longer enclosed");
    }

    [Fact]
    public void ApplyDeconstructionWork_ByStranger_IsDeniedUntilOwnerGrantsAccess()
    {
        this.BuildComplete(1, 0, StructureType.WoodWall);
        FakeInventory refund = new();

        BuildMutationResult denied = _service.ApplyDeconstructionWork(Stranger(), 1, 0, 1000f, refund);
        denied.Code.Should().Be(BuildResultCode.PermissionDenied);
        denied.ToOpStatus().Should().Be(OpStatus.Forbidden);
        _grid.TryGetStructure(1, 0, out _).Should().BeTrue();

        _service.CancelBlueprint(Stranger(), 1, 0, refund).Code.Should().Be(BuildResultCode.NotFound);

        _permissions.Grant(OwnerId, StrangerId);
        _service.ApplyDeconstructionWork(Stranger(), 1, 0, 1000f, refund).Stage.Should().Be(ConstructionStage.Removed);
    }

    [Fact]
    public void CancelOrDeliver_ByStrangerOnOwnersBlueprint_IsDenied()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North);

        _service.CancelBlueprint(Stranger(), 1, 0, new FakeInventory()).Code.Should().Be(BuildResultCode.PermissionDenied);
        _service.DeliverMaterials(Stranger(), RichInventory(), 1, 0).Code.Should().Be(BuildResultCode.PermissionDenied);
        _ledger.SiteCount.Should().Be(1);
    }

    [Fact]
    public void ApplyConstructionWork_OutOfReachOrLowToolTier_IsRejected()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.MetalWall, StructureRotation.North);
        _service.DeliverMaterials(Owner(), RichInventory(), 1, 0);

        _service.ApplyConstructionWork(Owner(x: 10, y: 0), 1, 0, 10f).Code.Should().Be(BuildResultCode.OutOfReach);
        _service.ApplyConstructionWork(Owner(toolTier: 1), 1, 0, 10f).Code.Should().Be(BuildResultCode.ToolTierTooLow);
        _service.PlaceBlueprint(Owner(x: 100), 5, 5, StructureType.WoodWall, StructureRotation.North).Code
            .Should().Be(BuildResultCode.OutOfReach);
    }

    [Fact]
    public void ApplyConstructionWork_EntityInsideFootprint_WaitsUntilTileIsClear()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodWall, StructureRotation.North);
        _service.DeliverMaterials(Owner(), RichInventory(), 1, 0);
        _environment.SetEntity(1, 0, present: true);

        _service.ApplyConstructionWork(Owner(), 1, 0, 500f).Code.Should().Be(BuildResultCode.BlockedByEntity);
        _grid.TryGetStructure(1, 0, out _).Should().BeFalse("walls never trap entities");

        _environment.SetEntity(1, 0, present: false);
        _service.ApplyConstructionWork(Owner(), 1, 0, 1f).Stage.Should().Be(ConstructionStage.Complete);
    }

    [Fact]
    public void ApplyDamage_UntilDestroyed_RemovesStructureAndDropsSmallerFraction()
    {
        this.BuildComplete(1, 0, StructureType.WoodChest);
        FakeInventory drops = new();

        BuildMutationResult hit = _service.ApplyDamage(1, 0, 40f, drops);
        hit.Stage.Should().Be(ConstructionStage.Complete);
        hit.Progress.Should().BeApproximately(0.5f, 0.001f);

        BuildMutationResult destroyed = _service.ApplyDamage(1, 0, 40f, drops);

        destroyed.Stage.Should().Be(ConstructionStage.Removed);
        drops.CountOf("wood_plank").Should().Be(1, "25% of 6 planks, rounded down");
        _grid.TryGetStructure(1, 0, out _).Should().BeFalse("destroyed structures do not linger as rubble");
    }

    [Fact]
    public void PlaceBlueprint_StoveRequiresFloor_AndDeconstructionRestoresTheFloor()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.Stove, StructureRotation.North).Code
            .Should().Be(BuildResultCode.RequiresFloor);

        this.BuildComplete(1, 0, StructureType.WoodFloor);
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodFloor, StructureRotation.North).Code
            .Should().Be(BuildResultCode.BlockedByExistingStructure, "floors do not stack");

        this.BuildComplete(1, 0, StructureType.Stove);
        _grid.TryGetStructure(1, 0, out StructureTile stove).Should().BeTrue();
        stove.Type.Should().Be(StructureType.Stove);
        _ledger.TryGetRecord(1, 0, out StructureRecord record).Should().BeTrue();
        record.FloorBeneath.Should().Be(StructureType.WoodFloor);

        _service.ApplyDeconstructionWork(Owner(), 1, 0, 1000f, new FakeInventory()).Stage.Should().Be(ConstructionStage.Removed);

        _grid.TryGetStructure(1, 0, out StructureTile floor).Should().BeTrue();
        floor.Type.Should().Be(StructureType.WoodFloor);
    }

    [Fact]
    public void PlaceBlueprint_RotatedWorkbench_OccupiesRotatedFootprintAndDeconstructsAsOne()
    {
        _service.PlaceBlueprint(Owner(), 1, 0, StructureType.WoodChest, StructureRotation.East).Code
            .Should().Be(BuildResultCode.RotationNotAllowed);

        this.BuildComplete(1, 0, StructureType.Workbench, StructureRotation.East);

        _grid.TryGetStructure(1, 0, out StructureTile top).Should().BeTrue();
        _grid.TryGetStructure(1, 1, out StructureTile bottom).Should().BeTrue("East rotation turns the 2x1 bench into 1x2");
        top.Type.Should().Be(StructureType.Workbench);
        bottom.Type.Should().Be(StructureType.Workbench);
        _grid.TryGetStructure(2, 0, out _).Should().BeFalse();

        FakeInventory refund = new();
        BuildMutationResult removed = _service.ApplyDeconstructionWork(Owner(), 1, 1, 1000f, refund);

        removed.Stage.Should().Be(ConstructionStage.Removed);
        (removed.AnchorX, removed.AnchorY, removed.Width, removed.Height).Should().Be((1, 0, 1, 2));
        _grid.StructureCount.Should().Be(0);
        refund.CountOf("wood_log").Should().Be(4, "75% of 6 wood logs, rounded down");
    }

    [Fact]
    public void BuildMutationResult_EveryCode_MapsToAnOpStatus()
    {
        foreach (BuildResultCode code in Enum.GetValues<BuildResultCode>())
        {
            OpStatus status = BuildMutationResult.Fail(code).ToOpStatus();
            (code == BuildResultCode.Ok).Should().Be(status == OpStatus.Ok, $"{code} maps to {status}");
        }

        Enum.GetValues<BuildResultCode>().Distinct().Count().Should().Be(Enum.GetValues<BuildResultCode>().Length);
    }

    private void BuildComplete(
        int x,
        int y,
        StructureType type,
        StructureRotation rotation = StructureRotation.North,
        int actorX = 0,
        int actorY = 0)
    {
        BuildActor actor = Owner(actorX, actorY);
        _service.PlaceBlueprint(actor, x, y, type, rotation).Code.Should().Be(BuildResultCode.Ok, $"{type} placement at ({x},{y})");
        _service.DeliverMaterials(actor, RichInventory(), x, y).Stage.Should().Be(ConstructionStage.Frame);
        _service.ApplyConstructionWork(actor, x, y, 10_000f).Stage.Should().Be(ConstructionStage.Complete);
    }
}
