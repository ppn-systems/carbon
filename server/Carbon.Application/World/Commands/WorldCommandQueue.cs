// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Maps.Building;

namespace Carbon.Application.World.Commands;

/// <summary>Kind of a tick-queued world command (ADR-017).</summary>
internal enum WorldCommandKind : byte
{
    /// <summary>Harvest or damage the prop on a tile.</summary>
    HarvestProp = 1,

    /// <summary>Place a construction blueprint.</summary>
    PlaceStructure = 2,

    /// <summary>Cancel a blueprint or start deconstructing a structure.</summary>
    DemolishStructure = 3,

    /// <summary>Open or close a door.</summary>
    ToggleDoor = 4,

    /// <summary>Start resting (<c>Arg0</c> = 1) or stand up (<c>Arg0</c> = 0) (ADR-018).</summary>
    SetResting = 5,

    /// <summary>Select the active hotbar slot (<c>Arg0</c> = slot).</summary>
    SelectHotbarSlot = 10,

    /// <summary>Craft a recipe (<c>Arg0</c> = recipe id, <c>Arg1</c> = quantity).</summary>
    CraftRecipe = 11,

    /// <summary>Move, swap, merge or equip a slot (<c>Arg0</c> = from, <c>Arg1</c> = to, <c>Arg3</c> = expected item id).</summary>
    MoveInventorySlot = 12,

    /// <summary>Enhance the item in a slot (<c>Arg0</c> = slot).</summary>
    UpgradeInventoryItem = 13,

    /// <summary>Drop items from a slot (<c>Arg0</c> = slot, <c>Arg2</c> = quantity, <c>Arg3</c> = expected item id).</summary>
    DropInventoryItem = 14,

    /// <summary>Split part of a bag stack (<c>Arg0</c> = from, <c>Arg1</c> = to, <c>Arg2</c> = quantity, <c>Arg3</c> = expected item id; ADR-020).</summary>
    SplitInventorySlot = 15,

    /// <summary>Merge and sort the backpack (ADR-020).</summary>
    SortInventory = 16,

    /// <summary>Destroy items in a slot (<c>Arg0</c> = slot, <c>Arg2</c> = quantity, <c>Arg3</c> = expected item id; ADR-020).</summary>
    DestroyInventoryItem = 17,

    // 20 and 21 were VendorBuy / VendorSell (ADR-038).

    /// <summary>Repair one item (ADR-028): <c>Arg0</c> = slot, <c>Arg1</c> = expected item, <c>Arg2</c> = steps, <c>Arg3</c> = venue.</summary>
    RepairItem = 22,

    /// <summary>Open a trader's window (ADR-025; trader network id packed in TileX/TileY).</summary>
    TraderOpen = 40,

    // 41 and 42 were the single-line TraderBuy / TraderSell (ADR-038).

    /// <summary>Sell a whole basket to a trader, all or nothing (ADR-025 D18; lines in <see cref="WorldCommand.Payload"/>).</summary>
    TraderSellBatch = 43,

    /// <summary>Buy a whole cart from a trader, all or nothing (ADR-025 D18; lines in <see cref="WorldCommand.Payload"/>).</summary>
    TraderBuyBatch = 44,

    /// <summary>Allocate character points (<c>Arg0</c> = <c>PointAllocationKind</c>, <c>Arg1</c> = target id, <c>Arg2</c> = amount).</summary>
    AllocateCharacterPoint = 30,

    /// <summary>Respawn a dead player at the sanctuary (ADR-031).</summary>
    Respawn = 31,
}

/// <summary>
/// A mutating request waiting for the next tick, with the completion its handler awaits. <see cref="Payload"/> carries
/// the lines of a variable-length request (a trader basket); fixed-size requests leave it <see langword="null"/>.
/// </summary>
internal readonly record struct WorldCommand(
    WorldCommandKind Kind,
    ulong ConnectionId,
    int TileX,
    int TileY,
    StructureType StructureType,
    StructureRotation Rotation,
    ushort Arg0,
    ushort Arg1,
    int Arg2,
    ushort Arg3,
    TaskCompletionSource<WorldMutationOutcome> Completion,
    object? Payload = null);

/// <summary>
/// Lock-free queue of mutating requests submitted from network I/O threads and drained at the start of each fixed
/// tick (ADR-017 D1, D2, D4). Submitting never takes the world lock.
/// </summary>
internal sealed class WorldCommandQueue
{
    /// <summary>Maximum queued commands per connection; further submissions are rejected (ADR-017 D4).</summary>
    public const int MaxPendingPerConnection = 16;

