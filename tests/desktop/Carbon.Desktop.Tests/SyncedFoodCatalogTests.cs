// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Survival.Food;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class SyncedFoodCatalogTests
{
    private static readonly FoodDefinition s_rawMeat = new()
    {
        ItemId = "raw_meat",
        Nutrition = 25f,
        Quality = FoodQuality.Raw,
        EatSeconds = 4f,
        SpoilSeconds = 1200f,
        SpoilsInto = "rotten_food",
    };

    private static readonly FoodDefinition s_bread = new()
    {
        ItemId = "bread",
        Nutrition = 35f,
        Quality = FoodQuality.Simple,
        EatSeconds = 3f,
    };

    [Fact]
    public void FormatTooltip_PerishableStack_ShowsNutritionAndRemainingTime()
    {
        SyncedFoodCatalog.FormatTooltip(s_rawMeat, 690f).Should().Be("+25 food  Spoils in 12m");
        SyncedFoodCatalog.FormatTooltip(s_rawMeat, 0f).Should().Be("+25 food  Spoils in 20m", "an unstamped stack shows the full spoil time");
    }

    [Fact]
    public void FormatTooltip_NonPerishableFood_ShowsOnlyNutrition() =>
        SyncedFoodCatalog.FormatTooltip(s_bread, 0f).Should().Be("+35 food");

    [Theory]
    [InlineData(45f, "45s")]
    [InlineData(61f, "2m")]
    [InlineData(3900f, "1h 05m")]
    public void FormatDuration_PicksTheLargestUnit(float seconds, string expected) =>
        SyncedFoodCatalog.FormatDuration(seconds).Should().Be(expected);
}
