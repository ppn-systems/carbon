// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Props;

namespace Carbon.Items.Catalog;

/// <summary>
/// Maps each harvestable <see cref="MapPropType"/> to its themed drop: trees → wood, boulders → stone, veins → their ore,
/// crystals → mana crystal, cactus → cactus fiber, bushes → berries. Items are resolved by code from the active
/// <c>items.json</c> catalog (<see cref="StarterGameCatalog.ItemCatalog"/>), never from raw numeric ids.
/// </summary>
public static class PropHarvestCatalog
{
    /// <summary>Resolves the dropped item stack and XP reward for a destroyed prop.</summary>
    /// <param name="propType">Destroyed prop type.</param>
    /// <param name="xpReward">Harvest XP (<c>0</c> when the prop yields nothing).</param>
    /// <returns>The dropped stack, or <see cref="ItemSlot.Empty"/> for props that yield nothing.</returns>
    public static ItemSlot ResolveDrop(MapPropType propType, out uint xpReward)
    {
        (string? code, int quantity, uint xp) = propType switch
        {
            MapPropType.OakTree or MapPropType.PineTree or MapPropType.SnowPine or MapPropType.PalmTree
                or MapPropType.BirchTree or MapPropType.WillowTree or MapPropType.DeadTree
                or MapPropType.AcaciaTree or MapPropType.CherryBlossomTree => ("wood_log", 3, 15U),
            MapPropType.Boulder or MapPropType.BasaltPillar => ("stone_chunk", 3, 15U),
            MapPropType.CoalDeposit => ("coal", 3, 15U),
            MapPropType.CopperVein => ("copper_ore", 2, 20U),
            MapPropType.IronVein or MapPropType.SilverVein => ("iron_ore", 2, 20U),
            MapPropType.GoldVein => ("rough_gem", 1, 30U),
            MapPropType.CrystalCluster or MapPropType.GlacialCrystal => ("mana_crystal", 2, 28U),
            MapPropType.Cactus => ("cactus_fiber", 2, 10U),
            MapPropType.Bush => ("berries", 2, 10U),
            MapPropType.MushroomCluster => ("berry_herb", 2, 10U),
            MapPropType.None or MapPropType.AncientShrine => (null, 0, 0U),
            _ => (null, 0, 0U),
        };

        if (code is null || !StarterGameCatalog.ItemCatalog.TryGetByCode(code, out ItemCatalogEntry? item))
        {
            xpReward = 0U;
            return ItemSlot.Empty;
        }

        xpReward = xp;
        return new ItemSlot(item.Id, quantity);
    }
}
