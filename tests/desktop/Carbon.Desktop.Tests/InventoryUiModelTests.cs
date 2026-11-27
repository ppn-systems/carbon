// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-020: the inventory UI model (split/confirm rules, dialog state, tooltips, rarity colours, layout).</summary>
public sealed class InventoryUiModelTests
{
    private static readonly SyncedItemCatalog s_items = new();

    [Fact]
    public void FindSplitTargetSlot_PrefersTheFirstEmptyBackpackSlotThenTheHotbar()
    {
        ItemSlot[] slots = new ItemSlot[PlayerInventoryLayout.TotalCapacity];
        for (int i = PlayerInventoryLayout.BackpackStart; i < 12; i++)
        {
            slots[i] = new ItemSlot(StarterGameCatalog.WoodLog, 5);
        }

        InventoryActionPolicy.FindSplitTargetSlot(slots, 9).Should().Be(12);

        for (int i = PlayerInventoryLayout.BackpackStart; i < PlayerInventoryLayout.BagLimit; i++)
        {
            slots[i] = new ItemSlot(StarterGameCatalog.WoodLog, 5);
        }

        slots[0] = new ItemSlot(StarterGameCatalog.WoodAxe, 1);
        InventoryActionPolicy.FindSplitTargetSlot(slots, 9).Should().Be(1, "a full backpack falls back to the hotbar");

        for (int i = 0; i < PlayerInventoryLayout.HotbarCount; i++)
        {
            slots[i] = new ItemSlot(StarterGameCatalog.WoodAxe, 1);
        }

        InventoryActionPolicy.FindSplitTargetSlot(slots, 9).Should().Be(-1);
    }

    [Theory]
    [InlineData(StarterGameCatalog.WoodLog, 0, 0, false)]
    [InlineData(StarterGameCatalog.CrystalCrown, 0, 0, true)]
    [InlineData(StarterGameCatalog.IronSword, 2, 0, true)]
    [InlineData(StarterGameCatalog.LeatherCap, 0, PlayerInventoryLayout.HeadSlot, true)]
    [InlineData(StarterGameCatalog.LeatherCap, 0, 12, false)]
    public void RequiresDropConfirmation_RareEnhancedOrEquipped_AsksFirst(uint itemId, byte upgrade, int slotIndex, bool expected)
    {
        ItemSlot slot = new(itemId, 1, UpgradeLevel: upgrade);

        InventoryActionPolicy.RequiresDropConfirmation(s_items.Rules, in slot, slotIndex).Should().Be(expected);
    }

    [Fact]
    public void TryResolveHotbarBind_HoveredItemAndNumberKey_MovesItOntoThatHotbarSlot()
    {
        ItemSlot[] slots = new ItemSlot[PlayerInventoryLayout.TotalCapacity];
        slots[20] = new ItemSlot(StarterGameCatalog.ManaTonic, 3);

        InventoryActionPolicy.TryResolveHotbarBind(slots, 20, 4, out ushort from, out ushort to).Should().BeTrue();
        from.Should().Be(20);
        to.Should().Be(4);
        InventoryActionPolicy.TryResolveHotbarBind(slots, 21, 4, out _, out _).Should().BeFalse("the hovered slot is empty");
        InventoryActionPolicy.TryResolveHotbarBind(slots, 4, 4, out _, out _).Should().BeFalse("it is already there");
        InventoryActionPolicy.TryResolveHotbarBind(slots, 20, 9, out _, out _).Should().BeFalse("there are nine hotbar slots");
    }