    private readonly ConcurrentQueue<WorldCommand> _queue = new();
    private readonly ConcurrentDictionary<ulong, int> _pendingByConnection = new();

    /// <summary>Gets the number of queued commands.</summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Queues a command and returns a task that completes once a tick has processed it, or immediately with
    /// <see cref="OpStatus.RateLimited"/> when the connection already has <see cref="MaxPendingPerConnection"/> queued.
    /// </summary>
    public ValueTask<WorldMutationOutcome> SubmitAsync(
        WorldCommandKind kind,
        ulong connectionId,
        int tileX,
        int tileY,
        StructureType structureType = StructureType.None,
        StructureRotation rotation = StructureRotation.North) =>
        this.EnqueueAsync(new WorldCommand(kind, connectionId, tileX, tileY, structureType, rotation, 0, 0, 0, 0, null!));

    /// <summary>Queues a slot-based command (inventory, crafting); see <see cref="WorldCommandKind"/> for the arguments.</summary>
    public ValueTask<WorldMutationOutcome> SubmitAsync(WorldCommandKind kind, ulong connectionId, ushort arg0, ushort arg1 = 0, int arg2 = 0, ushort arg3 = 0) =>
        this.EnqueueAsync(new WorldCommand(kind, connectionId, 0, 0, StructureType.None, StructureRotation.North, arg0, arg1, arg2, arg3, null!));

    /// <summary>Queues a command carrying a 64-bit target (split over TileX/TileY) and three arguments.</summary>
    public ValueTask<WorldMutationOutcome> SubmitTargetedAsync(WorldCommandKind kind, ulong connectionId, ulong target, ushort arg0, ushort arg1, int arg2) =>
        this.EnqueueAsync(new WorldCommand(kind, connectionId, (int)(uint)target, (int)(uint)(target >> 32), StructureType.None, StructureRotation.North, arg0, arg1, arg2, 0, null!));

    /// <summary>Queues a command carrying a 64-bit target and a variable-length <paramref name="payload"/> (normal path, not the tick).</summary>
    public ValueTask<WorldMutationOutcome> SubmitTargetedAsync(WorldCommandKind kind, ulong connectionId, ulong target, object payload) =>
        this.EnqueueAsync(new WorldCommand(kind, connectionId, (int)(uint)target, (int)(uint)(target >> 32), StructureType.None, StructureRotation.North, 0, 0, 0, 0, null!, payload));

    private ValueTask<WorldMutationOutcome> EnqueueAsync(WorldCommand command)
    {
        ulong connectionId = command.ConnectionId;
        int pending = _pendingByConnection.AddOrUpdate(connectionId, 1, static (_, n) => n + 1);
        if (pending > MaxPendingPerConnection)
        {
            this.ReleaseSlot(connectionId);
            return ValueTask.FromResult(new WorldMutationOutcome(OpStatus.RateLimited, 0));
        }

        TaskCompletionSource<WorldMutationOutcome> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(command with { Completion = completion });
        return new ValueTask<WorldMutationOutcome>(completion.Task);
    }

    /// <summary>
    /// Runs every queued command through <paramref name="execute"/> in arrival order and completes its task. Called by
    /// the tick while it holds the world lock; handler continuations run asynchronously, outside that lock.
    /// </summary>
    public void Drain(Func<WorldCommand, WorldMutationOutcome> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        // Only drain what was queued when the tick started, so a flood of submissions cannot starve the tick.
        int budget = _queue.Count;
        for (int i = 0; i < budget && _queue.TryDequeue(out WorldCommand command); i++)
        {
            this.ReleaseSlot(command.ConnectionId);
            try
            {
                _ = command.Completion.TrySetResult(execute(command));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A failing command faults only its own handler; the tick and the other commands carry on.
                _ = command.Completion.TrySetException(ex);
            }
        }
    }

    /// <summary>Completes every queued command with <paramref name="status"/> without running it (shutdown, ADR-017 D4).</summary>
    public void FailAll(OpStatus status)
    {
        while (_queue.TryDequeue(out WorldCommand command))
        {
            this.ReleaseSlot(command.ConnectionId);
            _ = command.Completion.TrySetResult(new WorldMutationOutcome(status, 0));
        }
    }

    private void ReleaseSlot(ulong connectionId)
    {
        while (_pendingByConnection.TryGetValue(connectionId, out int current))
        {
            if (current <= 1)
            {
                if (_pendingByConnection.TryRemove(new System.Collections.Generic.KeyValuePair<ulong, int>(connectionId, current)))
                {
                    return;
                }
            }
            else if (_pendingByConnection.TryUpdate(connectionId, current - 1, current))
            {
                return;
            }
        }
    }
}
