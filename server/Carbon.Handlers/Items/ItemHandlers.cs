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
    [Inject] private static IAuthoritativeWorld s_world = null!;

    /// <summary>Opcode <c>0x4001</c>. Streams active inventory slots ending in a terminator.</summary>
    [PacketOpcode(ItemOpCode.InventoryRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleInventoryRequestAsync(IPacketContext<InventoryRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<(ushort SlotIndex, ItemSlot Slot)> snapshot = [];
        if (s_world.GetInventorySnapshot(context.Connection.ConnectionId, snapshot))
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

        OpStatus status = s_world.TrySelectHotbarSlot(context.Connection.ConnectionId, context.Packet.SlotIndex);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.SelectHotbarSlot);
    }

    /// <summary>Opcode <c>0x4003</c>. Validates and executes a crafting recipe request.</summary>
    [PacketOpcode(ItemOpCode.CraftItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleCraftItemAsync(IPacketContext<CraftItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        OpStatus status = s_world.TryCraftRecipe(
            context.Connection.ConnectionId,
            context.Packet.RecipeId,
            context.Packet.Quantity);
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.CraftItem);
    }
}
