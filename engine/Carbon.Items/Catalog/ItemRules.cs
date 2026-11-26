// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Data.Registry;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// One immutable snapshot of the data-driven item and recipe tables (<c>items.json</c>, <c>recipes.json</c>) and the
/// stat lookups built on them. Owners hold an instance and pass it to the item policies: the server builds it from its
/// loaded config (one per <c>GameEconomy</c>, swapped whole on hot reload) and the desktop from each verified config
/// sync. There is no process-wide installed table, so several worlds or tests can run different configs side by side.
/// </summary>
public sealed class ItemRules
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemRules"/> class.
    /// </summary>
    /// <param name="items">Validated item catalog.</param>
    /// <param name="recipes">Recipe catalog compiled against <paramref name="items"/>.</param>
    public ItemRules(ItemCatalog items, RecipeCatalog recipes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(recipes);
        this.ItemCatalog = items;
        this.RecipeCatalog = recipes;
        this.Items = items.BuildArchetypeRegistry();
    }

    /// <summary>Gets the item catalog (codes, prices, rarity).</summary>
    public ItemCatalog ItemCatalog { get; }

    /// <summary>Gets the recipe catalog.</summary>
    public RecipeCatalog RecipeCatalog { get; }

    /// <summary>Gets the item archetypes (combat, tool, armor and consumable stats).</summary>
    public DefinitionRegistry<uint, ItemArchetype> Items { get; }

    /// <summary>Gets the compiled crafting recipes.</summary>
    public DefinitionRegistry<uint, CraftingRecipe> Recipes => this.RecipeCatalog.Registry;

    /// <summary>
    /// Builds rules from the copy of the shipped <c>resources/config</c> embedded at build time. For tests, offline
    /// tools and worlds started without a config directory; a running server or synced client uses its own config.
    /// </summary>
    /// <returns>A new instance.</returns>
    public static ItemRules LoadShippedDefaults()
    {
        ItemCatalog items = ItemCatalog.LoadFromJson(ReadEmbedded("items.json"));
        return new ItemRules(items, RecipeCatalog.LoadFromJson(ReadEmbedded("recipes.json"), items));
    }

    /// <summary>Returns the human-readable HUD display name for an item ID.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The display name, or <c>"Empty"</c>.</returns>
    public string GetDisplayName(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : "Empty";

    /// <summary>Returns armor defense rating contributed by <paramref name="itemId"/> when equipped.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Defense rating.</returns>
    public float GetArmorDefense(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) ? def.ArmorDefense : 0f;

    /// <summary>Returns bonus max HP contributed by <paramref name="itemId"/> when equipped.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Bonus HP.</returns>
    public float GetArmorBonusHealth(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) ? def.BonusHealth : 0f;

    /// <summary>Returns bonus max Mana contributed by <paramref name="itemId"/> when equipped.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Bonus mana.</returns>
    public float GetArmorBonusMana(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) ? def.BonusMana : 0f;

    /// <summary>Returns baseline melee attack damage for an active item ID.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Base damage (12 for an empty hand or unknown item).</returns>
    public float GetWeaponBaseDamage(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) ? def.BaseDamage : 12f;

    /// <summary>Returns harvest damage per strike against flora props (trees, bushes, cacti).</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Harvest damage.</returns>
    public float GetFloraHarvestDamage(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) ? def.FloraHarvestDamage : 20f;

    /// <summary>Returns harvest damage per strike against mineral props (boulders, ore veins, crystals).</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Harvest damage.</returns>
    public float GetMineralHarvestDamage(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) ? def.MineralHarvestDamage : 10f;

    /// <summary>Returns a concise HUD action tooltip describing the active item's role and tier.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Tooltip text.</returns>
    public string GetActionHint(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? def) && !string.IsNullOrEmpty(def.ActionHint)
            ? def.ActionHint
            : "Empty Hand | Forage Bushes & Punch (12 dmg)";

    /// <summary>Resolves the inventory placeable kit item ID corresponding to <paramref name="structureType"/>.</summary>
    /// <param name="structureType">Structure type byte.</param>
    /// <returns>The kit item id, or 0.</returns>
    public uint ResolveStructureKitItemId(byte structureType)
    {
        if (structureType == 0)
        {
            return 0U;
        }

        ReadOnlySpan<ItemArchetype> all = this.Items.All;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Category == ItemCategory.Placeable && all[i].PlaceableStructureType == structureType)
            {
                return all[i].Id;
            }
        }

        return 0U;
    }

    /// <summary>Returns an item's max stack size (99 when unknown).</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Max stack size.</returns>
    public int GetMaxStackSize(uint itemId) =>
        this.Items.TryGet(itemId, out ItemArchetype? archetype) ? Math.Max(1, archetype.MaxStackSize) : 99;

    private static string ReadEmbedded(string fileName)
    {
        string resource = "Carbon.Items.Config." + fileName;
        using Stream stream = typeof(ItemRules).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded default config '{resource}' is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
