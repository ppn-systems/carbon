// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Carbon.Combat.Attacks;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Maps.Biomes;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

public sealed class MonsterCatalogTests
{
    [Fact]
    public void FromJson_ShippedMonstersJson_ContainsRequiredMonstersAndEliteVariant()
    {
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();

        string[] required = ["wild_slime", "rock_golem", "frost_wolf", "ash_wraith", "skeleton_archer", "orc_shaman", "crimson_demon", "slime_king"];
        foreach (string id in required)
        {
            catalog.TryGetIndex(id, out _).Should().BeTrue($"'{id}' is a required monster");
        }

        catalog.TryGetEliteVariant(catalog.GetIndex("wild_slime"), out ushort elite).Should().BeTrue();
        catalog.GetDefinition(elite).Id.Should().Be("slime_king");
        catalog.GetDefinition(elite).IsElite.Should().BeTrue();
    }

    [Fact]
    public void FromJson_ShippedMonstersJson_EveryMonsterHasLootTableVisualKeyAndValidEnums()
    {
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();
        HashSet<string> lootTables = CreatureTestData.LoadLootTableIds();

        foreach (MonsterDefinition def in catalog.Definitions)
        {
            def.LootTableId.Should().NotBeNullOrWhiteSpace();
            lootTables.Should().Contain(def.LootTableId, $"'{def.Id}' must reference an economy loot.json table");
            def.VisualArchetype.Should().NotBeNullOrWhiteSpace("the client picks sprites by visual archetype key");
            def.ArchetypeId.Should().BeInRange(101, 199, "monsters use the WorldActorCatalog monster archetype range");
            def.PreferredBiomes.Should().NotBeEmpty().And.OnlyContain(b => Enum.IsDefined(b));
            def.Attacks.Should().NotBeEmpty().And.OnlyContain(a => Enum.IsDefined(a.Kind) && Enum.IsDefined(a.Shape));
        }
    }

    [Fact]
    public void FromJson_ShippedMonstersJson_UsesEveryAttackKind()
    {
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();

        HashSet<AttackKind> kinds = [];
        foreach (AttackProfile profile in catalog.AttackProfiles)
        {
            kinds.Add(profile.Kind);
        }

        kinds.Should().Contain(Enum.GetValues<AttackKind>(), "every attack kind is exercised by shipped data");
    }

    [Fact]
    public void FromJson_UnknownEnumName_ThrowsJsonException()
    {
        string json = CreatureTestData.MonstersJson.Replace("\"kind\": \"Leap\"", "\"kind\": \"Teleport\"", StringComparison.Ordinal);

        Action load = () => MonsterCatalog.FromJson(json);

        load.Should().Throw<JsonException>("attack kinds are parsed by enum name");
    }

    [Fact]
    public void FromJson_MissingLootTable_ThrowsArgumentException()
    {
        string json = CreatureTestData.MonstersJson.Replace("\"lootTableId\": \"golem\"", "\"lootTableId\": \"\"", StringComparison.Ordinal);

        Action load = () => MonsterCatalog.FromJson(json);

        load.Should().Throw<ArgumentException>().WithMessage("*rock_golem*lootTableId*");
    }

    [Fact]
    public void FromJson_ShippedSpawningJson_TablesReferenceMonstersThatPreferTheBiome()
    {
        MonsterCatalog catalog = CreatureTestData.LoadCatalog();
        SpawningConfig config = CreatureTestData.LoadSpawning();

        config.SpawnTables.Should().NotBeEmpty();
        config.SpawnTables.Select(t => t.Biome).Should().NotContain([BiomeType.DeepOcean, BiomeType.ShallowWater]);
        foreach (SpawnTable table in config.SpawnTables)
        {
            foreach (SpawnTableEntry entry in table.Entries)
            {
                catalog.TryGetIndex(entry.Monster, out ushort index).Should().BeTrue($"'{entry.Monster}' must exist");
                catalog.GetDefinition(index).PreferredBiomes.Should().Contain(table.Biome, $"'{entry.Monster}' spawns in {table.Biome}");
            }
        }
    }
}
