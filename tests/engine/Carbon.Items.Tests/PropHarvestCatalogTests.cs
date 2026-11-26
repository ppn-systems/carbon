// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Props;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class PropHarvestCatalogTests
{
    // Props that are scenery, not resources.
    private static readonly HashSet<MapPropType> s_notHarvestable = [MapPropType.None, MapPropType.AncientShrine];

    // Expected theme tag of the drop for each kind of prop.
    private static readonly Dictionary<MapPropType, string> s_expectedTheme = new()
    {
        [MapPropType.OakTree] = "wood",
        [MapPropType.PineTree] = "wood",
        [MapPropType.SnowPine] = "wood",
        [MapPropType.PalmTree] = "wood",
        [MapPropType.BirchTree] = "wood",
        [MapPropType.WillowTree] = "wood",
        [MapPropType.DeadTree] = "wood",
        [MapPropType.AcaciaTree] = "wood",
        [MapPropType.CherryBlossomTree] = "wood",
        [MapPropType.Boulder] = "stone",
        [MapPropType.BasaltPillar] = "stone",
        [MapPropType.CopperVein] = "ore",
        [MapPropType.IronVein] = "ore",
        [MapPropType.SilverVein] = "ore",
        [MapPropType.GoldVein] = "ore",
        [MapPropType.CoalDeposit] = "ore",
        [MapPropType.CrystalCluster] = "crystal",
        [MapPropType.GlacialCrystal] = "crystal",
        [MapPropType.Cactus] = "plant",
        [MapPropType.Bush] = "plant",
        [MapPropType.MushroomCluster] = "plant",
        [MapPropType.DryBush] = "plant",
        [MapPropType.DuneGrass] = "plant",
    };

    [Fact]
    public void ResolveDrop_EveryHarvestablePropType_DropsAThemedItemFromItemsJson()
    {
        foreach (MapPropType type in Enum.GetValues<MapPropType>())
        {
            ItemSlot drop = PropHarvestCatalog.ResolveDrop(type, out uint xp);
            if (s_notHarvestable.Contains(type))
            {
                drop.IsEmpty.Should().BeTrue($"{type} is scenery");
                continue;
            }

            // A new prop type must get a drop (and an expected theme here) before it ships.
            drop.IsEmpty.Should().BeFalse($"{type} is harvestable and needs a themed drop in PropHarvestCatalog");
            xp.Should().BePositive($"{type} grants harvest XP");
            StarterGameCatalog.ItemCatalog.TryGet(drop.ItemId, out ItemCatalogEntry? item).Should().BeTrue($"{type} drops an items.json item");
            s_expectedTheme.Should().ContainKey(type, $"add the expected drop theme for {type} to this test");
            item!.Tags.Should().Contain(s_expectedTheme[type], $"{type} dropped '{item.Code}'");
        }
    }

    [Theory]
    [InlineData(MapPropType.Cactus, "cactus_fiber")]
    [InlineData(MapPropType.Boulder, "stone_chunk")]
    [InlineData(MapPropType.OakTree, "wood_log")]
    [InlineData(MapPropType.CopperVein, "copper_ore")]
    [InlineData(MapPropType.CoalDeposit, "coal")]
    [InlineData(MapPropType.DryBush, "plant_fiber")]
    [InlineData(MapPropType.DuneGrass, "plant_fiber")]
    public void ResolveDrop_UsesTheEnumNotRawNumbers(MapPropType type, string expectedCode)
    {
        ItemSlot drop = PropHarvestCatalog.ResolveDrop(type, out _);
        drop.ItemId.Should().Be(StarterGameCatalog.ItemCatalog.ResolveId(expectedCode));
    }
}
