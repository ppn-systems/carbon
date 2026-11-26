// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading;
using Carbon.Data.Registry;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Stable item id constants, the starter loadout, and the active item and recipe tables shared by server authority and
/// client prediction/HUD. The tables are data-driven (<c>resources/config/items.json</c> and <c>recipes.json</c>);
/// no item stats or recipes are hardcoded here.
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

    private static volatile Tables? s_tables;

    /// <summary>
    /// Gets the item archetypes (combat, tool, armor and consumable stats) built from <c>items.json</c>.
    /// The server installs its loaded config at boot and the desktop installs the server-synced config after every
    /// sync (<see cref="Install"/>); until then the build-time copy of the shipped <c>resources/config</c> is used.
    /// </summary>
    public static DefinitionRegistry<uint, ItemArchetype> Items => Current.Items;

    /// <summary>Gets the crafting recipes built from <c>recipes.json</c> (see <see cref="Items"/> for the install rules).</summary>
    public static DefinitionRegistry<uint, CraftingRecipe> Recipes => Current.Recipes;

    /// <summary>Gets the item catalog behind <see cref="Items"/>.</summary>
    public static ItemCatalog ItemCatalog => Current.ItemCatalog;

    /// <summary>Gets the recipe catalog behind <see cref="Recipes"/>.</summary>
    public static RecipeCatalog RecipeCatalog => Current.RecipeCatalog;

    private static Tables Current => s_tables ?? LoadBuiltInDefaults();

    /// <summary>
    /// Replaces the active item and recipe tables atomically; readers on any thread see either the old or the new set.
    /// </summary>
    /// <param name="items">Validated item catalog (server: <c>ConfigRepository.Catalog</c>; desktop: synced config).</param>
    /// <param name="recipes">Recipe catalog compiled against <paramref name="items"/>.</param>
    public static void Install(ItemCatalog items, RecipeCatalog recipes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(recipes);
        s_tables = new Tables(items, items.BuildArchetypeRegistry(), recipes, recipes.Registry);
    }

    private static Tables LoadBuiltInDefaults()
    {
        ItemCatalog items = ItemCatalog.LoadFromJson(ReadEmbedded("items.json"));
        RecipeCatalog recipes = RecipeCatalog.LoadFromJson(ReadEmbedded("recipes.json"), items);
        Tables tables = new(items, items.BuildArchetypeRegistry(), recipes, recipes.Registry);
        _ = Interlocked.CompareExchange(ref s_tables, tables, null);
        return s_tables;
    }

    private static string ReadEmbedded(string fileName)
    {
        string resource = "Carbon.Items.Config." + fileName;
        using Stream stream = typeof(StarterGameCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded default config '{resource}' is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private sealed record Tables(
        ItemCatalog ItemCatalog,
        DefinitionRegistry<uint, ItemArchetype> Items,
        RecipeCatalog RecipeCatalog,
        DefinitionRegistry<uint, CraftingRecipe> Recipes);

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
