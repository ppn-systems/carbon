// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commands;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Maps.Building;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class WorldCommandExecutorTests
{
    private const ulong Connection = 42UL;

    [Fact]
    public async Task Drain_TileAndSlotCommands_DecodeIntoTheirHandlerCallsAndCompleteWithTheOutcome()
    {
        RecordingHandlers handlers = new();
        WorldCommandExecutor executor = new(handlers);
        WorldCommandQueue queue = new();

        Task<WorldMutationOutcome> place = queue.Submit(WorldCommandKind.PlaceStructure, Connection, 3, -4, StructureType.WoodWall, StructureRotation.East).AsTask();
        Task<WorldMutationOutcome> drop = queue.Submit(WorldCommandKind.DropInventoryItem, Connection, 7, arg2: 5).AsTask();
        Task<WorldMutationOutcome> rest = queue.Submit(WorldCommandKind.SetResting, Connection, 1).AsTask();
        Task<WorldMutationOutcome> point = queue.Submit(WorldCommandKind.AllocateCharacterPoint, Connection, (ushort)PointAllocationKind.Skill, 12, 2).AsTask();

        executor.Drain(queue);

        handlers.Calls.Should().Equal(
            "PlaceStructure 42 3 -4 WoodWall East",
            "DropInventoryItem 42 7 5",
            "SetResting 42 True",
            "AllocateCharacterPoint 42 Skill 12 2");
        (await place.ConfigureAwait(true)).Should().Be(new WorldMutationOutcome(OpStatus.Ok, 9U), "tile commands report the chunk revision");
        (await drop.ConfigureAwait(true)).Should().Be(new WorldMutationOutcome(OpStatus.Ok, 0U));
        (await rest.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        (await point.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public void Drain_EveryTickWithNothingQueued_AllocatesNothing()
    {
        WorldCommandExecutor executor = new(new RecordingHandlers());
        WorldCommandQueue queue = new();
        executor.Drain(queue);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 1_000; tick++)
        {
            executor.Drain(queue);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the drain runs at the start of every fixed tick (AGENTS.md rule 4)");
    }

    [Fact]
    public void Execute_UnknownKind_IsInvalidAndCallsNothing()
    {
        RecordingHandlers handlers = new();
        WorldCommandExecutor executor = new(handlers);
        WorldCommand unknown = new((WorldCommandKind)250, Connection, 0, 0, StructureType.None, StructureRotation.North, 0, 0, 0, null!);

        executor.Execute(unknown).Should().Be(new WorldMutationOutcome(OpStatus.Invalid, 0U));
        handlers.Calls.Should().BeEmpty();
    }

    private sealed class RecordingHandlers : IWorldCommandHandlers
    {
        public List<string> Calls { get; } = [];

        public WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY) => this.Tile($"HarvestProp {connectionId} {tileX} {tileY}");

        public WorldMutationOutcome TryPlaceStructure(ulong connectionId, int tileX, int tileY, StructureType structureType, StructureRotation rotation) =>
            this.Tile($"PlaceStructure {connectionId} {tileX} {tileY} {structureType} {rotation}");

        public WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY) => this.Tile($"DemolishStructure {connectionId} {tileX} {tileY}");

        public WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY) => this.Tile($"ToggleDoor {connectionId} {tileX} {tileY}");

        public OpStatus TrySetResting(ulong connectionId, bool rest) => this.Slot($"SetResting {connectionId} {rest}");

        public OpStatus TrySelectHotbarSlot(ulong connectionId, byte slotIndex) => this.Slot($"SelectHotbarSlot {connectionId} {slotIndex}");

        public OpStatus TryCraftRecipe(ulong connectionId, ushort recipeId, ushort quantity) => this.Slot($"CraftRecipe {connectionId} {recipeId} {quantity}");

        public OpStatus TryMoveInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot) => this.Slot($"MoveInventorySlot {connectionId} {fromSlot} {toSlot}");

        public OpStatus TryUpgradeInventoryItem(ulong connectionId, ushort slotIndex) => this.Slot($"UpgradeInventoryItem {connectionId} {slotIndex}");

        public OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity) => this.Slot($"DropInventoryItem {connectionId} {slotIndex} {quantity}");

        public OpStatus TryVendorBuy(ulong connectionId, ushort itemId, ushort quantity) => this.Slot($"VendorBuy {connectionId} {itemId} {quantity}");

        public OpStatus TryVendorSell(ulong connectionId, ushort slotIndex, ushort quantity) => this.Slot($"VendorSell {connectionId} {slotIndex} {quantity}");

        public OpStatus TryRepairItem(ulong connectionId, ushort slotIndex) => this.Slot($"RepairItem {connectionId} {slotIndex}");

        public OpStatus TryAllocateCharacterPoint(ulong connectionId, PointAllocationKind kind, ushort targetId, byte amount) =>
            this.Slot($"AllocateCharacterPoint {connectionId} {kind} {targetId} {amount}");

        private WorldMutationOutcome Tile(string call)
        {
            this.Calls.Add(call);
            return new WorldMutationOutcome(OpStatus.Ok, 9U);
        }

        private OpStatus Slot(string call)
        {
            this.Calls.Add(call);
            return OpStatus.Ok;
        }
    }
}