    [Fact]
    public void SplitDialog_OpensAtHalfAndClampsToLeaveAtLeastOne()
    {
        InventoryDialogState dialog = new();

        dialog.OpenSplit(10, new ItemSlot(StarterGameCatalog.WoodLog, 9)).Should().BeTrue();
        dialog.Quantity.Should().Be(4);

        dialog.Adjust(100);
        dialog.Quantity.Should().Be(8, "a split leaves one unit behind");
        dialog.Adjust(-100);
        dialog.Quantity.Should().Be(1);

        dialog.Close();
        dialog.OpenSplit(10, new ItemSlot(StarterGameCatalog.WoodLog, 1)).Should().BeFalse("one unit cannot be split");
        dialog.OpenSplit(PlayerInventoryLayout.HeadSlot, new ItemSlot(StarterGameCatalog.WoodLog, 9)).Should().BeFalse("splits stay in the bag");
        dialog.OpenSplit(10, new ItemSlot(StarterGameCatalog.IronSword, 2, UpgradeLevel: 1)).Should().BeFalse("enhanced items never stack");
        dialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Dialog_WhenTheSlotChangesUnderneathIt_IsStale()
    {
        InventoryDialogState dialog = new();
        ItemSlot sword = new(StarterGameCatalog.IronSword, 1, 0, 200f);
        dialog.OpenConfirmDestroy(14, in sword);

        dialog.IsStale(in sword).Should().BeFalse();
        dialog.IsStale(new ItemSlot(StarterGameCatalog.CrystalCrown, 1)).Should().BeTrue("another item moved into the slot");
        dialog.IsStale(ItemSlot.Empty).Should().BeTrue();

        dialog.OpenConfirmDrop(9, new ItemSlot(StarterGameCatalog.WoodLog, 6), 4);
        dialog.EffectiveQuantity.Should().Be(4);
        dialog.IsStale(new ItemSlot(StarterGameCatalog.WoodLog, 3)).Should().BeTrue("fewer units are left than the dialog would drop");
    }

    [Fact]
    public void ItemTooltip_EnhancedSword_ShowsRarityStatsDurabilityAndValue()
    {
        List<ItemTooltipLine> lines = [];
        ItemSlot sword = new(StarterGameCatalog.IronSword, 1, 0, 150f, UpgradeLevel: 3);

        ItemTooltipBuilder.Build(s_items, in sword, 2, lines);

        lines[0].Text.Should().Be("Iron Sword +3");
        lines[0].Color.Should().Be(ItemRarityColors.Uncommon, "the title takes the rarity colour");
        lines[1].Text.Should().StartWith("Uncommon Weapon");
        float damage = 34f * ItemEnhancementPolicy.GetStatMultiplier(3);
        lines.Select(l => l.Text).Should().Contain(t => t.StartsWith($"Damage {System.Math.Round(damage)}", System.StringComparison.Ordinal));
        lines.Select(l => l.Text).Should().Contain(t => t.StartsWith("Enhanced +3", System.StringComparison.Ordinal));
        lines.Select(l => l.Text).Should().Contain(t => t.StartsWith("Durability 150 /", System.StringComparison.Ordinal));
        lines.Select(l => l.Text).Should().Contain(t => t.StartsWith("Value ", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ItemTooltip_StackAndEquippedArmor_ShowStackValueAndEquippedTag()
    {
        List<ItemTooltipLine> lines = [];
        ItemTooltipBuilder.Build(s_items, new ItemSlot(StarterGameCatalog.WoodLog, 12), 9, lines);
        lines.Select(l => l.Text).Should().Contain(t => t.Contains("for 12", System.StringComparison.Ordinal), "a stack shows its total value");

        ItemTooltipBuilder.Build(s_items, new ItemSlot(StarterGameCatalog.LeatherTunic, 1, 0, 0f), PlayerInventoryLayout.ChestSlot, lines);
        lines[1].Text.Should().EndWith("(equipped)");
        lines.Select(l => l.Text).Should().Contain(t => t.StartsWith("Broken", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ItemTooltipBuilder_SameSlotAndItem_DoesNotRebuild()
    {
        ItemTooltipBuilder builder = new();
        ItemSlot logs = new(StarterGameCatalog.WoodLog, 12);

        builder.Update(s_items, 9, in logs).Should().BeTrue();
        builder.Update(s_items, 9, in logs).Should().BeFalse("hovering the same stack reuses the cached lines");
        builder.Update(s_items, 9, logs with { Quantity = 13 }).Should().BeTrue();
    }

    [Fact]
    public void ItemRarityColors_EveryRarity_HasItsOwnBorder()
    {
        ItemRarity[] rarities = System.Enum.GetValues<ItemRarity>();

        rarities.Select(ItemRarityColors.GetBorder).Distinct().Should().HaveCount(rarities.Length);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 600)]
    public void InventoryLayout_AllSlotsHeldAndSortButton_FitThePanelWithoutOverlapping(int width, int height)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(width, height);
        RectangleF panel = new(layout.Ox, layout.Oy, layout.PanelW, layout.PanelH);
        List<RectangleF> rects = [];
        for (int i = 0; i < PlayerInventoryLayout.TotalCapacity; i++)
        {
            rects.Add(InventoryModalWidget.GetSlotRectForTesting(in layout, i));
        }

        rects.Add(InventoryModalWidget.GetHeldSlotRectForTesting(in layout));
        rects.Add(InventoryModalWidget.GetSortButtonRectForTesting(in layout));

        for (int i = 0; i < rects.Count; i++)
        {
            RectangleF r = rects[i];
            (r.X >= panel.X && r.Y >= panel.Y && r.Right <= panel.Right && r.Bottom <= panel.Bottom).Should().BeTrue($"rect {i} must sit inside the panel");
            for (int j = i + 1; j < rects.Count; j++)
            {
                RectangleF o = rects[j];
                bool overlaps = r.X < o.Right && o.X < r.Right && r.Y < o.Bottom && o.Y < r.Bottom;
                overlaps.Should().BeFalse($"rects {i} and {j} must not overlap");
            }
        }

        RectangleF confirm = InventoryModalWidget.GetDialogButtonRectForTesting(in layout, InventoryDialogButton.Confirm);
        RectangleF cancel = InventoryModalWidget.GetDialogButtonRectForTesting(in layout, InventoryDialogButton.Cancel);
        (confirm.Right <= cancel.X).Should().BeTrue("the dialog buttons sit side by side");
    }
}
