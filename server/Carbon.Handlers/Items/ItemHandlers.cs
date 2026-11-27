// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
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

    /// <summary>Opcode <c>0x4005</c>. Enhances an eligible weapon, tool, or armor slot in the Forge.</summary>
    [PacketOpcode(ItemOpCode.UpgradeInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleUpgradeInventoryItemAsync(IPacketContext<UpgradeInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitUpgradeInventoryItemAsync(
            connectionId,
            context.Packet.SlotIndex).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.UpgradeInventoryItem).ConfigureAwait(false);
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

    /// <summary>Opcode <c>0x4007</c>. Buys vendor-sold items for gold near a friendly town NPC.</summary>
    [PacketOpcode(ItemOpCode.VendorBuyItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleVendorBuyItemAsync(IPacketContext<VendorBuyItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitVendorBuyAsync(connectionId, context.Packet.ItemId, context.Packet.Quantity).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.VendorBuyItem).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4008</c>. Sells a bag stack to a nearby friendly town NPC for gold.</summary>
    [PacketOpcode(ItemOpCode.VendorSellItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleVendorSellItemAsync(IPacketContext<VendorSellItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitVendorSellAsync(connectionId, context.Packet.SlotIndex, context.Packet.Quantity).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.VendorSellItem).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4009</c>. Repairs an item's durability for gold at a forge station.</summary>
    [PacketOpcode(ItemOpCode.RepairInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleRepairInventoryItemAsync(IPacketContext<RepairInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        WorldMutationOutcome outcome = await s_world.SubmitRepairItemAsync(connectionId, context.Packet.SlotIndex).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.RepairInventoryItem).ConfigureAwait(false);
    }
}
