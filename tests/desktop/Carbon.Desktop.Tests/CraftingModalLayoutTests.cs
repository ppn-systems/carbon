// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class CraftingModalLayoutTests
{
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 600)]
    [InlineData(2560, 1440)]
    public void GetIngredientRect_EveryIngredientOfEveryRecipe_GetsItsOwnRowAboveTheCraftButtons(int width, int height)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(width, height);
        float buttonTop = CraftingModalWidget.GetCraft1ButtonRect(in layout).Y;
        int maxIngredients = 0;
        foreach (CraftingRecipe recipe in ItemRules.LoadShippedDefaults().Recipes.All)
        {
            maxIngredients = System.Math.Max(maxIngredients, recipe.Ingredients.Count);
        }

        maxIngredients.Should().BeGreaterThan(3, "recipes.json has recipes with 4 ingredients (crystal_bow, abyssal_crown)");

        for (int count = 1; count <= maxIngredients + 2; count++)
        {
            float previousBottom = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                RectangleF row = CraftingModalWidget.GetIngredientRect(in layout, i, count);
                row.Height.Should().BeGreaterThan(0f);
                row.Y.Should().BeGreaterThanOrEqualTo(previousBottom, $"row {i}/{count} must not overlap the previous row at {width}x{height}");
                (row.Y + row.Height).Should().BeLessThanOrEqualTo(buttonTop, $"row {i}/{count} must stay above the craft buttons at {width}x{height}");
                previousBottom = row.Y + row.Height;
            }
        }
    }

    [Fact]
    public void GetIngredientRect_FewIngredients_KeepNaturalRowHeight()
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(1920, 1080);
        RectangleF one = CraftingModalWidget.GetIngredientRect(in layout, 0, 1);
        RectangleF three = CraftingModalWidget.GetIngredientRect(in layout, 0, 3);
        three.Height.Should().Be(one.Height, "rows only shrink when they would not fit");
    }
}
