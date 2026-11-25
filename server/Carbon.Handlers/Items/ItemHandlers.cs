// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Handlers.Internal;
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

        if (s_world.TryGetSessionByConnection(context.Connection.ConnectionId, out ConnectedPlayerSession? session) && session is not null)
        {
            using PacketScope<InventorySlotItem> lease = PacketFactory<InventorySlotItem>.Acquire();
            InventorySlotItem starterTool = lease.Value;
            starterTool.IsEndOfStream = false;
            starterTool.IsTerminator = false;
            starterTool.SlotIndex = session.SelectedHotbarSlot;
            starterTool.ItemId = 1;
            starterTool.Count = 1;
            starterTool.Durability = 100f;
            await context.Sender.ReplyAsync(starterTool).ConfigureAwait(false);
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

    /// <summary>Opcode <c>0x4003</c>. Validates a crafting recipe request.</summary>
    [PacketOpcode(ItemOpCode.CraftItem)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleCraftItemAsync(IPacketContext<CraftItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!s_world.TryGetSessionByConnection(context.Connection.ConnectionId, out _))
        {
            return PacketReplyHelper.SendMutationAsync(context, OpStatus.Forbidden, ItemOpCode.CraftItem);
        }

        OpStatus status = context.Packet.RecipeId > 0 && context.Packet.Quantity > 0
            ? OpStatus.NotFound
            : OpStatus.Invalid;
        return PacketReplyHelper.SendMutationAsync(context, status, ItemOpCode.CraftItem);
    }
}
