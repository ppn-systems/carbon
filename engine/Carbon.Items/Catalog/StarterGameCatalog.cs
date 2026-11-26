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

    /// <summary>Light stitched leather cap (Head armor).</summary>
    public const uint LeatherCap = 13;

    /// <summary>Light reinforced leather tunic (Chest armor).</summary>
    public const uint LeatherTunic = 14;

    /// <summary>Light stitched leather greaves (Leg armor).</summary>
    public const uint LeatherGreaves = 15;

    /// <summary>Light leather travel boots (Feet armor).</summary>
    public const uint LeatherBoots = 16;

    /// <summary>Forged vanguard iron helmet (Head armor).</summary>
    public const uint IronHelmet = 17;

    /// <summary>Forged vanguard iron chestplate (Chest armor).</summary>
    public const uint IronChestplate = 18;

    /// <summary>Forged vanguard iron leggings (Leg armor).</summary>
    public const uint IronLeggings = 19;

    /// <summary>Forged vanguard iron boots (Feet armor).</summary>
    public const uint IronBoots = 20;

    /// <summary>Arcane mana-infused crystal crown (Head armor).</summary>
    public const uint CrystalCrown = 21;

    /// <summary>Arcane crystal-weave battlemage robe (Chest armor).</summary>
    public const uint CrystalRobe = 22;

    /// <summary>Restorative mana &amp; stamina tonic (+45 MP, +30 Stamina).</summary>
    public const uint ManaTonic = 23;

    /// <summary>Gets all registered starter item archetypes indexed by ID.</summary>
    public static DefinitionRegistry<uint, ItemArchetype> Items { get; } = new(
    [
        new ItemArchetype(WoodAxe, "wood_axe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 1, MaxDurability: 120f),
        new ItemArchetype(StonePickaxe, "stone_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 2, MaxDurability: 160f),
        new ItemArchetype(IronSword, "iron_sword", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 200f),
        new ItemArchetype(WoodLog, "wood_log", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(StoneChunk, "stone_chunk", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(IronOre, "iron_ore", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(ManaCrystal, "mana_crystal", ItemCategory.Resource, MaxStackSize: 99),
        new ItemArchetype(BerryHerb, "berry_herb", ItemCategory.Consumable, MaxStackSize: 99),
        new ItemArchetype(WoodWallKit, "wood_wall", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 1),
        new ItemArchetype(StoneWallKit, "stone_wall", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 2),
        new ItemArchetype(WoodDoorKit, "wood_door", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 3),
        new ItemArchetype(CrystalPickaxe, "crystal_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 3, MaxDurability: 250f),
        new ItemArchetype(LeatherCap, "leather_cap", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Head, MaxDurability: 120f),
        new ItemArchetype(LeatherTunic, "leather_tunic", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Chest, MaxDurability: 150f),
        new ItemArchetype(LeatherGreaves, "leather_greaves", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Legs, MaxDurability: 140f),
        new ItemArchetype(LeatherBoots, "leather_boots", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Feet, MaxDurability: 120f),
        new ItemArchetype(IronHelmet, "iron_helmet", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Head, MaxDurability: 220f),
        new ItemArchetype(IronChestplate, "iron_chestplate", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Chest, MaxDurability: 260f),
        new ItemArchetype(IronLeggings, "iron_leggings", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Legs, MaxDurability: 240f),
        new ItemArchetype(IronBoots, "iron_boots", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Feet, MaxDurability: 200f),
        new ItemArchetype(CrystalCrown, "crystal_crown", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Head, MaxDurability: 240f),
        new ItemArchetype(CrystalRobe, "crystal_robe", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Chest, MaxDurability: 280f),
        new ItemArchetype(ManaTonic, "mana_tonic", ItemCategory.Consumable, MaxStackSize: 20),
    ]);

    /// <summary>Gets all registered crafting recipes indexed by ID (1..15).</summary>
    public static DefinitionRegistry<uint, CraftingRecipe> Recipes { get; } = new(
    [
        new CraftingRecipe(1, [new RecipeIngredient(WoodLog, 4)], new ItemSlot(WoodWallKit, 2)),
        new CraftingRecipe(2, [new RecipeIngredient(StoneChunk, 4)], new ItemSlot(StoneWallKit, 2)),
        new CraftingRecipe(3, [new RecipeIngredient(WoodLog, 6)], new ItemSlot(WoodDoorKit, 1)),
        new CraftingRecipe(4, [new RecipeIngredient(WoodLog, 5), new RecipeIngredient(StoneChunk, 5)], new ItemSlot(StonePickaxe, 1, 0, 160f)),
        new CraftingRecipe(5, [new RecipeIngredient(WoodLog, 4), new RecipeIngredient(IronOre, 6)], new ItemSlot(IronSword, 1, 0, 200f)),
        new CraftingRecipe(6, [new RecipeIngredient(IronOre, 4), new RecipeIngredient(ManaCrystal, 4)], new ItemSlot(CrystalPickaxe, 1, 0, 250f)),
        new CraftingRecipe(7, [new RecipeIngredient(WoodLog, 4), new RecipeIngredient(BerryHerb, 2)], new ItemSlot(LeatherCap, 1, 0, 120f)),
        new CraftingRecipe(8, [new RecipeIngredient(WoodLog, 6), new RecipeIngredient(BerryHerb, 3)], new ItemSlot(LeatherTunic, 1, 0, 150f)),
        new CraftingRecipe(9, [new RecipeIngredient(WoodLog, 5), new RecipeIngredient(BerryHerb, 2)], new ItemSlot(LeatherGreaves, 1, 0, 140f)),
        new CraftingRecipe(10, [new RecipeIngredient(WoodLog, 3), new RecipeIngredient(BerryHerb, 2)], new ItemSlot(LeatherBoots, 1, 0, 120f)),
        new CraftingRecipe(11, [new RecipeIngredient(IronOre, 4), new RecipeIngredient(StoneChunk, 2)], new ItemSlot(IronHelmet, 1, 0, 220f)),
        new CraftingRecipe(12, [new RecipeIngredient(IronOre, 7), new RecipeIngredient(StoneChunk, 3)], new ItemSlot(IronChestplate, 1, 0, 260f)),
        new CraftingRecipe(13, [new RecipeIngredient(IronOre, 5), new RecipeIngredient(StoneChunk, 3)], new ItemSlot(IronLeggings, 1, 0, 240f)),
        new CraftingRecipe(14, [new RecipeIngredient(IronOre, 4), new RecipeIngredient(StoneChunk, 2)], new ItemSlot(IronBoots, 1, 0, 200f)),
        new CraftingRecipe(15, [new RecipeIngredient(BerryHerb, 2), new RecipeIngredient(ManaCrystal, 1)], new ItemSlot(ManaTonic, 2)),
        new CraftingRecipe(16, [new RecipeIngredient(ManaCrystal, 4), new RecipeIngredient(IronOre, 4)], new ItemSlot(CrystalCrown, 1, 0, 240f)),
        new CraftingRecipe(17, [new RecipeIngredient(ManaCrystal, 6), new RecipeIngredient(IronOre, 5)], new ItemSlot(CrystalRobe, 1, 0, 280f)),
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

        if (inventory.Capacity > 6)
        {
            inventory.SetSlot(6, new ItemSlot(ManaTonic, 3));
        }

        if (inventory.Capacity > 10)
        {
            inventory.SetSlot(9, new ItemSlot(LeatherTunic, 1, 0, 150f));
            inventory.SetSlot(10, new ItemSlot(LeatherBoots, 1, 0, 120f));
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
        LeatherCap => "Leather Cap",
        LeatherTunic => "Leather Tunic",
        LeatherGreaves => "Leather Legs",
        LeatherBoots => "Leather Boots",
        IronHelmet => "Iron Helm",
        IronChestplate => "Iron Plate",
        IronLeggings => "Iron Legs",
        IronBoots => "Iron Boots",
        CrystalCrown => "Crystal Crown",
        CrystalRobe => "Crystal Robe",
        ManaTonic => "Mana Tonic",
        _ => "Empty",
    };

    /// <summary>Returns armor defense rating contributed by <paramref name="itemId"/> when equipped.</summary>
    public static float GetArmorDefense(uint itemId) => itemId switch
    {
        LeatherCap => 3f,
        LeatherTunic => 6f,
        LeatherGreaves => 4f,
        LeatherBoots => 3f,
        IronHelmet => 7f,
        IronChestplate => 14f,
        IronLeggings => 10f,
        IronBoots => 6f,
        CrystalCrown => 8f,
        CrystalRobe => 15f,
        _ => 0f,
    };

    /// <summary>Returns bonus max HP contributed by <paramref name="itemId"/> when equipped.</summary>
    public static float GetArmorBonusHealth(uint itemId) => itemId switch
    {
        LeatherTunic => 15f,
        LeatherGreaves => 10f,
        IronHelmet => 18f,
        IronChestplate => 40f,
        IronLeggings => 25f,
        IronBoots => 12f,
        CrystalCrown => 15f,
        CrystalRobe => 28f,
        _ => 0f,
    };

    /// <summary>Returns bonus max Mana contributed by <paramref name="itemId"/> when equipped.</summary>
    public static float GetArmorBonusMana(uint itemId) => itemId switch
    {
        CrystalCrown => 35f,
        CrystalRobe => 55f,
        LeatherCap => 10f,
        _ => 0f,
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

    /// <summary>Returns harvest damage per strike against flora props (trees, bushes, cacti).</summary>
    public static float GetFloraHarvestDamage(uint itemId) => itemId switch
    {
        WoodAxe => 55f,
        CrystalPickaxe => 60f,
        StonePickaxe => 38f,
        IronSword => 28f,
        _ => 20f,
    };

    /// <summary>Returns harvest damage per strike against mineral props (boulders, ore veins, crystals).</summary>
    public static float GetMineralHarvestDamage(uint itemId) => itemId switch
    {
        CrystalPickaxe => 95f,
        StonePickaxe => 68f,
        WoodAxe => 26f,
        IronSword => 16f,
        _ => 10f,
    };

    /// <summary>Returns a concise HUD action tooltip describing the active item's role and tier.</summary>
    public static string GetActionHint(uint itemId) => itemId switch
    {
        WoodAxe => "Tier 1 Axe | Chop Trees (55 dmg)",
        StonePickaxe => "Tier 2 Pick | Mine Stone/Ore (68 dmg)",
        CrystalPickaxe => "Tier 3 Pick | Mine All Crystal/Gold (95 dmg)",
        IronSword => "Melee Blade | Combat 34 dmg (+Crit) | Not for Wood/Stone",
        BerryHerb => "Consumable | Use: Restore +25 HP",
        ManaTonic => "Consumable | Use: +45 MP & +30 Stamina",
        LeatherCap or LeatherTunic or LeatherGreaves or LeatherBoots => "Leather Armor | Equip in [I] Bag to raise DEF & HP",
        IronHelmet or IronChestplate or IronLeggings or IronBoots => "Iron Armor | Heavy DEF & HP | Forge [+] Upgradable",
        CrystalCrown or CrystalRobe => "Arcane Armor | High MP & DEF | Forge [+] Upgradable",
        WoodWallKit => "Builder | Place Wood Wall on Target Tile",
        StoneWallKit => "Builder | Place Stone Wall on Target Tile",
        WoodDoorKit => "Builder | Place Wood Door ([E] Open/Close)",
        WoodLog => "Material | Craft Walls, Doors & Tools [C]",
        StoneChunk => "Material | Craft Stone Walls & Pickaxe [C]",
        IronOre => "Material | Craft Iron Sword & Crystal Pick [C]",
        ManaCrystal => "Rare Crystal | Craft Tier 3 Crystal Pick [C]",
        _ => "Empty Hand | Forage Bushes & Punch (12 dmg)",
    };

    /// <summary>Maps an item's <see cref="Items"/> max stack size for crafting output bounds.</summary>
    public static int GetMaxStackSize(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? archetype) ? Math.Max(1, archetype.MaxStackSize) : 99;
}
