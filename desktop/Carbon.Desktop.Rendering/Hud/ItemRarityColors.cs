// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Rarity colours for slot borders and tooltip titles (common keeps the neutral UI border).</summary>
internal static class ItemRarityColors
{
    public static readonly ColorRgba Uncommon = new(92, 184, 104, 255);
    public static readonly ColorRgba Rare = new(76, 146, 230, 255);
    public static readonly ColorRgba Epic = new(176, 104, 226, 255);
    public static readonly ColorRgba Legendary = new(238, 160, 52, 255);

    /// <summary>Gets the border colour of a slot holding an item of <paramref name="rarity"/>.</summary>
    public static ColorRgba GetBorder(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Uncommon => Uncommon,
        ItemRarity.Rare => Rare,
        ItemRarity.Epic => Epic,
        ItemRarity.Legendary => Legendary,
        ItemRarity.Common or _ => CarbonUiPalette.BorderSubtle,
    };

    /// <summary>Gets the tooltip title colour of an item of <paramref name="rarity"/>.</summary>
    public static ColorRgba GetTitle(ItemRarity rarity) =>
        rarity == ItemRarity.Common ? CarbonUiPalette.TextPrimary : GetBorder(rarity);

    /// <summary>Gets the rarity of <paramref name="itemId"/>, or common when the catalog does not know it.</summary>
    public static ItemRarity GetRarity(ItemRules rules, uint itemId) =>
        rules.ItemCatalog.TryGet(itemId, out ItemCatalogEntry? entry) ? entry.Rarity : ItemRarity.Common;

    /// <summary>Gets the display name of <paramref name="rarity"/>.</summary>
    public static string GetName(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Uncommon => "Uncommon",
        ItemRarity.Rare => "Rare",
        ItemRarity.Epic => "Epic",
        ItemRarity.Legendary => "Legendary",
        ItemRarity.Common or _ => "Common",
    };
}
