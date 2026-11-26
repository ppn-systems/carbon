// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Containers;

namespace Carbon.Items.Catalog;

/// <summary>
/// Data-driven lookup mapping <c>MapPropType</c> byte codes to harvest loot items without leaking map types into <c>Carbon.Items</c>.
/// </summary>
public static class PropHarvestCatalog
{
    /// <summary>Resolves dropped item slot and XP reward for a destroyed prop type code.</summary>
    public static ItemSlot ResolveDropForPropTypeCode(byte propTypeCode, out uint xpReward)
    {
        // MapPropType ordinals: 1=OakTree..14=CrystalCluster..22=CherryBlossomTree
        switch (propTypeCode)
        {
            case 1:  // OakTree
            case 2:  // PineTree
            case 3:  // SnowPine
            case 4:  // PalmTree
            case 5:  // BirchTree
            case 6:  // WillowTree
            case 7:  // DeadTree
            case 21: // AcaciaTree
            case 22: // CherryBlossomTree
                xpReward = 15U;
                return new ItemSlot(StarterGameCatalog.WoodLog, 3);

            case 8:  // Boulder
            case 18: // BasaltPillar
            case 19: // CoalDeposit
                xpReward = 15U;
                return new ItemSlot(StarterGameCatalog.StoneChunk, 3);

            case 9:  // Bush
            case 10: // Cactus
            case 20: // MushroomCluster
                xpReward = 10U;
                return new ItemSlot(StarterGameCatalog.BerryHerb, 2);

            case 11: // CopperVein
            case 12: // IronVein
            case 13: // GoldVein
            case 16: // SilverVein
                xpReward = 20U;
                return new ItemSlot(StarterGameCatalog.IronOre, 2);

            case 14: // CrystalCluster
            case 17: // GlacialCrystal
                xpReward = 28U;
                return new ItemSlot(StarterGameCatalog.ManaCrystal, 2);

            case 23: // DryBush
                xpReward = 6U;
                return new ItemSlot(StarterGameCatalog.WoodLog, 1);

            default:
                xpReward = 0U;
                return ItemSlot.Empty;
        }
    }
}
