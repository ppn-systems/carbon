// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using Carbon.Maps.Building;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class StructureCatalogTests
{
    [Fact]
    public void LoadFromFile_RepositoryStructuresJson_DefinesEveryStructureTypeWithValidValues()
    {
        StructureCatalog catalog = ConstructionFixtures.Catalog;

        foreach (StructureType type in Enum.GetValues<StructureType>().Where(t => t != StructureType.None))
        {
            catalog.TryGet(type, out StructureDefinition definition).Should().BeTrue($"{type} needs a definition");
            Enum.IsDefined(definition.Category).Should().BeTrue();
            Enum.IsDefined(definition.Material).Should().BeTrue();
            Enum.IsDefined(definition.Affordance).Should().BeTrue();
            definition.Cost.Should().NotBeEmpty($"{definition.Id} must cost materials");
            definition.Cost.Should().OnlyContain(c => c.Quantity > 0 && !string.IsNullOrWhiteSpace(c.ItemId));
            definition.WorkAmount.Should().BePositive();
            definition.HitPoints.Should().BePositive();
        }

        catalog.Rules.DeconstructRefundFraction.Should().Be(0.75f);
        catalog.Rules.DestroyedDropFraction.Should().BeLessThan(catalog.Rules.DeconstructRefundFraction);
    }

    [Fact]
    public void LoadFromFile_RepositoryStructuresJson_MatchesGridDefaultsUsedByWireDecoding()
    {
        // ChunkDeltaPacketMapper rebuilds structures from BuildingGrid.CreateBlueprint, so both sources must agree.
        foreach (StructureDefinition definition in ConstructionFixtures.Catalog.Definitions)
        {
            StructureTile fromJson = definition.CreateTile();
            StructureTile fromGrid = BuildingGrid.CreateBlueprint(definition.Type);

            fromJson.Category.Should().Be(fromGrid.Category, definition.Id);
            fromJson.Material.Should().Be(fromGrid.Material, definition.Id);
            fromJson.MaxDurability.Should().Be(fromGrid.MaxDurability, definition.Id);
            fromJson.Destructibility.HarvestTier.Should().Be(fromGrid.Destructibility.HarvestTier, definition.Id);
            fromJson.MoveSpeedMultiplier.Should().Be(fromGrid.MoveSpeedMultiplier, definition.Id);
        }
    }

    [Fact]
    public void Parse_UnknownEnumName_ThrowsFormatException()
    {
        const string json = """
            { "structures": [ { "id": "x", "type": "LaserWall", "category": "Wall", "material": "Wood",
              "cost": [ { "item": "planks", "qty": 1 } ], "work": 1, "hitPoints": 1, "affordance": "Light" } ] }
            """;

        Action parse = () => StructureCatalog.Parse(json);

        parse.Should().Throw<FormatException>().WithMessage("*LaserWall*");
    }

    [Fact]
    public void Parse_DuplicateType_ThrowsFormatException()
    {
        const string entry = """
            { "id": "ID", "type": "WoodWall", "category": "Wall", "material": "Wood",
              "cost": [ { "item": "planks", "qty": 1 } ], "work": 1, "hitPoints": 1, "affordance": "Light" }
            """;
        string json = "{ \"structures\": [" + entry.Replace("ID", "a", StringComparison.Ordinal) + "," + entry.Replace("ID", "b", StringComparison.Ordinal) + "] }";

        Action parse = () => StructureCatalog.Parse(json);

        parse.Should().Throw<FormatException>().WithMessage("*WoodWall*");
    }
}
