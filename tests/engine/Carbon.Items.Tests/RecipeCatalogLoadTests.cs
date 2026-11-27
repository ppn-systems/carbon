// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>
/// The shipped <c>recipes.json</c> loads through the strict items JSON context
/// (<c>UnmappedMemberHandling.Disallow</c>), so a key the loader does not read cannot sit in the file unnoticed.
/// </summary>
public sealed class RecipeCatalogLoadTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly string s_recipesJson = ItemRules.ReadShippedDocument("recipes.json");

    [Fact]
    public void LoadFromJson_ShippedRecipes_LoadsEveryRecipe()
    {
        RecipeCatalog catalog = RecipeCatalog.LoadFromJson(s_recipesJson, s_rules.ItemCatalog);

        catalog.Count.Should().Be(70);
        catalog.TryGetByCode("wood_plank", out CraftingRecipe? plank).Should().BeTrue();
        plank!.Station.Should().Be(CraftingStation.None);
        plank.Skill.Should().Be(CraftingSkill.Carpentry);
        plank.UnlockLevel.Should().Be(1);
    }

    [Fact]
    public void LoadFromJson_UnknownRecipeKey_IsRejected()
    {
        string json = InsertIntoFirstRecipe(s_recipesJson, "\"bogus\": 1,");

        Action load = () => RecipeCatalog.LoadFromJson(json, s_rules.ItemCatalog);

        load.Should().Throw<InvalidDataException>().WithMessage("*bogus*");
    }

    [Fact]
    public void LoadFromJson_WorkTicksKey_IsRejected()
    {
        string json = InsertIntoFirstRecipe(s_recipesJson, "\"workTicks\": 30,");

        Action load = () => RecipeCatalog.LoadFromJson(json, s_rules.ItemCatalog);

        load.Should().Throw<InvalidDataException>("crafting is instant; recipes carry no work amount").WithMessage("*workTicks*");
    }

    private static string InsertIntoFirstRecipe(string json, string member)
    {
        const string Anchor = "\"code\": \"wood_plank\",";
        json.Should().Contain(Anchor);
        return json.Replace(Anchor, Anchor + member, StringComparison.Ordinal);
    }
}
