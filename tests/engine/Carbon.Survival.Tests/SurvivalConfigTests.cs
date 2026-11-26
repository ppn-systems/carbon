// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

public sealed class SurvivalConfigTests
{
    private static readonly string[] s_expectedFoods = ["berries", "raw_meat", "cooked_meat", "bread", "stew", "slime_jelly", "roast_meat", "arcane_pie"];

    [Fact]
    public void LoadFromJson_RepositoryNeedsJson_LoadsAndMatchesDocumentedTimeline()
    {
        NeedsConfig config = NeedsConfig.LoadFromJson(File.ReadAllText(SurvivalTestWorld.FindConfigFile("needs.json")));

        float walkingMinutesToStarving = config.FoodMax / (config.FoodDecayPerMinute * config.FoodActivity.Walking);
        walkingMinutesToStarving.Should().BeApproximately(60f, 0.1f, "docs/survival-needs.md documents 60 minutes");
        (1f / config.MalnutritionGainPerMinute).Should().BeApproximately(10f, 0.001f, "starving → death in 10 minutes");
        config.UrgentlyHungryThreshold.Should().BeLessThan(config.HungryThreshold);
        config.FullThreshold.Should().BeInRange(config.HungryThreshold, 1f);
        config.MoodBaseline.Should().BeInRange(0f, 100f);
        config.FoodActivity.Sprinting.Should().BeGreaterThan(config.FoodActivity.Walking);
        config.FoodActivity.Idle.Should().BeLessThan(config.FoodActivity.Walking);
    }

    [Fact]
    public void LoadFromJson_RepositoryFoodJson_HasEveryFoodWithValuesInRange()
    {
        NeedsConfig config = NeedsConfig.LoadFromJson(File.ReadAllText(SurvivalTestWorld.FindConfigFile("needs.json")));
        FoodCatalog catalog = FoodCatalog.LoadFromJson(File.ReadAllText(SurvivalTestWorld.FindConfigFile("food.json")), config.FoodMax);

        catalog.Count.Should().Be(s_expectedFoods.Length);
        foreach (string id in s_expectedFoods)
        {
            catalog.TryGetIndex(id, out _).Should().BeTrue($"food.json must define {id}");
        }

        foreach (FoodDefinition food in catalog.All)
        {
            food.Nutrition.Should().BeInRange(0.01f, config.FoodMax, food.ItemId);
            food.PoisonChance.Should().BeInRange(0f, 1f, food.ItemId);
            food.EatSeconds.Should().BeInRange(0.1f, FoodCatalog.MaxEatSeconds, food.ItemId);
            MathF.Abs(food.MoodOffset).Should().BeLessThanOrEqualTo(FoodCatalog.MaxMoodOffset, food.ItemId);
            food.MoodDurationSeconds.Should().BeGreaterThanOrEqualTo(0f, food.ItemId);
            if (food.Quality != FoodQuality.Raw)
            {
                food.MoodOffset.Should().BeGreaterThanOrEqualTo(0f, $"cooked {food.ItemId} must not be a mood penalty");
            }
        }

        FoodDefinition raw = catalog.Get(Index(catalog, "raw_meat"));
        FoodDefinition cooked = catalog.Get(Index(catalog, "cooked_meat"));
        cooked.Nutrition.Should().BeGreaterThan(raw.Nutrition, "cooking adds value");
        cooked.PoisonChance.Should().BeLessThan(raw.PoisonChance, "cooking reduces poisoning");
    }

    [Fact]
    public void FoodJson_EveryFood_ExistsInItemsJsonWithFoodTag()
    {
        NeedsConfig config = NeedsConfig.LoadFromJson(File.ReadAllText(SurvivalTestWorld.FindConfigFile("needs.json")));
        FoodCatalog catalog = FoodCatalog.LoadFromJson(File.ReadAllText(SurvivalTestWorld.FindConfigFile("food.json")), config.FoodMax);

        using JsonDocument items = JsonDocument.Parse(File.ReadAllText(SurvivalTestWorld.FindConfigFile("items.json")));
        Dictionary<string, JsonElement> byCode = new(StringComparer.Ordinal);
        foreach (JsonElement item in items.RootElement.GetProperty("items").EnumerateArray())
        {
            byCode[item.GetProperty("code").GetString()!] = item;
        }

        foreach (FoodDefinition food in catalog.All)
        {
            byCode.TryGetValue(food.ItemId, out JsonElement item).Should().BeTrue($"food.json '{food.ItemId}' must be an item in items.json");
            List<string> tags = [];
            foreach (JsonElement tag in item.GetProperty("tags").EnumerateArray())
            {
                tags.Add(tag.GetString()!);
            }

            tags.Should().Contain("food", $"items.json '{food.ItemId}' must carry the food tag");
        }

        foreach (JsonElement item in byCode.Values)
        {
            item.TryGetProperty("nutrition", out _).Should().BeFalse(
                $"food.json is the single source of nutrition; items.json '{item.GetProperty("code").GetString()}' must not define it");
        }
    }

    [Fact]
    public void Validate_MisorderedThresholds_Throws()
    {
        NeedsConfig config = new() { HungryThreshold = 0.1f, UrgentlyHungryThreshold = 0.2f };

        Action act = config.Validate;

        act.Should().Throw<InvalidDataException>().WithMessage("*urgentlyHungryThreshold*");
    }

    [Fact]
    public void LoadFromJson_UnknownProperty_Throws()
    {
        Action act = () => NeedsConfig.LoadFromJson("{ \"foodMaxx\": 100 }");

        act.Should().Throw<Exception>("typos in needs.json must fail loudly");
    }

    [Fact]
    public void FoodCatalog_DuplicateOrOutOfRangeEntries_Throw()
    {
        FoodDefinition bread = new() { ItemId = "bread", Nutrition = 35f, Quality = FoodQuality.Simple, EatSeconds = 3f };

        Action duplicate = () => _ = new FoodCatalog([bread, bread], 100f);
        Action tooNutritious = () => _ = new FoodCatalog([new FoodDefinition { ItemId = "feast", Nutrition = 500f, EatSeconds = 3f }], 100f);
        Action badChance = () => _ = new FoodCatalog([new FoodDefinition { ItemId = "rot", Nutrition = 5f, PoisonChance = 1.5f, EatSeconds = 1f }], 100f);

        duplicate.Should().Throw<InvalidDataException>().WithMessage("*duplicate*");
        tooNutritious.Should().Throw<InvalidDataException>().WithMessage("*nutrition*");
        badChance.Should().Throw<InvalidDataException>().WithMessage("*poisonChance*");
    }

    private static int Index(FoodCatalog catalog, string id)
    {
        catalog.TryGetIndex(id, out int index).Should().BeTrue();
        return index;
    }
}
