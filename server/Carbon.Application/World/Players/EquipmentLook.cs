// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Abstractions.World;
using Carbon.Items.Containers;
using Carbon.Items.Catalog;

namespace Carbon.Application.World.Players;

/// <summary>What other players see a player wear and hold, as replicated by <c>EntityEquipmentChanged</c>.</summary>
internal readonly record struct EquipmentLook(
    ulong NetworkId,
    ushort HeadId,
    ushort ChestId,
    ushort LegsId,
    ushort FeetId,
    ushort WeaponId,
    byte WeaponUpgrade,
    ushort Level,
    float MaxHealth)
{
    /// <summary>Captures the current look of <paramref name="state"/>: armor slots, held hotbar item, level, max health.</summary>
    public static EquipmentLook Of(PlayerState state)
    {
        InventoryContainer inventory = state.Inventory;
        ItemSlot held = state.Session.SelectedHotbarSlot < inventory.Capacity
            ? inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        return new EquipmentLook(
            state.Session.PlayerNetworkId,
            (ushort)inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId,
            (ushort)inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId,
            (ushort)inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId,
            (ushort)inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId,
            (ushort)held.ItemId,
            ItemEnhancementPolicy.GetUpgradeLevel(in held),
            state.Level,
            state.Stats.MaxHealth);
    }

    /// <summary>Sends this look to <paramref name="recipients"/>.</summary>
    public void SendTo(IWorldOutboundSink sink, ulong[] recipients) =>
        sink.SendEntityEquipmentChanged(
            recipients,
            this.NetworkId,
            this.HeadId,
            this.ChestId,
            this.LegsId,
            this.FeetId,
            this.WeaponId,
            this.WeaponUpgrade,
            this.Level,
            this.MaxHealth);
}
