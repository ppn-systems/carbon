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

    /// <summary>Hunter ashwood recurve bow (ranged physical projectiles).</summary>
    public const uint HuntingBow = 24;

    /// <summary>Crystal-tipped arcane mage staff (fires mana bolts, +30 MaxMP).</summary>
    public const uint ArcaneStaff = 25;

    /// <summary>Tier 3 prism crystal bow (high-velocity piercing arrows).</summary>
    public const uint CrystalBow = 26;

    /// <summary>Archmage astral crystal staff (+50 MaxMP, high-power mana bolts).</summary>
    public const uint CrystalStaff = 27;

    /// <summary>Placeable blacksmith anvil workstation kit (<c>StructureType.ForgeAnvil = 8</c>).</summary>
    public const uint ForgeAnvilKit = 28;

    /// <summary>Hearty roast haunch ration (+55 HP, +40 Stamina).</summary>
    public const uint RoastMeat = 29;

    /// <summary>Arcane berry pastry (+30 HP, +60 MP).</summary>
    public const uint ArcanePie = 30;

    /// <summary>Abyssal serrated demon cleaver (heavy melee crit weapon).</summary>
    public const uint DemonCleaver = 31;

    /// <summary>Tribal skull shaman staff (medium-high hex bolt caster, +40 MaxMP).</summary>
    public const uint ShamanStaff = 32;

    /// <summary>Placeable reinforced stone door kit (<c>StructureType.StoneDoor = 4</c>).</summary>
    public const uint StoneDoorKit = 33;

    /// <summary>Placeable timber floor kit (<c>StructureType.WoodFloor = 5</c>).</summary>
    public const uint WoodFloorKit = 34;

    /// <summary>Placeable flagstone floor kit (<c>StructureType.StoneFloor = 6</c>).</summary>
    public const uint StoneFloorKit = 35;

    /// <summary>Placeable roof support pillar kit (<c>StructureType.RoofPillar = 7</c>).</summary>
    public const uint RoofPillarKit = 36;

    /// <summary>Thick wolf pelt harvested from frontier beasts.</summary>
    public const uint BeastPelt = 37;

    /// <summary>Jagged skeleton bone shard dropped by undead archers and shamans.</summary>
    public const uint BoneShard = 38;

    /// <summary>Molten abyssal core dropped by Crimson Demons.</summary>
    public const uint DemonEmber = 39;

    /// <summary>Gets all registered starter item archetypes indexed by ID.</summary>
    public static DefinitionRegistry<uint, ItemArchetype> Items { get; } = new(
    [
        new ItemArchetype(
            WoodAxe, "wood_axe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 1, MaxDurability: 120f,
            DisplayName: "Wood Axe", BaseDamage: 16f, FloraHarvestDamage: 55f, MineralHarvestDamage: 26f,
            ActionHint: "Tier 1 Axe | Chop Trees (55 dmg)", Capabilities: ToolCapabilities.ChopFlora),
        new ItemArchetype(
            StonePickaxe, "stone_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 2, MaxDurability: 160f,
            DisplayName: "Stone Pick", BaseDamage: 18f, FloraHarvestDamage: 38f, MineralHarvestDamage: 68f,
            ActionHint: "Tier 2 Pick | Mine Stone/Ore (68 dmg)", Capabilities: ToolCapabilities.MineMineral),
        new ItemArchetype(
            IronSword, "iron_sword", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 200f,
            DisplayName: "Iron Sword", BaseDamage: 34f, CritChance: 0.20f, FloraHarvestDamage: 28f, MineralHarvestDamage: 16f,
            ActionHint: "Melee Blade | Combat 34 dmg (+Crit) | Not for Wood/Stone", Capabilities: ToolCapabilities.MeleeWeapon),
        new ItemArchetype(
            WoodLog, "wood_log", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Wood Log", ActionHint: "Material | Craft Walls, Doors & Tools [C]"),
        new ItemArchetype(
            StoneChunk, "stone_chunk", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Stone", ActionHint: "Material | Craft Stone Walls & Pickaxe [C]"),
        new ItemArchetype(
            IronOre, "iron_ore", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Iron Ore", ActionHint: "Material | Craft Iron Sword & Crystal Pick [C]"),
        new ItemArchetype(
            ManaCrystal, "mana_crystal", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Crystal", ActionHint: "Rare Crystal | Craft Tier 3 Crystal Pick [C]"),
        new ItemArchetype(
            BerryHerb, "berry_herb", ItemCategory.Consumable, MaxStackSize: 99,
            DisplayName: "Berry Herb", HealHealth: 25f, ActionHint: "Consumable | Use: Restore +25 HP"),
        new ItemArchetype(
            WoodWallKit, "wood_wall", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 1,
            DisplayName: "Wood Wall", ActionHint: "Builder | Place Wood Wall on Target Tile"),
        new ItemArchetype(
            StoneWallKit, "stone_wall", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 2,
            DisplayName: "Stone Wall", ActionHint: "Builder | Place Stone Wall on Target Tile"),
        new ItemArchetype(
            WoodDoorKit, "wood_door", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 3,
            DisplayName: "Wood Door", ActionHint: "Builder | Place Wood Door ([E] Open/Close)"),
        new ItemArchetype(
            CrystalPickaxe, "crystal_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipmentSlotKind.Tool, HarvestTier: 3, MaxDurability: 250f,
            DisplayName: "Crys.Pick", BaseDamage: 24f, FloraHarvestDamage: 60f, MineralHarvestDamage: 95f,
            ActionHint: "Tier 3 Pick | Mine All Crystal/Gold (95 dmg)", Capabilities: ToolCapabilities.MineMineral),
        new ItemArchetype(
            LeatherCap, "leather_cap", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Head, MaxDurability: 120f,
            DisplayName: "Leather Cap", ArmorDefense: 3f, BonusMana: 10f,
            ActionHint: "Leather Armor | Equip in [I] Bag to raise DEF & MP"),
        new ItemArchetype(
            LeatherTunic, "leather_tunic", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Chest, MaxDurability: 150f,
            DisplayName: "Leather Tunic", ArmorDefense: 6f, BonusHealth: 15f,
            ActionHint: "Leather Armor | Equip in [I] Bag to raise DEF & HP"),
        new ItemArchetype(
            LeatherGreaves, "leather_greaves", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Legs, MaxDurability: 140f,
            DisplayName: "Leather Legs", ArmorDefense: 4f, BonusHealth: 10f,
            ActionHint: "Leather Armor | Equip in [I] Bag to raise DEF & HP"),
        new ItemArchetype(
            LeatherBoots, "leather_boots", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Feet, MaxDurability: 120f,
            DisplayName: "Leather Boots", ArmorDefense: 3f,
            ActionHint: "Leather Armor | Equip in [I] Bag to raise DEF & HP"),
        new ItemArchetype(
            IronHelmet, "iron_helmet", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Head, MaxDurability: 220f,
            DisplayName: "Iron Helm", ArmorDefense: 7f, BonusHealth: 18f,
            ActionHint: "Iron Armor | Heavy DEF & HP | Forge [+] Upgradable"),
        new ItemArchetype(
            IronChestplate, "iron_chestplate", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Chest, MaxDurability: 260f,
            DisplayName: "Iron Plate", ArmorDefense: 14f, BonusHealth: 40f,
            ActionHint: "Iron Armor | Heavy DEF & HP | Forge [+] Upgradable"),
        new ItemArchetype(
            IronLeggings, "iron_leggings", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Legs, MaxDurability: 240f,
            DisplayName: "Iron Legs", ArmorDefense: 10f, BonusHealth: 25f,
            ActionHint: "Iron Armor | Heavy DEF & HP | Forge [+] Upgradable"),
        new ItemArchetype(
            IronBoots, "iron_boots", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Feet, MaxDurability: 200f,
            DisplayName: "Iron Boots", ArmorDefense: 6f, BonusHealth: 12f,
            ActionHint: "Iron Armor | Heavy DEF & HP | Forge [+] Upgradable"),
        new ItemArchetype(
            CrystalCrown, "crystal_crown", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Head, MaxDurability: 240f,
            DisplayName: "Crystal Crown", ArmorDefense: 8f, BonusHealth: 15f, BonusMana: 35f,
            ActionHint: "Arcane Armor | High MP & DEF | Forge [+] Upgradable"),
        new ItemArchetype(
            CrystalRobe, "crystal_robe", ItemCategory.Armor, MaxStackSize: 1, EquipmentSlotKind.Chest, MaxDurability: 280f,
            DisplayName: "Crystal Robe", ArmorDefense: 15f, BonusHealth: 28f, BonusMana: 55f,
            ActionHint: "Arcane Armor | High MP & DEF | Forge [+] Upgradable"),
        new ItemArchetype(
            ManaTonic, "mana_tonic", ItemCategory.Consumable, MaxStackSize: 20,
            DisplayName: "Mana Tonic", RestoreMana: 45f, RestoreStamina: 30f,
            ActionHint: "Consumable | Use: +45 MP & +30 Stamina"),
        new ItemArchetype(
            HuntingBow, "hunting_bow", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 180f,
            DisplayName: "Hunt Bow", BaseDamage: 26f, CritChance: 0.22f, FloraHarvestDamage: 12f, MineralHarvestDamage: 0f,
            ActionHint: "Ranged Bow | Shoot Arrows (26 dmg, 290 range)", AttackRange: 290f, ProjectileSpeed: 520f, ProjectileArchetype: 1, Capabilities: ToolCapabilities.RangedBow),
        new ItemArchetype(
            ArcaneStaff, "arcane_staff", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 190f,
            DisplayName: "Arc.Staff", BaseDamage: 31f, CritChance: 0.18f, FloraHarvestDamage: 10f, MineralHarvestDamage: 0f, BonusMana: 30f,
            ActionHint: "Mage Staff | Arcane Bolt (31 dmg, 4 MP, +30 MaxMP)", AttackRange: 275f, ProjectileSpeed: 460f, ProjectileArchetype: 2, AttackManaCost: 4f, Capabilities: ToolCapabilities.MagicFocus),
        new ItemArchetype(
            CrystalBow, "crystal_bow", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 250f,
            DisplayName: "Crys.Bow", BaseDamage: 38f, CritChance: 0.25f, FloraHarvestDamage: 14f, MineralHarvestDamage: 0f,
            ActionHint: "Tier 3 Bow | Prism Arrows (38 dmg, 320 range)", AttackRange: 320f, ProjectileSpeed: 580f, ProjectileArchetype: 3, Capabilities: ToolCapabilities.RangedBow),
        new ItemArchetype(
            CrystalStaff, "crystal_staff", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 260f,
            DisplayName: "Crys.Staff", BaseDamage: 44f, CritChance: 0.22f, FloraHarvestDamage: 12f, MineralHarvestDamage: 0f, BonusMana: 50f,
            ActionHint: "Archmage Staff | Astral Bolt (44 dmg, 6 MP, +50 MaxMP)", AttackRange: 300f, ProjectileSpeed: 500f, ProjectileArchetype: 2, AttackManaCost: 6f, Capabilities: ToolCapabilities.MagicFocus),
        new ItemArchetype(
            ForgeAnvilKit, "forge_anvil", ItemCategory.Placeable, MaxStackSize: 20, PlaceableStructureType: 8,
            DisplayName: "Forge Anvil",
            ActionHint: "Workstation | Place Anvil to Enhance Gear ([U])"),
        new ItemArchetype(
            RoastMeat, "roast_meat", ItemCategory.Consumable, MaxStackSize: 20,
            DisplayName: "Roast Meat", HealHealth: 55f, RestoreStamina: 40f,
            ActionHint: "Hearty Ration | Use: +55 HP & +40 Stamina"),
        new ItemArchetype(
            ArcanePie, "arcane_pie", ItemCategory.Consumable, MaxStackSize: 20,
            DisplayName: "Arcane Pie", HealHealth: 30f, RestoreMana: 60f,
            ActionHint: "Mystic Pastry | Use: +30 HP & +60 MP"),
        new ItemArchetype(
            DemonCleaver, "demon_cleaver", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 300f,
            DisplayName: "Dmn.Cleaver", BaseDamage: 52f, CritChance: 0.24f, FloraHarvestDamage: 24f, MineralHarvestDamage: 12f, AttackRange: 62f,
            ActionHint: "Abyssal Blade | Heavy Cleave (52 dmg, 24% Crit)", Capabilities: ToolCapabilities.MeleeWeapon),
        new ItemArchetype(
            ShamanStaff, "shaman_staff", ItemCategory.Weapon, MaxStackSize: 1, EquipmentSlotKind.MainHand, HarvestTier: 0, MaxDurability: 230f,
            DisplayName: "Hex Staff", BaseDamage: 38f, CritChance: 0.20f, FloraHarvestDamage: 10f, MineralHarvestDamage: 0f, BonusMana: 40f,
            ActionHint: "Shaman Focus | Hex Bolt (38 dmg, 5 MP, +40 MaxMP)", AttackRange: 290f, ProjectileSpeed: 480f, ProjectileArchetype: 6, AttackManaCost: 5f, Capabilities: ToolCapabilities.MagicFocus),
        new ItemArchetype(
            StoneDoorKit, "stone_door", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 4,
            DisplayName: "Stone Door", ActionHint: "Builder | Place Stone Door ([E] Open/Close)"),
        new ItemArchetype(
            WoodFloorKit, "wood_floor", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 5,
            DisplayName: "Wood Floor", ActionHint: "Builder | Place Timber Floor (+15% Speed)"),
        new ItemArchetype(
            StoneFloorKit, "stone_floor", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 6,
            DisplayName: "Stone Floor", ActionHint: "Builder | Place Flagstone Floor (+20% Speed)"),
        new ItemArchetype(
            RoofPillarKit, "roof_pillar", ItemCategory.Placeable, MaxStackSize: 99, PlaceableStructureType: 7,
            DisplayName: "Roof Pillar", ActionHint: "Builder | Place Support Pillar (300 HP)"),
        new ItemArchetype(
            BeastPelt, "beast_pelt", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Beast Pelt", ActionHint: "Monster Drop | Craft Leather Gear & Rations [C]"),
        new ItemArchetype(
            BoneShard, "bone_shard", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Bone Shard", ActionHint: "Monster Drop | Craft Bows & Potions [C]"),
        new ItemArchetype(
            DemonEmber, "demon_ember", ItemCategory.Resource, MaxStackSize: 99,
            DisplayName: "Demon Ember", ActionHint: "Boss Drop | Craft Demon Cleaver & High Gear [C]"),
    ]);

    /// <summary>Gets all registered crafting recipes indexed by ID (1..34).</summary>
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
        new CraftingRecipe(18, [new RecipeIngredient(WoodLog, 6), new RecipeIngredient(BerryHerb, 2)], new ItemSlot(HuntingBow, 1, 0, 180f)),
        new CraftingRecipe(19, [new RecipeIngredient(WoodLog, 5), new RecipeIngredient(ManaCrystal, 2)], new ItemSlot(ArcaneStaff, 1, 0, 190f)),
        new CraftingRecipe(20, [new RecipeIngredient(WoodLog, 5), new RecipeIngredient(IronOre, 3), new RecipeIngredient(ManaCrystal, 4)], new ItemSlot(CrystalBow, 1, 0, 250f)),
        new CraftingRecipe(21, [new RecipeIngredient(ManaCrystal, 5), new RecipeIngredient(IronOre, 3)], new ItemSlot(CrystalStaff, 1, 0, 260f)),
        new CraftingRecipe(22, [new RecipeIngredient(IronOre, 6), new RecipeIngredient(StoneChunk, 6)], new ItemSlot(ForgeAnvilKit, 1)),
        new CraftingRecipe(23, [new RecipeIngredient(BerryHerb, 2), new RecipeIngredient(WoodLog, 1)], new ItemSlot(RoastMeat, 2)),
        new CraftingRecipe(24, [new RecipeIngredient(BerryHerb, 2), new RecipeIngredient(ManaCrystal, 1)], new ItemSlot(ArcanePie, 2)),
        new CraftingRecipe(25, [new RecipeIngredient(IronOre, 6), new RecipeIngredient(ManaCrystal, 4)], new ItemSlot(DemonCleaver, 1, 0, 300f)),
        new CraftingRecipe(26, [new RecipeIngredient(WoodLog, 4), new RecipeIngredient(ManaCrystal, 3), new RecipeIngredient(IronOre, 2)], new ItemSlot(ShamanStaff, 1, 0, 230f)),
        new CraftingRecipe(27, [new RecipeIngredient(StoneChunk, 6), new RecipeIngredient(IronOre, 1)], new ItemSlot(StoneDoorKit, 1)),
        new CraftingRecipe(28, [new RecipeIngredient(WoodLog, 4)], new ItemSlot(WoodFloorKit, 4)),
        new CraftingRecipe(29, [new RecipeIngredient(StoneChunk, 4)], new ItemSlot(StoneFloorKit, 4)),
        new CraftingRecipe(30, [new RecipeIngredient(StoneChunk, 5), new RecipeIngredient(WoodLog, 2)], new ItemSlot(RoofPillarKit, 2)),
        new CraftingRecipe(31, [new RecipeIngredient(BeastPelt, 3), new RecipeIngredient(WoodLog, 2)], new ItemSlot(LeatherTunic, 1, 0, 150f)),
        new CraftingRecipe(32, [new RecipeIngredient(BeastPelt, 2), new RecipeIngredient(BerryHerb, 1)], new ItemSlot(RoastMeat, 2)),
        new CraftingRecipe(33, [new RecipeIngredient(BoneShard, 3), new RecipeIngredient(WoodLog, 2)], new ItemSlot(HuntingBow, 1, 0, 180f)),
        new CraftingRecipe(34, [new RecipeIngredient(DemonEmber, 2), new RecipeIngredient(IronOre, 4), new RecipeIngredient(ManaCrystal, 2)], new ItemSlot(DemonCleaver, 1, 0, 300f)),
    ]);

    /// <summary>
    /// Populates a fresh character inventory with default starter Vanguard gear.
    /// </summary>
    /// <param name="inventory">Target inventory container.</param>
    public static void GrantStarterLoadout(InventoryContainer inventory) =>
        GrantStarterLoadout(inventory, startingClass: 0);

    /// <summary>
    /// Populates a fresh character inventorytailored for <paramref name="startingClass"/>
    /// (<c>0</c>=Vanguard Sword, <c>1</c>=Ranger Bow, <c>2</c>=Arcanist Staff).
    /// </summary>
    /// <param name="inventory">Target inventory container.</param>
    /// <param name="startingClass">Selected starting hero class byte.</param>
    public static void GrantStarterLoadout(InventoryContainer inventory, byte startingClass)
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
            ItemSlot primaryWeapon = startingClass switch
            {
                1 => new ItemSlot(HuntingBow, 1, 0, 180f),
                2 => new ItemSlot(ArcaneStaff, 1, 0, 190f),
                _ => new ItemSlot(IronSword, 1, 0, 200f),
            };
            inventory.SetSlot(2, in primaryWeapon);
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
            inventory.SetSlot(6, new ItemSlot(ManaTonic, startingClass == 2 ? 6 : 3));
        }

        if (inventory.Capacity > 7)
        {
            // Building materials: wall/door kits only select blueprints, so starters carry logs for six walls.
            inventory.SetSlot(7, new ItemSlot(WoodLog, 18));
        }

        if (inventory.Capacity > PlayerInventoryLayout.HeadSlot)
        {
            inventory.SetSlot(PlayerInventoryLayout.HeadSlot, new ItemSlot(LeatherCap, 1, 0, 120f));
        }

        if (inventory.Capacity > PlayerInventoryLayout.ChestSlot)
        {
            inventory.SetSlot(PlayerInventoryLayout.ChestSlot, new ItemSlot(LeatherTunic, 1, 0, 150f));
        }

        if (inventory.Capacity > PlayerInventoryLayout.FeetSlot)
        {
            inventory.SetSlot(PlayerInventoryLayout.FeetSlot, new ItemSlot(LeatherBoots, 1, 0, 120f));
        }
    }

    /// <summary>Returns the human-readable HUD display name for an item ID.</summary>
    public static string GetDisplayName(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : "Empty";

    /// <summary>Returns armor defense rating contributed by <paramref name="itemId"/> when equipped.</summary>
    public static float GetArmorDefense(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) ? def.ArmorDefense : 0f;

    /// <summary>Returns bonus max HP contributed by <paramref name="itemId"/> when equipped.</summary>
    public static float GetArmorBonusHealth(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) ? def.BonusHealth : 0f;

    /// <summary>Returns bonus max Mana contributed by <paramref name="itemId"/> when equipped.</summary>
    public static float GetArmorBonusMana(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) ? def.BonusMana : 0f;

    /// <summary>Returns baseline melee attack damage for an active item ID.</summary>
    public static float GetWeaponBaseDamage(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) ? def.BaseDamage : 12f;

    /// <summary>Returns harvest damage per strike against flora props (trees, bushes, cacti).</summary>
    public static float GetFloraHarvestDamage(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) ? def.FloraHarvestDamage : 20f;

    /// <summary>Returns harvest damage per strike against mineral props (boulders, ore veins, crystals).</summary>
    public static float GetMineralHarvestDamage(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) ? def.MineralHarvestDamage : 10f;

    /// <summary>Returns a concise HUD action tooltip describing the active item's role and tier.</summary>
    public static string GetActionHint(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? def) && !string.IsNullOrEmpty(def.ActionHint)
            ? def.ActionHint
            : "Empty Hand | Forage Bushes & Punch (12 dmg)";

    /// <summary>Resolves the inventory placeable kit item ID corresponding to <paramref name="structureType"/>.</summary>
    public static uint ResolveStructureKitItemId(byte structureType)
    {
        if (structureType == 0)
        {
            return 0U;
        }

        ReadOnlySpan<ItemArchetype> all = Items.All;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Category == ItemCategory.Placeable && all[i].PlaceableStructureType == structureType)
            {
                return all[i].Id;
            }
        }

        return 0U;
    }

    /// <summary>Maps an item's <see cref="Items"/> max stack size for crafting output bounds.</summary>
    public static int GetMaxStackSize(uint itemId) =>
        Items.TryGet(itemId, out ItemArchetype? archetype) ? Math.Max(1, archetype.MaxStackSize) : 99;
}
