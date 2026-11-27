// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World.Needs;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class NeedsServiceTests
{
    private const float TileSize = 32f;

    [Fact]
    public void AdvanceSpoilage_UnstampedPerishable_StampsAndReportsTheSlot()
    {
        Harness h = new();
        InventoryContainer inventory = new(4);
        inventory.SetSlot(2, new ItemSlot(h.RawMeatId, 3));
        List<(ushort SlotIndex, ItemSlot Slot)> changed = [];

        h.Needs.AdvanceSpoilage(inventory, 1f, changed);

        changed.Should().ContainSingle().Which.SlotIndex.Should().Be(2);
        inventory.GetSlot(2).IsPerishable.Should().BeTrue("a first-seen perishable stack is stamped with its spoil time");
        changed[0].Slot.Should().Be(inventory.GetSlot(2));
    }

    [Fact]
    public void AdvanceSpoilage_ExpiringPerishable_TurnsIntoItsSpoilTarget()
    {
        Harness h = new();
        InventoryContainer inventory = new(4);
        inventory.SetSlot(0, new ItemSlot(h.RawMeatId, 3, FreshnessSeconds: 0.5f));
        List<(ushort SlotIndex, ItemSlot Slot)> changed = [];

        h.Needs.AdvanceSpoilage(inventory, 1f, changed);

        inventory.GetSlot(0).ItemId.Should().Be(h.RottenFoodId);
        inventory.GetSlot(0).Quantity.Should().Be(3);
        changed.Should().ContainSingle();
    }

    [Fact]
    public void AdvanceSpoilage_NonFoodOrNonPerishable_LeavesInventoryAlone()
    {
        Harness h = new();
        InventoryContainer inventory = new(4);
        inventory.SetSlot(0, new ItemSlot(h.CookedMeatId, 1));
        List<(ushort SlotIndex, ItemSlot Slot)> changed = [];

        h.Needs.AdvanceSpoilage(inventory, 3600f, changed);

        changed.Should().BeEmpty();
        inventory.GetSlot(0).Should().Be(new ItemSlot(h.CookedMeatId, 1));
    }

    [Fact]
    public void TryGetFoodCode_FoodItem_ReturnsItsCode()
    {
        Harness h = new();

        h.Needs.TryGetFoodCode(h.RawMeatId, out string code).Should().BeTrue();
        code.Should().Be("raw_meat");
        h.Needs.TryGetFoodCode(uint.MaxValue, out _).Should().BeFalse();
    }

    [Fact]
    public void Restore_NeverSavedNeeds_StartsFull()
    {
        Harness h = new();

        NeedsComponent needs = h.Needs.Restore(Character(needsFood: -1f));

        needs.Food.Should().Be(needs.FoodMax);
    }

    [Fact]
    public void Restore_OutOfRangeStoredValues_AreClamped()
    {
        Harness h = new();

        NeedsComponent needs = h.Needs.Restore(Character(needsFood: 1_000_000f, malnutrition: 5f));

        needs.Food.Should().Be(needs.FoodMax);
        needs.Malnutrition.Should().Be(1f);
    }

    [Fact]
    public void ResetAfterDeath_StarvedPlayer_RespawnsWithAtLeastHalfFood()
    {
        Harness h = new();
        Entity player = h.Simulation.SpawnEntity(Vector2.Zero, 7f);
        NeedsComponent starving = NeedsComponent.CreateFull(h.Needs.Config);
        starving.Food = 0f;
        h.Simulation.Entities.SetComponent(player, starving);

        h.Needs.ResetAfterDeath(player);

        h.Simulation.Entities.GetComponent<NeedsComponent>(player).Food
            .Should().Be(h.Needs.Config.FoodMax * NeedsService.RespawnFoodFraction);
    }

    [Fact]
    public void TryFindBed_TwoIntactBedsInReach_PicksTheClosest()
    {
        Harness h = new();
        h.Buildings.ForcePlaceStructure(1, 0, StructureType.WoodBed);
        h.Buildings.ForcePlaceStructure(0, 0, StructureType.WoodBed);
        Vector2 nearFirstBed = new(0.6f * TileSize, 0.5f * TileSize);

        h.Needs.TryFindBed(nearFirstBed, out int bedX, out int bedY).Should().BeTrue();

        (bedX, bedY).Should().Be((0, 0));
        h.Needs.TryFindBed(new Vector2(20f * TileSize, 0f), out _, out _).Should().BeFalse("no bed is within reach");
    }

    private static CharacterRecord Character(float needsFood, float malnutrition = 0f) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Tester",
        1,
        default,
        0f,
        0f,
        100f,
        100f,
        0,
        [],
        DateTimeOffset.UnixEpoch,
        NeedsFood: needsFood,
        NeedsMalnutrition: malnutrition);

    private sealed class Harness
    {
        public Harness()
        {
            ItemRules rules = ItemRules.LoadShippedDefaults();
            FoodCatalog food = new(
                [
                    new FoodDefinition { ItemId = "raw_meat", Nutrition = 25f, EatSeconds = 4f, SpoilSeconds = 1200f, SpoilsInto = "rotten_food" },
                    new FoodDefinition { ItemId = "cooked_meat", Nutrition = 40f, EatSeconds = 4f },
                    new FoodDefinition { ItemId = "rotten_food", Nutrition = 5f, EatSeconds = 2f },
                ],
                100f);
            this.RawMeatId = IdOf(rules, "raw_meat");
            this.CookedMeatId = IdOf(rules, "cooked_meat");
            this.RottenFoodId = IdOf(rules, "rotten_food");
            this.Needs = new NeedsService(this.Simulation, this.Buildings, TileSize, () => rules, () => null, new NeedsConfig(), food, TestGameplayEntropy.Create(42UL));
        }

        public WorldSimulation Simulation { get; } = new();

        public BuildingGrid Buildings { get; } = new();

        public NeedsService Needs { get; }

        public uint RawMeatId { get; }

        public uint CookedMeatId { get; }

        public uint RottenFoodId { get; }

        private static uint IdOf(ItemRules rules, string code)
        {
            ReadOnlySpan<ItemArchetype> all = rules.Items.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Code == code)
                {
                    return all[i].Id;
                }
            }

            throw new InvalidOperationException($"Shipped item '{code}' not found.");
        }
    }
}
