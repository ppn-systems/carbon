// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Abstractions.Localization;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Localized, per-frame-cached labels of the crafting window (<c>craft.*</c> keys, ADR-023). Each visible recipe row
/// owns its own <see cref="CachedText"/>, so redrawing an unchanged list allocates nothing.
/// </summary>
internal sealed class CraftingText
{
    private readonly CachedText[] _rowNames;
    private readonly CachedText[] _rowTags;
    private readonly CachedText _title = new();
    private readonly CachedText _requirement = new();
    private readonly CachedText _station = new();
    private readonly CachedText _stats = new();
    private readonly CachedText[] _ingredientCounts;

    /// <summary>Initializes a new instance of the <see cref="CraftingText"/> class.</summary>
    /// <param name="visibleRows">Recipe rows shown at once.</param>
    /// <param name="maxIngredients">Ingredient cards shown at once.</param>
    public CraftingText(int visibleRows, int maxIngredients)
    {
        _rowNames = NewCaches(visibleRows);
        _rowTags = NewCaches(visibleRows);
        _ingredientCounts = NewCaches(maxIngredients);
    }

    /// <summary>Filter tab label keys, in tab order.</summary>
    public static string[] TabKeys { get; } = ["craft.tab.all", "craft.tab.weapons", "craft.tab.armor", "craft.tab.tools", "craft.tab.build", "craft.tab.potions"];

    /// <summary><c>Iron Sword x1</c> for a recipe row.</summary>
    public string RowName(int row, string itemName, int quantity) =>
        _rowNames[row].Format("craft.output", new TextArg("item", itemName), new TextArg("count", quantity));

    /// <summary><c>Iron Sword x1</c> for the selected recipe header.</summary>
    public string Title(string itemName, int quantity) =>
        _title.Format("craft.output", new TextArg("item", itemName), new TextArg("count", quantity));

    /// <summary><c>Owned: 2  |  Forge Lv 12</c>: the recipes.json station and unlock-level gate the server enforces.</summary>
    public string Requirement(int owned, CraftingRecipe recipe)
    {
        string stationKey = recipe.Station switch
        {
            CraftingStation.None => "craft.station.hand",
            CraftingStation.Workbench => "craft.station.workbench",
            CraftingStation.Forge => "craft.station.forge",
            CraftingStation.Stove => "craft.station.stove",
            CraftingStation.Alchemy => "craft.station.shrine",
            _ => "craft.station.generic",
        };
        string station = recipe.UnlockLevel > 1
            ? _station.Format("craft.station_level", new TextArg("station", GameText.Get(stationKey)), new TextArg("level", recipe.UnlockLevel))
            : GameText.Get(stationKey);
        return _requirement.Format("craft.requirement", new TextArg("owned", owned), new TextArg("station", station));
    }

    /// <summary>Short category tag under a recipe row (<c>Weapon  34 DMG</c>).</summary>
    public string ShortTag(int row, ItemRules rules, uint itemId)
    {
        if (!rules.Items.TryGet(itemId, out ItemArchetype? def))
        {
            return string.Empty;
        }

        CachedText cache = _rowTags[row];
        return def.Category switch
        {
            ItemCategory.Weapon => cache.Format("craft.tag.weapon", new TextArg("dmg", (int)def.BaseDamage)),
            ItemCategory.Armor => cache.Format("craft.tag.armor", new TextArg("def", (int)def.ArmorDefense)),
            ItemCategory.Tool => cache.Format("craft.tag.tool", new TextArg("tier", def.HarvestTier)),
            ItemCategory.Placeable => GameText.Get("craft.tag.structure"),
            ItemCategory.Consumable => GameText.Get("craft.tag.consumable"),
            ItemCategory.Accessory => GameText.Get("craft.tag.accessory"),
            ItemCategory.Quest => GameText.Get("craft.tag.quest"),
            ItemCategory.Resource or ItemCategory.None or _ => GameText.Get("craft.tag.material"),
        };
    }

    /// <summary>Detailed stat line of the selected recipe's output.</summary>
    public string DetailedStats(ItemRules rules, uint itemId)
    {
        if (!rules.Items.TryGet(itemId, out ItemArchetype? def))
        {
            return string.Empty;
        }

        return def.Category switch
        {
            ItemCategory.Weapon => _stats.Format(
                "craft.stats.weapon",
                new TextArg("dmg", (int)def.BaseDamage),
                new TextArg("crit", (int)(def.CritChance * 100f)),
                new TextArg("range", (int)def.AttackRange)),
            ItemCategory.Armor => _stats.Format(
                "craft.stats.armor",
                new TextArg("def", (int)def.ArmorDefense),
                new TextArg("hp", (int)def.BonusHealth),
                new TextArg("mp", (int)def.BonusMana)),
            ItemCategory.Tool => _stats.Format(
                "craft.stats.tool",
                new TextArg("tier", def.HarvestTier),
                new TextArg("chop", (int)def.FloraHarvestDamage),
                new TextArg("mine", (int)def.MineralHarvestDamage)),
            ItemCategory.Consumable or ItemCategory.Placeable or ItemCategory.Accessory or ItemCategory.Resource or ItemCategory.Quest or ItemCategory.None or _ =>
                _stats.Format("craft.stats.stack", new TextArg("max", def.MaxStackSize)),
        };
    }

    /// <summary><c>2/5</c> for an ingredient card (cached per card).</summary>
    public string IngredientCount(int index, int have, int need) =>
        index < _ingredientCounts.Length
            ? _ingredientCounts[index].Format("craft.ingredient_count", new TextArg("have", have), new TextArg("need", need))
            : GameText.Format("craft.ingredient_count", new TextArg("have", have), new TextArg("need", need));

    private static CachedText[] NewCaches(int count)
    {
        CachedText[] caches = new CachedText[count];
        for (int i = 0; i < count; i++)
        {
            caches[i] = new CachedText();
        }

        return caches;
    }
}
