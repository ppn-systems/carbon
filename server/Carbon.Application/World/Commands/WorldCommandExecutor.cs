// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Maps.Building;

namespace Carbon.Application.World.Commands;

/// <summary>The synchronous world mutations a tick-queued <see cref="WorldCommand"/> can run (ADR-017).</summary>
internal interface IWorldCommandHandlers
{
    WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY);

    WorldMutationOutcome TryPlaceStructure(ulong connectionId, int tileX, int tileY, StructureType structureType, StructureRotation rotation);

    WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY);

    WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY);

    OpStatus TrySetResting(ulong connectionId, bool rest);

    OpStatus TrySelectHotbarSlot(ulong connectionId, byte slotIndex);

    OpStatus TryCraftRecipe(ulong connectionId, ushort recipeId, ushort quantity);

    OpStatus TryMoveInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot, ushort expectedItemId);

    WorldMutationOutcome TryEnhanceInventoryItem(ulong connectionId, ushort slotIndex, ushort protectionItemId, uint deterministicRollSeed = 0U);

    OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId);

    OpStatus TrySplitInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot, ushort quantity, ushort expectedItemId);

    OpStatus TrySortInventory(ulong connectionId);

    OpStatus TryDestroyInventoryItem(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId);

    OpStatus TryVendorBuy(ulong connectionId, ushort itemId, ushort quantity);

    OpStatus TryVendorSell(ulong connectionId, ushort slotIndex, ushort quantity);

    OpStatus TryRepairItem(ulong connectionId, ushort slotIndex);

    OpStatus TryAllocateCharacterPoint(ulong connectionId, PointAllocationKind kind, ushort targetId, byte amount);

    OpStatus TryRespawn(ulong connectionId);

    /// <summary>Gets whether the connection's player is dead and waiting to respawn (ADR-031).</summary>
    bool IsPlayerDead(ulong connectionId);
}

/// <summary>
/// Runs tick-queued world commands (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): decodes each
/// <see cref="WorldCommand"/> into its <see cref="IWorldCommandHandlers"/> call. The drain delegate is created once, so
/// draining the queue every tick does not allocate.
/// </summary>
internal sealed class WorldCommandExecutor
{
    private readonly IWorldCommandHandlers _handlers;
    private readonly Func<WorldCommand, WorldMutationOutcome> _execute;

    /// <summary>Initializes a new instance of the <see cref="WorldCommandExecutor"/> class.</summary>
    public WorldCommandExecutor(IWorldCommandHandlers handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _execute = this.Execute;
    }

    /// <summary>Runs every command queued when the tick started. Call while holding the world lock.</summary>
    public void Drain(WorldCommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        queue.Drain(_execute);
    }

    /// <summary>
    /// Runs one command; an unknown kind is <see cref="OpStatus.Invalid"/>. A dead player may only respawn: every other
    /// command is <see cref="OpStatus.Forbidden"/> (ADR-031).
    /// </summary>
    public WorldMutationOutcome Execute(WorldCommand command) =>
        command.Kind != WorldCommandKind.Respawn && _handlers.IsPlayerDead(command.ConnectionId)
            ? new WorldMutationOutcome(OpStatus.Forbidden, 0)
            : this.Run(command);

    private WorldMutationOutcome Run(WorldCommand command) => command.Kind switch
    {
        WorldCommandKind.HarvestProp => _handlers.TryHarvestProp(command.ConnectionId, command.TileX, command.TileY),
        WorldCommandKind.PlaceStructure => _handlers.TryPlaceStructure(command.ConnectionId, command.TileX, command.TileY, command.StructureType, command.Rotation),
        WorldCommandKind.DemolishStructure => _handlers.TryDemolishStructure(command.ConnectionId, command.TileX, command.TileY),
        WorldCommandKind.ToggleDoor => _handlers.TryToggleDoor(command.ConnectionId, command.TileX, command.TileY),
        WorldCommandKind.SetResting => new(_handlers.TrySetResting(command.ConnectionId, command.Arg0 != 0), 0),
        WorldCommandKind.SelectHotbarSlot => new(_handlers.TrySelectHotbarSlot(command.ConnectionId, (byte)command.Arg0), 0),
        WorldCommandKind.CraftRecipe => new(_handlers.TryCraftRecipe(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.MoveInventorySlot => new(_handlers.TryMoveInventorySlot(command.ConnectionId, command.Arg0, command.Arg1, command.Arg3), 0),
        WorldCommandKind.UpgradeInventoryItem => _handlers.TryEnhanceInventoryItem(command.ConnectionId, command.Arg0, command.Arg1),
        WorldCommandKind.DropInventoryItem => new(_handlers.TryDropInventoryItem(command.ConnectionId, command.Arg0, command.Arg2, command.Arg3), 0),
        WorldCommandKind.SplitInventorySlot => new(
            _handlers.TrySplitInventorySlot(command.ConnectionId, command.Arg0, command.Arg1, (ushort)Math.Clamp(command.Arg2, 0, ushort.MaxValue), command.Arg3),
            0),
        WorldCommandKind.SortInventory => new(_handlers.TrySortInventory(command.ConnectionId), 0),
        WorldCommandKind.DestroyInventoryItem => new(_handlers.TryDestroyInventoryItem(command.ConnectionId, command.Arg0, command.Arg2, command.Arg3), 0),
        WorldCommandKind.VendorBuy => new(_handlers.TryVendorBuy(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.VendorSell => new(_handlers.TryVendorSell(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.RepairItem => new(_handlers.TryRepairItem(command.ConnectionId, command.Arg0), 0),
        WorldCommandKind.AllocateCharacterPoint => new(
            _handlers.TryAllocateCharacterPoint(command.ConnectionId, (PointAllocationKind)command.Arg0, command.Arg1, (byte)command.Arg2),
            0),
        WorldCommandKind.Respawn => new(_handlers.TryRespawn(command.ConnectionId), 0),
        // Trading runs on TradeService's own queue (drained on the same tick, under the same lock).
        WorldCommandKind.TraderOpen or WorldCommandKind.TraderBuy or WorldCommandKind.TraderSell => new WorldMutationOutcome(OpStatus.Invalid, 0),
        _ => new WorldMutationOutcome(OpStatus.Invalid, 0),
    };
}
