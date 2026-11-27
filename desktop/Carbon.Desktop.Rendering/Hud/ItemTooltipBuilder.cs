// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
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
    private int _languageVersion = -1;

    /// <summary>Gets the lines of the last built tooltip.</summary>
    public IReadOnlyList<ItemTooltipLine> Lines => _lines;

    /// <summary>Rebuilds the lines when <paramref name="slotIndex"/> or <paramref name="slot"/> changed.</summary>
    /// <returns><see langword="true"/> when the lines were rebuilt.</returns>
    public bool Update(SyncedItemCatalog items, int slotIndex, in ItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(items);
        int languageVersion = GameText.Current.Version;
        if (slotIndex == _slotIndex && slot == _slot && languageVersion == _languageVersion && _lines.Count > 0)
        {
            return false;
        }

        _languageVersion = languageVersion;
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

        ItemRules rules = items.Rules;
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in slot);
        float mult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        _ = rules.ItemCatalog.TryGet(slot.ItemId, out ItemCatalogEntry? entry);
        ItemRarity rarity = entry?.Rarity ?? ItemRarity.Common;
        string name = entry is null ? rules.GetDisplayName(slot.ItemId) : GameText.ItemName(entry.Code, rules.GetDisplayName(slot.ItemId));
        output.Add(new ItemTooltipLine(
            upg > 0 ? GameText.Format("inventory.enhanced_name", new TextArg("name", name), new TextArg("level", upg)) : name,
            ItemRarityColors.GetTitle(rarity)));

        string category = entry is null ? GameText.Get("tooltip.category.item") : CategoryName(entry.Category);
        string tier = entry is not null && entry.Tier > 0 ? "  " + GameText.Format("tooltip.tier", new TextArg("tier", entry.Tier)) : string.Empty;
        string equipped = slotIndex >= PlayerInventoryLayout.BagLimit ? "  " + GameText.Get("tooltip.equipped") : string.Empty;
        output.Add(new ItemTooltipLine(
            GameText.Format(
                "tooltip.kind",
                new TextArg("rarity", ItemRarityColors.GetName(rarity)),
                new TextArg("category", category),
                new TextArg("tier", tier),
                new TextArg("equipped", equipped)),
            CarbonUiPalette.TextMuted));

        if (rules.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            string? stats = def.Category switch
            {
                ItemCategory.Weapon => GameText.Format("tooltip.weapon", Num("damage", def.BaseDamage * mult), Num("range", def.AttackRange)) +
                    (def.BonusMana > 0f ? "  " + GameText.Format("tooltip.bonus_mp", Num("value", def.BonusMana * mult)) : string.Empty),
                ItemCategory.Armor => GameText.Format("tooltip.armor", Num("defense", def.ArmorDefense * mult)) +
                    (def.BonusHealth > 0f ? "  " + GameText.Format("tooltip.bonus_hp", Num("value", def.BonusHealth * mult)) : string.Empty) +
                    (def.BonusMana > 0f ? "  " + GameText.Format("tooltip.bonus_mp", Num("value", def.BonusMana * mult)) : string.Empty),
                ItemCategory.Tool => GameText.Format("tooltip.tool", new TextArg("tier", def.HarvestTier), Num("damage", def.BaseDamage * mult)),
                ItemCategory.Consumable when def.HealHealth > 0f || def.RestoreMana > 0f || def.RestoreStamina > 0f =>
                    GameText.Format("tooltip.restores", Num("hp", def.HealHealth), Num("mp", def.RestoreMana), Num("st", def.RestoreStamina)),
                ItemCategory.Accessory => GameText.Get("tooltip.accessory"),
                ItemCategory.None or ItemCategory.Resource or ItemCategory.Consumable or ItemCategory.Placeable or ItemCategory.Quest or _ => null,
            };
            if (stats is not null)
            {
                output.Add(new ItemTooltipLine(stats, CarbonUiPalette.TextPrimary));
            }

            if (upg > 0)
            {
                output.Add(new ItemTooltipLine(
                    GameText.Format(
                        "tooltip.enhanced",
                        new TextArg("level", upg),
                        new TextArg("multiplier", mult.ToString("0.00", CultureInfo.InvariantCulture))),
                    CarbonUiPalette.TextWarm));
            }

            if (def.MaxDurability > 0f)
            {
                float max = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                float frac = Math.Clamp(slot.Durability / max, 0f, 1f);
                string text = frac <= 0f
                    ? GameText.Format("tooltip.broken", Num("max", max))
                    : GameText.Format("tooltip.durability", Num("value", slot.Durability), Num("max", max));
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
                ? GameText.Format(
                    "tooltip.value_stack",
                    new TextArg("price", entry.BasePrice),
                    new TextArg("total", entry.BasePrice * slot.Quantity),
                    new TextArg("count", slot.Quantity))
                : GameText.Format("tooltip.value", new TextArg("price", entry.BasePrice));
            output.Add(new ItemTooltipLine(value, CarbonUiPalette.TextWarm));
        }

        string hint = entry is null ? rules.GetActionHint(slot.ItemId) : GameText.ItemHint(entry.Code, rules.GetActionHint(slot.ItemId));
        if (!string.IsNullOrWhiteSpace(hint))
        {
            output.Add(new ItemTooltipLine(hint, CarbonUiPalette.TextMuted));
        }
    }

    private static TextArg Num(string name, float value) => new(name, (long)MathF.Round(value));

    private static string CategoryName(ItemCategory category) => category switch
    {
        ItemCategory.Resource => GameText.Get("tooltip.category.material"),
        ItemCategory.Consumable => GameText.Get("tooltip.category.consumable"),
        ItemCategory.Tool => GameText.Get("tooltip.category.tool"),
        ItemCategory.Weapon => GameText.Get("tooltip.category.weapon"),
        ItemCategory.Armor => GameText.Get("tooltip.category.armor"),
        ItemCategory.Accessory => GameText.Get("tooltip.category.accessory"),
        ItemCategory.Placeable => GameText.Get("tooltip.category.placeable"),
        ItemCategory.Quest => GameText.Get("tooltip.category.quest"),
        ItemCategory.None or _ => GameText.Get("tooltip.category.item"),
    };
}
