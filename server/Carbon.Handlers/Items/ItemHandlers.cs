// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Handlers.Internal;
using Carbon.Items.Containers;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;

namespace Carbon.Handlers.Items;

/// <summary>
/// Stateless packet handlers for inventory inspection, hotbar tool selection, and crafting requests.
/// </summary>
[PacketHandler("Carbon.Items")]
public static partial class ItemHandlers
{
    [Inject] private static IWorldInventoryService s_world = null!;
    [Inject] private static ISessionTransportRegistry s_transports = null!;

    /// <summary>Opcode <c>0x4001</c>. Streams active inventory slots ending in a terminator.</summary>
    [PacketOpcode(ItemOpCode.InventoryRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleInventoryRequestAsync(IPacketContext<InventoryRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while it is suspended (audit TS-4).
        IPacketSender sender = context.Sender;
        List<(ushort SlotIndex, ItemSlot Slot)> snapshot = [];
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (s_world.GetInventorySnapshot(connectionId, snapshot))
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                (ushort slotIndex, ItemSlot slot) = snapshot[i];
                using PacketScope<InventorySlotItem> lease = PacketFactory<InventorySlotItem>.Acquire();
                InventorySlotItem pkt = lease.Value;
                pkt.IsEndOfStream = false;
                pkt.IsTerminator = false;
                pkt.SlotIndex = slotIndex;
                pkt.ItemId = (ushort)slot.ItemId;
                pkt.Count = slot.Quantity;
                pkt.Durability = slot.Durability;
                pkt.UpgradeLevel = slot.UpgradeLevel;
                pkt.FreshnessSeconds = slot.FreshnessSeconds;
                await sender.ReplyAsync(pkt).ConfigureAwait(false);
            }
        }

        using PacketScope<InventorySlotItem> termLease = PacketFactory<InventorySlotItem>.Acquire();
        InventorySlotItem terminator = termLease.Value;
        terminator.IsEndOfStream = true;
        terminator.IsTerminator = true;
        await sender.ReplyAsync(terminator).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4002</c>. Updates the active hotbar tool slot (adjusting tool harvest tier).</summary>
    [PacketOpcode(ItemOpCode.SelectHotbarSlot)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleSelectHotbarSlotAsync(IPacketContext<SelectHotbarSlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitSelectHotbarSlotAsync(connectionId, context.Packet.SlotIndex).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.SelectHotbarSlot).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4003</c>. Validates and executes a crafting recipe request.</summary>
    [PacketOpcode(ItemOpCode.CraftItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCraftItemAsync(IPacketContext<CraftItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitCraftRecipeAsync(
            connectionId,
            context.Packet.RecipeId,
            context.Packet.Quantity).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.CraftItem).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4004</c>. Moves, swaps, merges, or equips items between slots (<c>0..39</c>).</summary>
    [PacketOpcode(ItemOpCode.MoveInventorySlot)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleMoveInventorySlotAsync(IPacketContext<MoveInventorySlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitMoveInventorySlotAsync(
            connectionId,
            context.Packet.FromSlot,
            context.Packet.ToSlot,
            context.Packet.ExpectedItemId).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.MoveInventorySlot).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4005</c>. Queues a forge enhancement attempt and answers with <see cref="EnhancementResult"/> (ADR-024).</summary>
    [PacketOpcode(ItemOpCode.UpgradeInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleUpgradeInventoryItemAsync(IPacketContext<UpgradeInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        ushort slotIndex = context.Packet.SlotIndex;
        WorldMutationOutcome outcome = await s_world.SubmitUpgradeInventoryItemAsync(
            connectionId,
            slotIndex,
            context.Packet.ProtectionItemId).ConfigureAwait(false);

        // The reply carries the server-rolled outcome (ADR-024); the request only names the slot and the scroll.
        ForgeEnhancementReceipt receipt = outcome.Status == OpStatus.Ok
            ? ForgeEnhancementReceipt.Unpack(outcome.Revision)
            : default;
        using PacketScope<EnhancementResult> lease = PacketFactory<EnhancementResult>.Acquire();
        EnhancementResult reply = lease.Value;
        reply.Status = outcome.Status;
        reply.ItemId = receipt.ItemId;
        reply.Outcome = receipt.Outcome;
        reply.NewLevel = receipt.NewLevel;
        reply.ProtectionConsumed = receipt.ProtectionConsumed;
        await sender.ReplyAsync(reply).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4006</c>. Drops an inventory stack onto the ground as pickupable loot.</summary>
    [PacketOpcode(ItemOpCode.DropInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleDropInventoryItemAsync(IPacketContext<DropInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitDropInventoryItemAsync(
            connectionId,
            context.Packet.SlotIndex,
            context.Packet.Quantity,
            context.Packet.ExpectedItemId).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.DropInventoryItem).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x400A</c>. Moves part of a bag stack into another bag slot (ADR-020).</summary>
    [PacketOpcode(ItemOpCode.SplitInventorySlot)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleSplitInventorySlotAsync(IPacketContext<SplitInventorySlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        SplitInventorySlot packet = context.Packet;
        WorldMutationOutcome outcome = await s_world.SubmitSplitInventorySlotAsync(
            connectionId,
            packet.FromSlot,
            packet.ToSlot,
            packet.Quantity,
            packet.ExpectedItemId).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.SplitInventorySlot).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x400B</c>. Merges and sorts the backpack on the server (ADR-020).</summary>
    [PacketOpcode(ItemOpCode.SortInventory)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleSortInventoryAsync(IPacketContext<SortInventory> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitSortInventoryAsync(connectionId).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.SortInventory).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x400C</c>. Destroys items in a slot permanently (ADR-020).</summary>
    [PacketOpcode(ItemOpCode.DestroyInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleDestroyInventoryItemAsync(IPacketContext<DestroyInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        DestroyInventoryItem packet = context.Packet;
        WorldMutationOutcome outcome = await s_world.SubmitDestroyInventoryItemAsync(
            connectionId,
            packet.SlotIndex,
            packet.Quantity,
            packet.ExpectedItemId).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.DestroyInventoryItem).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4050</c>. Queues repairing one item for materials and answers with <see cref="RepairItemResult"/> (ADR-028).</summary>
    [PacketOpcode(ItemOpCode.RepairItemRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleRepairItemRequestAsync(IPacketContext<RepairItemRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        RepairItemRequest request = context.Packet;
        ushort slotIndex = request.SlotIndex;
        WorldMutationOutcome outcome = await s_world.SubmitRepairItemAsync(
            connectionId,
            slotIndex,
            request.ExpectedItemId,
            request.Steps,
            request.Venue).ConfigureAwait(false);

        RepairReceipt receipt = RepairReceipt.Unpack(outcome.Revision);
        using PacketScope<RepairItemResult> lease = PacketFactory<RepairItemResult>.Acquire();
        RepairItemResult reply = lease.Value;
        reply.Status = outcome.Status;
        reply.Error = outcome.Status == OpStatus.Ok ? RepairError.None : receipt.Error;
        reply.Steps = receipt.Steps;
        await sender.ReplyAsync(reply).ConfigureAwait(false);
    }
}
