// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Survival.Food;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Builds the inventory tooltip for one slot: name with <c>+N</c> in its rarity colour, rarity and category, stats
/// scaled by enhancement, durability, freshness, value and the item hint. Lines are cached until the hovered slot or
/// its contents change, so hovering does not allocate every frame.
/// </summary>
internal sealed class ItemTooltipBuilder
{
    private readonly List<ItemTooltipLine> _lines = new(10);
    private int _slotIndex = -1;
    private ItemSlot _slot;

    /// <summary>Gets the lines of the last built tooltip.</summary>
    public IReadOnlyList<ItemTooltipLine> Lines => _lines;

    /// <summary>Rebuilds the lines when <paramref name="slotIndex"/> or <paramref name="slot"/> changed.</summary>
    /// <returns><see langword="true"/> when the lines were rebuilt.</returns>
    public bool Update(SyncedItemCatalog items, int slotIndex, in ItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (slotIndex == _slotIndex && slot == _slot && _lines.Count > 0)
        {
            return false;
        }

        _slotIndex = slotIndex;
        _slot = slot;
        Build(items, in slot, slotIndex, _lines);
        return true;
    }

    /// <summary>Builds the tooltip lines for <paramref name="slot"/> into <paramref name="output"/>.</summary>
    public static void Build(SyncedItemCatalog items, in ItemSlot slot, int slotIndex, List<ItemTooltipLine> output)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (slot.IsEmpty)
        {
            return;
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        ItemRules rules = items.Rules;
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in slot);
        float mult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        _ = rules.ItemCatalog.TryGet(slot.ItemId, out ItemCatalogEntry? entry);
        ItemRarity rarity = entry?.Rarity ?? ItemRarity.Common;
        string name = rules.GetDisplayName(slot.ItemId);
        output.Add(new ItemTooltipLine(upg > 0 ? $"{name} +{upg}" : name, ItemRarityColors.GetTitle(rarity)));

        string category = entry is null ? "Item" : CategoryName(entry.Category);
        string tier = entry is not null && entry.Tier > 0 ? string.Create(inv, $"  Tier {entry.Tier}") : string.Empty;
        string equipped = slotIndex >= PlayerInventoryLayout.BagLimit ? "  (equipped)" : string.Empty;
        output.Add(new ItemTooltipLine($"{ItemRarityColors.GetName(rarity)} {category}{tier}{equipped}", CarbonUiPalette.TextMuted));

        if (rules.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            string? stats = def.Category switch
            {
                ItemCategory.Weapon => string.Create(inv, $"Damage {Math.Round(def.BaseDamage * mult)}  Range {Math.Round(def.AttackRange)}") +
                    (def.BonusMana > 0f ? string.Create(inv, $"  +{Math.Round(def.BonusMana * mult)} MP") : string.Empty),
                ItemCategory.Armor => string.Create(inv, $"Defense {Math.Round(def.ArmorDefense * mult)}") +
                    (def.BonusHealth > 0f ? string.Create(inv, $"  +{Math.Round(def.BonusHealth * mult)} HP") : string.Empty) +
                    (def.BonusMana > 0f ? string.Create(inv, $"  +{Math.Round(def.BonusMana * mult)} MP") : string.Empty),
                ItemCategory.Tool => string.Create(inv, $"Harvest tier {def.HarvestTier}  Damage {Math.Round(def.BaseDamage * mult)}"),
                ItemCategory.Consumable when def.HealHealth > 0f || def.RestoreMana > 0f || def.RestoreStamina > 0f =>
                    string.Create(inv, $"Restores {Math.Round(def.HealHealth)} HP  {Math.Round(def.RestoreMana)} MP  {Math.Round(def.RestoreStamina)} ST"),
                ItemCategory.Accessory => "Ring or amulet slot",
                ItemCategory.None or ItemCategory.Resource or ItemCategory.Consumable or ItemCategory.Placeable or ItemCategory.Quest or _ => null,
            };
            if (stats is not null)
            {
                output.Add(new ItemTooltipLine(stats, CarbonUiPalette.TextPrimary));
            }

            if (upg > 0)
            {
                ref readonly EnhancementTierStyle tierStyle = ref EnhancementTierStyles.Get(upg);
                output.Add(new ItemTooltipLine(string.Create(inv, $"Enhanced +{upg} ({tierStyle.Name}): stats x{mult:0.00}"), tierStyle.Accent));
            }

            if (def.MaxDurability > 0f)
            {
                float max = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                float frac = Math.Clamp(slot.Durability / max, 0f, 1f);
                string text = frac <= 0f
                    ? string.Create(inv, $"Broken  0 / {Math.Round(max)}  (repair at a forge)")
                    : string.Create(inv, $"Durability {Math.Round(slot.Durability)} / {Math.Round(max)}");
                output.Add(new ItemTooltipLine(text, frac <= 0f ? CarbonUiPalette.DangerBorder : frac <= 0.35f ? CarbonUiPalette.XpFill : CarbonUiPalette.TextMuted));
            }
        }

        if (SyncedFoodCatalog.TryGet(items, slot.ItemId, out FoodDefinition? food))
        {
            output.Add(new ItemTooltipLine(SyncedFoodCatalog.FormatTooltip(food, slot.FreshnessSeconds), CarbonUiPalette.StaminaFill));
        }

        if (entry is not null && entry.BasePrice > 0)
        {
            string value = slot.Quantity > 1
                ? string.Create(inv, $"Value {entry.BasePrice} g each  ({entry.BasePrice * slot.Quantity} g for {slot.Quantity})")
                : string.Create(inv, $"Value {entry.BasePrice} g");
            output.Add(new ItemTooltipLine(value, CarbonUiPalette.TextWarm));
        }

        string hint = rules.GetActionHint(slot.ItemId);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            output.Add(new ItemTooltipLine(hint, CarbonUiPalette.TextMuted));
        }
    }

    private static string CategoryName(ItemCategory category) => category switch
    {
        ItemCategory.Resource => "Material",
        ItemCategory.Consumable => "Consumable",
        ItemCategory.Tool => "Tool",
        ItemCategory.Weapon => "Weapon",
        ItemCategory.Armor => "Armor",
        ItemCategory.Accessory => "Accessory",
        ItemCategory.Placeable => "Placeable",
        ItemCategory.Quest => "Quest item",
        ItemCategory.None or _ => "Item",
    };
}
