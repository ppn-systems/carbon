// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;

namespace Carbon.Items.Catalog;

/// <summary>
/// Stable item id constants and the starter loadout. Item stats and recipes are data (<c>items.json</c>,
/// <c>recipes.json</c>) and live in an <see cref="ItemRules"/> instance owned by the server world or the client session.
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
}
