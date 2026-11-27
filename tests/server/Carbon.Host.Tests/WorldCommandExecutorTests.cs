// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commands;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Maps.Building;
using Carbon.Testing;
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
        Task<WorldMutationOutcome> drop = queue.Submit(WorldCommandKind.DropInventoryItem, Connection, 7, arg2: 5, arg3: 13).AsTask();
        Task<WorldMutationOutcome> split = queue.Submit(WorldCommandKind.SplitInventorySlot, Connection, 9, 10, 4, 21).AsTask();
        Task<WorldMutationOutcome> sort = queue.Submit(WorldCommandKind.SortInventory, Connection, 0).AsTask();
        Task<WorldMutationOutcome> destroy = queue.Submit(WorldCommandKind.DestroyInventoryItem, Connection, 11, arg2: -1, arg3: 22).AsTask();
        Task<WorldMutationOutcome> rest = queue.Submit(WorldCommandKind.SetResting, Connection, 1).AsTask();
        Task<WorldMutationOutcome> point = queue.Submit(WorldCommandKind.AllocateCharacterPoint, Connection, (ushort)PointAllocationKind.Skill, 12, 2).AsTask();

        executor.Drain(queue);

        handlers.Calls.Should().Equal(
            "PlaceStructure 42 3 -4 WoodWall East",
            "DropInventoryItem 42 7 5 13",
            "SplitInventorySlot 42 9 10 4 21",
            "SortInventory 42",
            "DestroyInventoryItem 42 11 -1 22",
            "SetResting 42 True",
            "AllocateCharacterPoint 42 Skill 12 2");
        (await place.ConfigureAwait(true)).Should().Be(new WorldMutationOutcome(OpStatus.Ok, 9U), "tile commands report the chunk revision");
        (await drop.ConfigureAwait(true)).Should().Be(new WorldMutationOutcome(OpStatus.Ok, 0U));
        (await split.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        (await sort.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        (await destroy.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        (await rest.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        (await point.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Drain_UpgradeCommand_PassesTheProtectionScrollAndReturnsThePackedReceipt()
    {
        RecordingHandlers handlers = new();
        WorldCommandExecutor executor = new(handlers);
        WorldCommandQueue queue = new();

        Task<WorldMutationOutcome> upgrade = queue.Submit(WorldCommandKind.UpgradeInventoryItem, Connection, (ushort)4, (ushort)206).AsTask();
        executor.Drain(queue);

        handlers.Calls.Should().Equal("UpgradeInventoryItem 42 4 206");
        (await upgrade.ConfigureAwait(true)).Should().Be(new WorldMutationOutcome(OpStatus.Ok, 0x1234_5678U), "the enhancement receipt rides in the revision (ADR-024)");
    }

    [Fact]
    public async Task Drain_RepairCommand_DecodesSlotItemStepsAndVenueAndReturnsThePackedReceipt()
    {
        RecordingHandlers handlers = new();
        WorldCommandExecutor executor = new(handlers);
        WorldCommandQueue queue = new();

        Task<WorldMutationOutcome> repair = queue.Submit(WorldCommandKind.RepairItem, Connection, 37, 3, 4, (ushort)RepairVenue.Blacksmith).AsTask();
        executor.Drain(queue);

        handlers.Calls.Should().Equal("RepairItem 42 37 3 4 Blacksmith");
        (await repair.ConfigureAwait(true)).Should().Be(new WorldMutationOutcome(OpStatus.Ok, 0x0000_0304U), "the repair receipt rides in the revision (ADR-028)");
    }

    [Fact]
    public void Drain_EveryTickWithNothingQueued_AllocatesNothing()
    {
        WorldCommandExecutor executor = new(new RecordingHandlers());
        WorldCommandQueue queue = new();
        AllocationResult window = AllocationProbe.Measure(1_000, warmupIterations: 1_000, tick => executor.Drain(queue));

        window.Bytes.Should().Be(0, $"the drain runs at the start of every fixed tick (AGENTS.md rule 4) ({window})");
    }

    [Fact]
    public async Task Drain_DeadPlayer_RefusesEveryCommandButRespawn()
    {
        RecordingHandlers handlers = new() { Dead = true };
        WorldCommandExecutor executor = new(handlers);
        WorldCommandQueue queue = new();

        Task<WorldMutationOutcome> sort = queue.Submit(WorldCommandKind.SortInventory, Connection, 0).AsTask();
        Task<WorldMutationOutcome> harvest = queue.Submit(WorldCommandKind.HarvestProp, Connection, 3, -4, StructureType.None, StructureRotation.North).AsTask();
        Task<WorldMutationOutcome> buy = queue.Submit(WorldCommandKind.CraftRecipe, Connection, 5, 1).AsTask();
        Task<WorldMutationOutcome> respawn = queue.Submit(WorldCommandKind.Respawn, Connection, 0).AsTask();
        executor.Drain(queue);

        handlers.Calls.Should().Equal(["Respawn 42"], "a dead player can do nothing but ask to respawn (ADR-031)");
        (await sort.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden);
        (await harvest.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden);
        (await buy.ConfigureAwait(true)).Status.Should().Be(OpStatus.Forbidden);
        (await respawn.ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
    }

    [Fact]
    public void Execute_UnknownKind_IsInvalidAndCallsNothing()
    {
        RecordingHandlers handlers = new();
        WorldCommandExecutor executor = new(handlers);
        WorldCommand unknown = new((WorldCommandKind)250, Connection, 0, 0, StructureType.None, StructureRotation.North, 0, 0, 0, 0, null!);

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

        public OpStatus TryMoveInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot, ushort expectedItemId) =>
            this.Slot($"MoveInventorySlot {connectionId} {fromSlot} {toSlot} {expectedItemId}");

        public WorldMutationOutcome TryEnhanceInventoryItem(ulong connectionId, ushort slotIndex, ushort protectionItemId, uint deterministicRollSeed = 0U) =>
            new(this.Slot($"UpgradeInventoryItem {connectionId} {slotIndex} {protectionItemId}"), 0x1234_5678U);

        public OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId) =>
            this.Slot($"DropInventoryItem {connectionId} {slotIndex} {quantity} {expectedItemId}");

        public OpStatus TrySplitInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot, ushort quantity, ushort expectedItemId) =>
            this.Slot($"SplitInventorySlot {connectionId} {fromSlot} {toSlot} {quantity} {expectedItemId}");

        public OpStatus TrySortInventory(ulong connectionId) => this.Slot($"SortInventory {connectionId}");

        public OpStatus TryDestroyInventoryItem(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId) =>
            this.Slot($"DestroyInventoryItem {connectionId} {slotIndex} {quantity} {expectedItemId}");

        public WorldMutationOutcome TryRepairItem(ulong connectionId, ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue) =>
            new(this.Slot($"RepairItem {connectionId} {slotIndex} {expectedItemId} {steps} {venue}"), 0x0000_0304U);

        public OpStatus TryRespawn(ulong connectionId) => this.Slot($"Respawn {connectionId}");

        public bool Dead { get; set; }

        public bool IsPlayerDead(ulong connectionId) => this.Dead;

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
