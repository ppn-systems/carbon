// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Actions;
using Carbon.Application.World.Combat;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Needs;
using Carbon.Application.World.Players;
using Carbon.Application.World.Projectiles;
using Carbon.Application.World.Props;
using Carbon.Application.World.Replication;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Survival.Config;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class PlayerActionServiceTests
{
    private const uint MonsterLayer = 2U;

    [Fact]
    public void Attack_HealingConsumableWhileHurt_HealsUsesOneAndStartsTheConsumableCooldown()
    {
        Harness h = new();
        ItemArchetype potion = h.Find(a => a.Category == ItemCategory.Consumable && a.HealHealth > 0f);
        h.Hold(new ItemSlot(potion.Id, 2));
        h.Simulation.Entities.SetHealth(h.Player.Entity, new HealthComponent(10f, 100f));

        h.Actions.Attack(h.Player);

        h.Simulation.Entities.GetHealth(h.Player.Entity).Current.Should().BeGreaterThan(10f);
        h.Player.Inventory.GetSlot(0).Quantity.Should().Be(1);
        h.Player.Cooldowns.IsReady(PlayerActionService.ConsumableAbilityId, h.Tick).Should().BeFalse();
        h.Outbound.Pending.SlotChanges.Should().ContainSingle();
        h.Host.Wakes.Should().Be(0, "using a consumable does not stand a resting player up");
    }

    [Fact]
    public void Attack_FullHealthAndResourcesWithAConsumable_DoesNotUseIt()
    {
        Harness h = new();
        ItemArchetype potion = h.Find(a => a.Category == ItemCategory.Consumable && a.HealHealth > 0f && a.RestoreMana <= 0f && a.RestoreStamina <= 0f);
        h.Hold(new ItemSlot(potion.Id, 2));

        h.Actions.Attack(h.Player);

        h.Player.Inventory.GetSlot(0).Quantity.Should().Be(2);
        h.Player.Cooldowns.IsReady(PlayerActionService.ConsumableAbilityId, h.Tick).Should().BeTrue();
    }

    [Fact]
    public void Attack_RangedWeapon_LaunchesAProjectileAndStartsTheAttackCooldown()
    {
        Harness h = new();
        ItemArchetype bow = h.Find(a => a.ProjectileSpeed > 0f && a.AttackManaCost <= 0f);
        h.Hold(new ItemSlot(bow.Id, 1, Durability: bow.MaxDurability));
        h.Player.PendingAttackAim = new Vector2(100f, 0f);

        h.Actions.Attack(h.Player);

        h.Projectiles.Count.Should().Be(1);
        h.Player.Cooldowns.IsReady(PlayerActionService.AttackAbilityId, h.Tick).Should().BeFalse();
        h.Host.Wakes.Should().Be(1);
    }

    [Fact]
    public void Attack_ManaWeaponWithoutEnoughMana_DoesNotFire()
    {
        Harness h = new();
        ItemArchetype staff = h.Find(a => a.ProjectileSpeed > 0f && a.AttackManaCost > 0f);
        h.Hold(new ItemSlot(staff.Id, 1, Durability: staff.MaxDurability));
        h.Player.CurrentMana = 0f;

        h.Actions.Attack(h.Player);

        h.Projectiles.Count.Should().Be(0);
    }

    [Fact]
    public void Attack_MeleeSwingAtAMonsterInFront_DamagesIt()
    {
        Harness h = new();
        Entity mob = h.Simulation.SpawnNetworkEntity(500UL, new Vector2(5f, 0f), 8f);
        h.Simulation.Entities.SetCollider(mob, new Collider2D(ColliderKind.Circle, 8f, default, false, MonsterLayer, 0U));
        h.Simulation.Entities.SetHealth(mob, new HealthComponent(1000f, 1000f));
        h.Player.PendingAttackAim = new Vector2(50f, 0f);

        h.Actions.Attack(h.Player);

        h.Simulation.Entities.GetHealth(mob).Current.Should().BeLessThan(1000f);
    }

    [Fact]
    public void Attack_WhileTheAttackCooldownRuns_WakesButDoesNotStrike()
    {
        Harness h = new();
        h.Player.Cooldowns.StartCooldown(PlayerActionService.AttackAbilityId, h.Tick, 100U);
        ItemArchetype bow = h.Find(a => a.ProjectileSpeed > 0f && a.AttackManaCost <= 0f);
        h.Hold(new ItemSlot(bow.Id, 1, Durability: bow.MaxDurability));

        h.Actions.Attack(h.Player);

        h.Host.Wakes.Should().Be(1);
        h.Projectiles.Count.Should().Be(0);
    }

    [Fact]
    public void WearActiveTool_DurableItem_LowersDurabilityAndMarksTheInventoryDirty()
    {
        Harness h = new();
        ItemArchetype tool = h.Find(a => a.MaxDurability > 10f);
        ItemSlot held = new(tool.Id, 1, Durability: tool.MaxDurability);
        h.Hold(held);

        h.Actions.WearActiveTool(h.Player, in held, 2f, out ItemSlot worn).Should().BeTrue();

        worn.Durability.Should().Be(tool.MaxDurability - 2f);
        h.Player.Inventory.GetSlot(0).Should().Be(worn);
        h.Host.InventoryDirtyMarks.Should().Be(1);
        h.Actions.WearActiveTool(h.Player, ItemSlot.Empty, 2f, out _).Should().BeFalse();
    }

    [Fact]
    public void WearArmor_PieceBreaks_RecomputesStats()
    {
        Harness h = new();
        h.Player.Inventory.SetSlot(PlayerInventoryLayout.HeadSlot, new ItemSlot(h.Find(a => a.MaxDurability > 0f).Id, 1, Durability: 0.4f));

        h.Actions.WearArmor(h.Player, 0.5f);

        h.Player.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).Durability.Should().Be(0f);
        h.Host.StatRecomputes.Should().Be(1);
        h.Outbound.Pending.SlotChanges.Should().ContainSingle();
    }

    private sealed class Harness
    {
        private ulong _nextNetworkId = 9000UL;

        public Harness()
        {
            ChunkWorld2D chunks = new(new ProceduralWorldGenerator(1337UL), 32f, 0.0008f);
            GroundLootService loot = new(this.Simulation, () => this.Rules, () => ++_nextNetworkId, () => this.Tick, (_, _) => { });
            HashSet<long> dirty = [];
            ConstructionService construction = new(chunks, StructureCatalog.LoadEmbedded(), (_, _) => false, this.Simulation, 32f, () => this.Rules, this.Outbound, dirty, (_, _) => (0, 0), (_, _) => { });
            CombatService combat = new(this.Simulation, chunks, MonsterLayer, this.Outbound, (_, output) => output.Clear(), (_, _, _) => { });
            this.Projectiles = new ProjectileService(this.Simulation, chunks, MonsterLayer, () => ++_nextNetworkId, new NoProjectileHits());
            NeedsService needs = new(this.Simulation, new BuildingGrid(), 32f, () => this.Rules, () => null, new NeedsConfig(), null, 1UL);
            InventoryService inventory = new(this.Simulation, new BuildingGrid(), 32f, 1UL, () => this.Rules, () => null, () => this.Tick, (_, _, _) => false, loot, this.Outbound, _ => { });
            PropHarvestService props = new(
                this.Simulation, chunks, 32f, 30, () => this.Rules, () => this.Tick, loot, construction, this.Outbound, dirty,
                (_, _) => (0, 0), (_, _, _) => false, (_, in slot, _, out updated) =>
                {
                    updated = slot;
                    return false;
                },
                (_, _) => { });
            this.Actions = new PlayerActionService(this.Simulation, 30, MonsterLayer, () => this.Rules, () => this.Tick, this.Outbound, combat, this.Projectiles, props, inventory, needs, this.Host);

            ConnectedPlayerSession session = new(1UL, 9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
            CharacterRecord character = new(
                session.CharacterId, session.AccountId, "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            Entity entity = this.Simulation.SpawnNetworkEntity(900UL, Vector2.Zero, 7f);
            this.Simulation.Entities.SetHealth(entity, new HealthComponent(100f, 100f));
            this.Player = new PlayerState(session, entity, character, new InventoryContainer(PlayerInventoryLayout.TotalCapacity))
            {
                PendingAttackAbility = 0,
            };
        }

        public ItemRules Rules { get; } = ItemRules.LoadShippedDefaults();

        public ulong Tick { get; } = 1000UL;

        public WorldSimulation Simulation { get; } = new();

        public OutboundTickBuffer Outbound { get; } = new();

        public ProjectileService Projectiles { get; }

        public RecordingHost Host { get; } = new();

        public PlayerActionService Actions { get; }

        public PlayerState Player { get; }

        public ItemArchetype Find(Func<ItemArchetype, bool> predicate)
        {
            foreach (ItemArchetype item in this.Rules.Items.All)
            {
                if (predicate(item))
                {
                    return item;
                }
            }

            throw new InvalidOperationException("No shipped item matches.");
        }

        public void Hold(ItemSlot slot) => this.Player.Inventory.SetSlot(0, in slot);
    }

    private sealed class RecordingHost : IPlayerActionHost
    {
        public int Wakes { get; private set; }

        public int InventoryDirtyMarks { get; private set; }

        public int StatRecomputes { get; private set; }

        public void MarkCharacterDirty(PlayerState state, bool inventoryChanged)
        {
            if (inventoryChanged)
            {
                this.InventoryDirtyMarks++;
            }
        }

        public void Wake(PlayerState state) => this.Wakes++;

        public bool TryBeginEating(PlayerState state, in ItemSlot activeSlot) => false;

        public void UsePlaceable(PlayerState state, Vector2 attackerPos, ItemSlot activeSlot, ItemArchetype archetype)
        {
        }

        public void RecomputeDerivedStats(PlayerState state) => this.StatRecomputes++;
    }

    private sealed class NoProjectileHits : IProjectileHitHandler
    {
        public void OnMonsterHit(in ProjectileState projectile, Entity monster, Vector2 segmentStart, Vector2 shotDirection)
        {
        }

        public bool TryHitPlayer(in ProjectileState projectile, Vector2 segmentStart, Vector2 segmentEnd) => false;
    }
}
