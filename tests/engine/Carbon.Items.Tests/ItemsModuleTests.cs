// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Data.Registry;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Systems;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class ItemsModuleTests
{
    private static DefinitionRegistry<uint, ItemArchetype> CreateSampleRegistry() =>
        new(
        [
            new ItemArchetype(1, "wood_log", ItemCategory.Resource, MaxStackSize: 50),
            new ItemArchetype(2, "stone_chunk", ItemCategory.Resource, MaxStackSize: 50),
            new ItemArchetype(10, "iron_pickaxe", ItemCategory.Tool, MaxStackSize: 1, EquipSlot: EquipmentSlotKind.Tool, HarvestTier: 2, MaxDurability: 200f),
            new ItemArchetype(20, "wood_wall_kit", ItemCategory.Placeable, MaxStackSize: 20, PlaceableStructureType: 1)
        ]);

    [Fact]
    public void InventoryContainer_WithRegistry_RespectsArchetypeMaxStackSize()
    {
        DefinitionRegistry<uint, ItemArchetype> registry = CreateSampleRegistry();
        InventoryContainer bag = new(capacity: 2);

        bag.TryAdd(new ItemSlot(1, 60), registry).Should().Be(0);
        bag.GetSlot(0).Quantity.Should().Be(50);
        bag.GetSlot(1).Quantity.Should().Be(10);

        int remainder = bag.TryAdd(new ItemSlot(1, 50), registry);
        remainder.Should().Be(10);
        bag.CountItem(1).Should().Be(100);
    }

    [Fact]
    public void RecipeMatcher_TryCraft_ConsumesIngredientsAndAddsOutput()
    {
        InventoryContainer bag = new(capacity: 2);
        _ = bag.TryAdd(new ItemSlot(1, 10), maxStackSize: 50);
        _ = bag.TryAdd(new ItemSlot(2, 5), maxStackSize: 50);

        CraftingRecipe wallRecipe = new(
            Id: 100,
            Ingredients: [new RecipeIngredient(1, 10), new RecipeIngredient(2, 3)],
            Output: new ItemSlot(20, 2));

        RecipeMatcher.CanCraft(bag, wallRecipe).Should().BeTrue();
        RecipeMatcher.TryCraft(bag, wallRecipe, maxOutputStackSize: 20).Should().BeTrue();

        bag.CountItem(1).Should().Be(0);
        bag.CountItem(2).Should().Be(2);
        bag.CountItem(20).Should().Be(2);
    }

    [Fact]
    public void EntityManager_WithLootDropComponent_StoresUnmanagedComponent()
    {
        EntityManager entities = new(16);
        Entity e = entities.CreateEntity();
        entities.SetComponent(e, new LootDropComponent(new ItemSlot(1, 5), OwnerEntityId: e.Id, DespawnAtTick: 600));

        entities.GetComponent<LootDropComponent>(e).Item.Quantity.Should().Be(5);
    }

    [Fact]
    public void LootExpirationSystem_DespawnsExpiredDropsAndRetainsPermanentDrops()
    {
        WorldSimulation world = new(tickRate: 20);
        world.RegisterSystem(new LootExpirationSystem());

        Entity expiring = world.SpawnEntity(Vector2.Zero, radius: 1f);
        world.Entities.SetComponent(expiring, new LootDropComponent(new ItemSlot(1, 3), DespawnAtTick: 2));

        Entity permanent = world.SpawnEntity(new Vector2(10f, 0f), radius: 1f);
        world.Entities.SetComponent(permanent, new LootDropComponent(new ItemSlot(2, 1), DespawnAtTick: 0));

        _ = world.Update(0.05); // Tick 1
        world.Entities.IsAlive(expiring).Should().BeTrue();

        _ = world.Update(0.05); // Tick 2 (>= 2)
        world.Entities.IsAlive(expiring).Should().BeFalse();
        world.Entities.IsAlive(permanent).Should().BeTrue();
    }
}
