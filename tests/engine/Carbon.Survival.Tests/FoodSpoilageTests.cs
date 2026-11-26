// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Items.Containers;
using Carbon.Survival.Food;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

public sealed class FoodSpoilageTests
{
    private const uint RawMeatId = 121;
    private const uint RottenId = 203;

    private static readonly FoodDefinition s_rawMeat = new()
    {
        ItemId = "raw_meat",
        Nutrition = 25f,
        Quality = FoodQuality.Raw,
        EatSeconds = 4f,
        SpoilSeconds = 10f,
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
    public void Advance_UnstampedPerishable_StampsFullSpoilTime()
    {
        SpoilageStep step = FoodSpoilage.Advance(new ItemSlot(RawMeatId, 3), s_rawMeat, 1f, RottenId);

        step.Outcome.Should().Be(SpoilageOutcome.Stamped);
        step.Slot.FreshnessSeconds.Should().Be(10f);
        step.Slot.Quantity.Should().Be(3);
    }

    [Fact]
    public void Advance_OverTime_AgesThenSpoilsIntoRottenFoodKeepingQuantity()
    {
        ItemSlot slot = FoodSpoilage.Advance(new ItemSlot(RawMeatId, 3), s_rawMeat, 1f, RottenId).Slot;

        for (int second = 1; second < 10; second++)
        {
            SpoilageStep step = FoodSpoilage.Advance(slot, s_rawMeat, 1f, RottenId);
            step.Outcome.Should().Be(SpoilageOutcome.Aging, $"second {second} of 10");
            slot = step.Slot;
        }

        slot.FreshnessSeconds.Should().BeApproximately(1f, 0.001f);
        SpoilageStep spoiled = FoodSpoilage.Advance(slot, s_rawMeat, 1f, RottenId);
        spoiled.Outcome.Should().Be(SpoilageOutcome.Spoiled);
        spoiled.Slot.ItemId.Should().Be(RottenId);
        spoiled.Slot.Quantity.Should().Be(3);
        spoiled.Slot.IsPerishable.Should().BeFalse("rotten food does not spoil further");
    }

    [Fact]
    public void Advance_WithoutSpoilTarget_RemovesTheStack()
    {
        ItemSlot slot = new(RawMeatId, 2, FreshnessSeconds: 0.5f);

        SpoilageStep step = FoodSpoilage.Advance(slot, s_rawMeat, 1f, spoiledItemId: 0U);

        step.Outcome.Should().Be(SpoilageOutcome.Spoiled);
        step.Slot.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Advance_NonPerishableFood_IsUnchanged()
    {
        ItemSlot bread = new(123, 4);

        SpoilageStep step = FoodSpoilage.Advance(bread, s_bread, 3600f, RottenId);

        step.Outcome.Should().Be(SpoilageOutcome.Unchanged);
        step.Slot.Should().Be(bread);
    }

    [Fact]
    public void FoodCatalog_InvalidSpoilSettings_Throw()
    {
        Action spoilsIntoWithoutTimer = () => _ = new FoodCatalog(
            [new FoodDefinition { ItemId = "bread", Nutrition = 35f, EatSeconds = 3f, SpoilsInto = "rotten_food" }], 100f);
        Action chain = () => _ = new FoodCatalog(
            [
                new FoodDefinition { ItemId = "a", Nutrition = 5f, EatSeconds = 1f, SpoilSeconds = 10f, SpoilsInto = "b" },
                new FoodDefinition { ItemId = "b", Nutrition = 5f, EatSeconds = 1f, SpoilSeconds = 10f },
            ],
            100f);
        Action tooLong = () => _ = new FoodCatalog(
            [new FoodDefinition { ItemId = "jerky", Nutrition = 5f, EatSeconds = 1f, SpoilSeconds = FoodCatalog.MaxSpoilSeconds + 1f }], 100f);

        spoilsIntoWithoutTimer.Should().Throw<InvalidDataException>().WithMessage("*spoilsInto*");
        chain.Should().Throw<InvalidDataException>().WithMessage("*itself spoils*");
        tooLong.Should().Throw<InvalidDataException>().WithMessage("*spoilSeconds*");
    }
}
