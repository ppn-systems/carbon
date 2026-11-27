// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Settlements;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class VillageStructureKitTests
{
    // Villages are built from what players can build (ADR-025, ADR-027): every structure a village can emit, in any
    // style and role, has a placeable kit item that selects it in the build tool. Positive check: the emitted types come
    // from the generator itself, so a new furniture piece or palette entry without a kit fails here.
    [Fact]
    public void EveryStructureAVillageCanEmit_HasAPlaceableKit()
    {
        ItemRules rules = ItemRules.LoadShippedDefaults();
        StructureCatalog structures = StructureCatalog.LoadEmbedded();
        HashSet<StructureType> emitted = [];
        List<VillagePlacement> placements = [];
        foreach (VillageStyle style in Enum.GetValues<VillageStyle>())
        {
            foreach (VillageBuildingRole role in Enum.GetValues<VillageBuildingRole>())
            {
                for (ulong seed = 1; seed <= 200; seed++)
                {
                    VillageBuildingTemplate template = VillageBuildingGenerator.Generate(role, seed);
                    (int w, int h) = template.RotatedSize(0);
                    VillagePlot plot = new(template, role, style, 0, 0, 0, (byte)w, (byte)h);
                    placements.Clear();
                    VillageBlueprint.Expand(VillageBuildingCatalog.Default, structures, plot, placements);
                    foreach (VillagePlacement p in placements)
                    {
                        _ = emitted.Add(p.Type);
                    }
                }
            }
        }

        Assert.True(emitted.Count >= 10, $"only {emitted.Count} structure types seen");
        foreach (StructureType type in emitted)
        {
            uint kit = rules.ResolveStructureKitItemId((byte)type);
            Assert.True(kit != 0U, $"villages build {type}, but no placeable kit item places it");
            Assert.True(rules.Items.TryGet(kit, out ItemArchetype? item) && item.Category == ItemCategory.Placeable, $"the kit of {type} is not a placeable item");
            Assert.True(structures.TryGet(type, out StructureDefinition def) && def.Cost.Count > 0, $"{type} has no build cost in structures.json");
        }
    }
}
