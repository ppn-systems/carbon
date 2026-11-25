// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Data.Registry;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Built-in Native-AOT catalog of core tools, weapons, harvested resources, placeables, and crafting recipes
/// shared across server authority and client prediction/HUD.
/// </summary>
public static class StarterGameCatalog
{
    /// <summary>Starter timber axe (Harvest tier 1).</summary>
    public const uint WoodAxe = 1;

    /// <summary>Stone pickaxe (Harvest tier 2).</summary>
    public const uint StonePickaxe = 2;

    /// <summary>Forged iron sword for close-quarters melee combat.</summary>
    public const uint IronSword = 3;

    /// <summary>Timber harvested from trees.</summary>
    public const uint WoodLog = 4;

    /// <summary>Stone mined from boulders and mountain rock.</summary>
    public const uint StoneChunk = 5;

    /// <summary>Raw iron/copper/silver ore mined from mineral veins.</summary>
    public const uint IronOre = 6;

    /// <summary>Refined mana crystal harvested from crystalline deposits.</summary>
    public const uint ManaCrystal = 7;

    /// <summary>Restorative berry/mushroom consumable (+25 HP).</summary>
    public const uint BerryHerb = 8;

    /// <summary>Placeable wooden wall kit (StructureType 1).</summary>
    public const uint WoodWallKit = 9;

    /// <summary>Placeable stone wall kit (StructureType 2).</summary>
    public const uint StoneWallKit = 10;

    /// <summary>Placeable wooden door kit (StructureType 3).</summary>
    public const uint WoodDoorKit = 11;

    /// <summary>Crystal pickaxe (Harvest tier 3).</summary>
    public const uint CrystalPickaxe = 12;

    /// <summary>Gets all registered starter item archetypes indexed by ID.</summary>
    public static DefinitionRegistry<uint, ItemArchetype> Items { get; } = new(
    [
        new ItemArchetype(WoodAxe, "wood_axe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 1, MaxDurability: 120f),
        new ItemArchetype(StonePickaxe, "stone_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 2, MaxDurability: 160f),
        new ItemArchetype(IronSword, "iron_sword", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 1, MaxDurability: 200f),
        new ItemArchetype(WoodLog, "wood_log", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(StoneChunk, "stone_chunk", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(IronOre, "iron_ore", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(ManaCrystal, "mana_crystal", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(BerryHerb, "berry_herb", ItemCategory.Consumable, MaxStackSize: 99),
        new ItemArchetype(WoodWallKit, "wood_wall", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 1),
        new ItemArchetype(StoneWallKit, "stone_wall", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 2),
        new ItemArchetype(WoodDoorKit, "wood_door", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 3),
        new ItemArchetype(CrystalPickaxe, "crystal_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 3, MaxDurability: 250f),
    ]);

    /// <summary>Gets all registered crafting recipes indexed by ID (1..6).</summary>
    public static DefinitionRegistry<uint, CraftingRecipe> Recipes { get; } = new(
    [
        new CraftingRecipe(1, [new RecipeIngredient(WoodLog, 4)], new ItemSlot(WoodWallKit, 2)),
        new CraftingRecipe(2, [new RecipeIngredient(StoneChunk, 4)], new ItemSlot(StoneWallKit, 2)),
        new CraftingRecipe(3, [new RecipeIngredient(WoodLog, 6)], new ItemSlot(WoodDoorKit, 1)),
        new CraftingRecipe(4, [new RecipeIngredient(WoodLog, 5), new RecipeIngredient(StoneChunk, 5)], new ItemSlot(StonePickaxe, 1, 0, 160f)),
        new CraftingRecipe(5, [new RecipeIngredient(WoodLog, 4), new RecipeIngredient(IronOre, 6)], new ItemSlot(IronSword, 1, 0, 200f)),
        new CraftingRecipe(6, [new RecipeIngredient(IronOre, 4), new RecipeIngredient(ManaCrystal, 4)], new ItemSlot(CrystalPickaxe, 1, 0, 250f)),
    ]);

    /// <summary>
    /// Populates a fresh character inventory with starter tools, weapon, rations, and building kits.
    /// </summary>
    /// <param name="inventory">Target inventory container.</param>
    public static void GrantStarterLoadout(InventoryContainer inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        inventory.Clear();
        if (inventory.Capacity > 0)
        {
            inventory.SetSlot(0, new ItemSlot(WoodAxe, 1, 0, 120f));
        }

        if (inventory.Capacity > 1)
        {
            inventory.SetSlot(1, new ItemSlot(StonePickaxe, 1, 0, 160f));
        }

        if (inventory.Capacity > 2)
        {
            inventory.SetSlot(2, new ItemSlot(IronSword, 1, 0, 200f));
        }

        if (inventory.Capacity > 3)
        {
            inventory.SetSlot(3, new ItemSlot(BerryHerb, 5));
        }

        if (inventory.Capacity > 4)
        {
            inventory.SetSlot(4, new ItemSlot(WoodWallKit, 6));
        }

        if (inventory.Capacity > 5)
        {
            inventory.SetSlot(5, new ItemSlot(WoodDoorKit, 1));
        }
    }

    /// <summary>Returns the human-readable HUD display name for an item ID.</summary>
    public static string GetDisplayName(uint itemId) => itemId switch
    {
        WoodAxe => "Wood Axe",
        StonePickaxe => "Stone Pick",
        IronSword => "Iron Sword",
        WoodLog => "Wood Log",
        StoneChunk => "Stone",
        IronOre => "Iron Ore",
        ManaCrystal => "Crystal",
        BerryHerb => "Berry Herb",
        WoodWallKit => "Wood Wall",
        StoneWallKit => "Stone Wall",
        WoodDoorKit => "Wood Door",
        CrystalPickaxe => "Crys.Pick",
        _ => "Empty",
    };

    /// <summary>Returns baseline melee attack damage for an active item ID.</summary>
    public static float GetWeaponBaseDamage(uint itemId) => itemId switch
    {
        IronSword => 34f,
        CrystalPickaxe => 24f,
        StonePickaxe => 18f,
        WoodAxe => 16f,
        _ => 12f,
    };

    /// <summary>Maps an item's <see cref="Items"/> max stack size for crafting output bounds.</summary>
    public static int GetMaxStackSize(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? archetype) ? Math.Max(1, archetype.MaxStackSize) : 99;
}
