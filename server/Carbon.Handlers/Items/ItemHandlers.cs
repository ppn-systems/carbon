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
                await context.Sender.ReplyAsync(pkt).ConfigureAwait(false);
            }
        }

        using PacketScope<InventorySlotItem> termLease = PacketFactory<InventorySlotItem>.Acquire();
        InventorySlotItem terminator = termLease.Value;
        terminator.IsEndOfStream = true;
        terminator.IsTerminator = true;
        await context.Sender.ReplyAsync(terminator).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4002</c>. Updates the active hotbar tool slot (adjusting tool harvest tier).</summary>
    [PacketOpcode(ItemOpCode.SelectHotbarSlot)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleSelectHotbarSlotAsync(IPacketContext<SelectHotbarSlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TrySelectHotbarSlot(connectionId, context.Packet.SlotIndex);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.SelectHotbarSlot);
    }

    /// <summary>Opcode <c>0x4003</c>. Validates and executes a crafting recipe request.</summary>
    [PacketOpcode(ItemOpCode.CraftItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleCraftItemAsync(IPacketContext<CraftItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryCraftRecipe(
            connectionId,
            context.Packet.RecipeId,
            context.Packet.Quantity);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.CraftItem);
    }

    /// <summary>Opcode <c>0x4004</c>. Moves, swaps, merges, or equips items between slots (<c>0..39</c>).</summary>
    [PacketOpcode(ItemOpCode.MoveInventorySlot)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleMoveInventorySlotAsync(IPacketContext<MoveInventorySlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryMoveInventorySlot(
            connectionId,
            context.Packet.FromSlot,
            context.Packet.ToSlot);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.MoveInventorySlot);
    }

    /// <summary>Opcode <c>0x4005</c>. Enhances an eligible weapon, tool, or armor slot in the Forge.</summary>
    [PacketOpcode(ItemOpCode.UpgradeInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleUpgradeInventoryItemAsync(IPacketContext<UpgradeInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryUpgradeInventoryItem(
            connectionId,
            context.Packet.SlotIndex);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.UpgradeInventoryItem);
    }

    /// <summary>Opcode <c>0x4006</c>. Drops an inventory stack onto the ground as pickupable loot.</summary>
    [PacketOpcode(ItemOpCode.DropInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleDropInventoryItemAsync(IPacketContext<DropInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryDropInventoryItem(
            connectionId,
            context.Packet.SlotIndex,
            context.Packet.Quantity);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.DropInventoryItem);
    }

    /// <summary>Opcode <c>0x4007</c>. Buys vendor-sold items for gold near a friendly town NPC.</summary>
    [PacketOpcode(ItemOpCode.VendorBuyItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleVendorBuyItemAsync(IPacketContext<VendorBuyItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryVendorBuy(connectionId, context.Packet.ItemId, context.Packet.Quantity);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.VendorBuyItem);
    }

    /// <summary>Opcode <c>0x4008</c>. Sells a bag stack to a nearby friendly town NPC for gold.</summary>
    [PacketOpcode(ItemOpCode.VendorSellItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleVendorSellItemAsync(IPacketContext<VendorSellItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryVendorSell(connectionId, context.Packet.SlotIndex, context.Packet.Quantity);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.VendorSellItem);
    }

    /// <summary>Opcode <c>0x4009</c>. Repairs an item's durability for gold at a forge station.</summary>
    [PacketOpcode(ItemOpCode.RepairInventoryItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleRepairInventoryItemAsync(IPacketContext<RepairInventoryItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        OpStatus status = s_world.TryRepairItem(connectionId, context.Packet.SlotIndex);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.RepairInventoryItem);
    }
}
