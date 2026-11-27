// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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
using Carbon.Application.World.Stamina;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Progression;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
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
    public void WearArmor_ChestplateAtOneDurabilityTakesAHit_IsDestroyedAndStatsLoseItsDefense()
    {
        Harness h = new();
        uint chest = h.Rules.ItemCatalog.ResolveId("iron_chestplate");
        h.Player.Inventory.SetSlot(PlayerInventoryLayout.ChestSlot, new ItemSlot(chest, 1, InstanceId: 5, Durability: 0.4f));
        h.Host.RecomputeDerivedStats(h.Player);
        float defenseWithArmor = h.Player.Stats.Defense;

        h.Actions.WearArmor(h.Player, 0.5f);

        h.Player.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).IsEmpty.Should().BeTrue("ADR-035: armor worn to 0 is destroyed, not left worn out");
        h.Player.Stats.Defense.Should().BeLessThan(defenseWithArmor, "the broken chestplate no longer adds its defense");
        h.Player.StatsDirty.Should().BeTrue("the owner must receive the recomputed stats");
        h.Host.EquipmentBroadcasts.Should().Be(1, "nearby players stop seeing the chestplate");
        h.Host.InventoryDirtyMarks.Should().Be(1, "the empty slot is saved");
        h.Outbound.Pending.SlotChanges.Should().ContainSingle().Which.Should().Be(
            new OutboundSlotChange(h.Player.Session.ConnectionId, PlayerInventoryLayout.ChestSlot, ItemSlot.Empty));
        h.Outbound.Pending.DurabilityAlerts.Should().ContainSingle().Which.Should().Be(
            new OutboundDurabilityAlert(h.Player.Session.ConnectionId, DurabilityAlertKind.Broke, chest, 0));
    }

    [Fact]
    public void WearArmor_HealthyPiece_OnlyWearsItWithoutAnAlert()
    {
        Harness h = new();
        uint chest = h.Rules.ItemCatalog.ResolveId("iron_chestplate");
        h.Player.Inventory.SetSlot(PlayerInventoryLayout.ChestSlot, new ItemSlot(chest, 1, InstanceId: 5, Durability: 200f));

        h.Actions.WearArmor(h.Player, 0.5f);

        h.Player.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).Durability.Should().Be(199.5f);
        h.Outbound.Pending.DurabilityAlerts.Should().BeEmpty();
        h.Host.EquipmentBroadcasts.Should().Be(0);
    }

    [Fact]
    public void Attack_MeleeWeaponOnItsLastHit_HitsThenIsDestroyedAndTheOwnerGetsTheEmptySlotAndABreak()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(1000f);
        h.Hold(new ItemSlot(StarterGameCatalog.IronSword, 1, InstanceId: 9, Durability: 1f));

        h.Actions.Attack(h.Player);

        h.Simulation.Entities.GetHealth(mob).Current.Should().BeLessThan(1000f, "the last hit still lands");
        h.Player.Inventory.GetSlot(0).IsEmpty.Should().BeTrue("a 1.5 wear step takes the sword from 1 to below 0");
        h.Outbound.Pending.SlotChanges.Should().ContainSingle().Which.Should().Be(new OutboundSlotChange(h.Player.Session.ConnectionId, 0, ItemSlot.Empty));
        h.Outbound.Pending.DurabilityAlerts.Should().ContainSingle().Which.Should().Be(
            new OutboundDurabilityAlert(h.Player.Session.ConnectionId, DurabilityAlertKind.Broke, StarterGameCatalog.IronSword, 0));
        h.Host.StatRecomputes.Should().Be(1, "the held weapon fed the derived stats");
        h.Host.EquipmentBroadcasts.Should().Be(1);
    }

    [Fact]
    public void Attack_BowOnItsLastShot_FiresThenIsDestroyed()
    {
        Harness h = new();
        ItemArchetype bow = h.Find(a => a.ProjectileSpeed > 0f && a.AttackManaCost <= 0f);
        h.Hold(new ItemSlot(bow.Id, 1, InstanceId: 9, Durability: 0.5f));
        h.Player.PendingAttackAim = new Vector2(100f, 0f);

        h.Actions.Attack(h.Player);

        h.Projectiles.Count.Should().Be(1, "the last shot still flies");
        h.Player.Inventory.GetSlot(0).IsEmpty.Should().BeTrue();
        h.Outbound.Pending.DurabilityAlerts.Should().ContainSingle().Which.Kind.Should().Be(DurabilityAlertKind.Broke);
    }

    [Fact]
    public void WearActiveTool_EnhancedSwordAtOne_BreaksLikeAnyOtherAndReportsItsLevel()
    {
        Harness h = new();
        ItemSlot held = new(StarterGameCatalog.IronSword, 1, InstanceId: 9, Durability: 1f, UpgradeLevel: 10);
        h.Hold(held);

        h.Actions.WearActiveTool(h.Player, in held, 1.5f, out ItemSlot after).Should().BeTrue();

        after.Should().Be(ItemSlot.Empty, "ADR-035 D2: enhancement does not protect an item from breaking");
        h.Player.Inventory.GetSlot(0).IsEmpty.Should().BeTrue();
        h.Outbound.Pending.DurabilityAlerts.Should().ContainSingle().Which.Should().Be(
            new OutboundDurabilityAlert(h.Player.Session.ConnectionId, DurabilityAlertKind.Broke, StarterGameCatalog.IronSword, 10));
    }

    [Fact]
    public void WearActiveTool_WornDownStepByStep_WarnsExactlyOnceAtTheCriticalShareThenBreaksOnce()
    {
        // Iron sword: 200 max, so the critical share (10 %) is 20. Start just above it and wear 1.5 per step.
        Harness h = new();
        ItemSlot held = new(StarterGameCatalog.IronSword, 1, InstanceId: 9, Durability: 21f);
        h.Hold(held);

        h.Actions.WearActiveTool(h.Player, in held, 1.5f, out held).Should().BeTrue();
        h.Outbound.Pending.DurabilityAlerts.Should().ContainSingle().Which.Should().Be(
            new OutboundDurabilityAlert(h.Player.Session.ConnectionId, DurabilityAlertKind.AboutToBreak, StarterGameCatalog.IronSword, 0),
            "21 → 19.5 crosses 10 % of 200");

        int steps = 1;
        while (!held.IsEmpty && steps < 100)
        {
            h.Actions.WearActiveTool(h.Player, in held, 1.5f, out held).Should().BeTrue();
            steps++;
        }

        held.IsEmpty.Should().BeTrue();
        h.Outbound.Pending.DurabilityAlerts.Select(a => a.Kind).Should().Equal(
            [DurabilityAlertKind.AboutToBreak, DurabilityAlertKind.Broke],
            "the warning fires once per descent, not on every step below 10 %");
        h.Host.StatRecomputes.Should().Be(1, "only the break changes the stats");
    }

    [Fact]
    public void Attack_BareHandSwing_DebitsExactlyTheBareHandCost()
    {
        Harness h = new();
        h.Player.PendingAttackAim = new Vector2(0f, 40f);

        h.Actions.Attack(h.Player);

        h.Player.CurrentStamina.Should().Be(100f - StaminaTestConfig.BareHandCost, "a punch or bare-hand gather costs stamina, hit or miss");
    }

    [Fact]
    public void Attack_MeleeWeaponAtAMonster_DebitsExactlyTheMeleeCostAndHits()
    {
        Harness h = new();
        ItemArchetype sword = h.Find(a => a.Capabilities.HasFlag(ToolCapabilities.MeleeWeapon) && a.Code != "wood_axe");
        h.Hold(new ItemSlot(sword.Id, 1, Durability: sword.MaxDurability));
        Entity mob = h.SpawnMonster(1000f);

        h.Actions.Attack(h.Player);

        h.Player.CurrentStamina.Should().Be(100f - StaminaTestConfig.MeleeCost);
        h.Simulation.Entities.GetHealth(mob).Current.Should().BeLessThan(1000f);
    }

    [Fact]
    public void Attack_ToolWithAnItemOverride_DebitsTheItemCost()
    {
        Harness h = new();
        ItemArchetype axe = h.Find(a => a.Code == "wood_axe");
        h.Hold(new ItemSlot(axe.Id, 1, Durability: axe.MaxDurability));
        h.Player.PendingAttackAim = new Vector2(0f, 40f);

        h.Actions.Attack(h.Player);

        h.Player.CurrentStamina.Should().Be(100f - StaminaTestConfig.WoodAxeCost, "itemSwingCosts scales the cost by tool");
    }

    [Fact]
    public void Attack_ToolWithoutAnOverride_DebitsTheToolCost()
    {
        Harness h = new();
        ItemArchetype pick = h.Find(a => a.Category == ItemCategory.Tool && a.Code != "wood_axe");
        h.Hold(new ItemSlot(pick.Id, 1, Durability: pick.MaxDurability));
        h.Player.PendingAttackAim = new Vector2(0f, 40f);

        h.Actions.Attack(h.Player);

        h.Player.CurrentStamina.Should().Be(100f - StaminaTestConfig.ToolCost);
    }

    [Fact]
    public void Attack_BowShot_DebitsExactlyTheRangedCost()
    {
        Harness h = new();
        ItemArchetype bow = h.Find(a => a.Capabilities.HasFlag(ToolCapabilities.RangedBow));
        h.Hold(new ItemSlot(bow.Id, 1, Durability: bow.MaxDurability));
        h.Player.PendingAttackAim = new Vector2(100f, 0f);

        h.Actions.Attack(h.Player);

        h.Player.CurrentStamina.Should().Be(100f - StaminaTestConfig.RangedCost);
        h.Projectiles.Count.Should().Be(1);
    }

    [Fact]
    public void Attack_MeleeWithoutEnoughStamina_IsRefusedAndNothingHappens()
    {
        Harness h = new();
        ItemArchetype sword = h.Find(a => a.Capabilities.HasFlag(ToolCapabilities.MeleeWeapon) && a.Code != "wood_axe");
        ItemSlot held = new(sword.Id, 1, Durability: sword.MaxDurability);
        h.Hold(held);
        Entity mob = h.SpawnMonster(1000f);
        h.Player.CurrentStamina = StaminaTestConfig.MeleeCost - 1f;

        h.Actions.Attack(h.Player);

        h.Simulation.Entities.GetHealth(mob).Current.Should().Be(1000f, "a refused swing does not hit");
        h.Player.Inventory.GetSlot(0).Should().Be(held, "a refused swing does not wear the weapon");
        h.Player.CurrentStamina.Should().Be(StaminaTestConfig.MeleeCost - 1f);
        h.Player.Cooldowns.IsReady(PlayerActionService.AttackAbilityId, h.Tick).Should().BeTrue("a refused swing does not start the cooldown");
        h.Player.StaminaRefusals.Should().Be(1U);
        h.Player.Stamina.Exhausted.Should().BeTrue();
    }

    [Fact]
    public void Attack_WhileExhausted_IsRefusedEvenWhenTheSwingIsAffordable()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(1000f);
        h.Player.CurrentStamina = 2f;
        h.Actions.Attack(h.Player); // 2 < 3: refused, exhausted
        h.Player.CurrentStamina = 40f; // below the 50% recover fraction

        h.Actions.Attack(h.Player);

        h.Simulation.Entities.GetHealth(mob).Current.Should().Be(1000f);
        h.Player.CurrentStamina.Should().Be(40f);
        h.Player.StaminaRefusals.Should().Be(2U);
    }

    [Fact]
    public void QueueStatusIfChanged_AfterARefusal_PushesExhaustionAndTheRefusalCountOnce()
    {
        Harness h = new();
        h.Player.CurrentStamina = 1f;
        h.Actions.Attack(h.Player);

        h.Stamina.QueueStatusIfChanged(h.Player);
        h.Stamina.QueueStatusIfChanged(h.Player);

        h.Outbound.Pending.StaminaStatuses.Should().ContainSingle()
            .Which.Should().Be(new OutboundStaminaStatus(h.Player.Session.ConnectionId, Exhausted: true, RefusalCount: 1U));
    }

    [Fact]
    public void Regenerate_BeforeTheDelay_RestoresNothing()
    {
        Harness h = new();
        h.Player.PendingAttackAim = new Vector2(0f, 40f);
        h.Actions.Attack(h.Player); // 97, delay 1.5 s

        h.Stamina.Regenerate(h.Player, 1.4f, isMoving: false);

        h.Player.CurrentStamina.Should().Be(100f - StaminaTestConfig.BareHandCost);
    }

    [Theory]
    [InlineData(false, StaminaTestConfig.RegenStill)]
    [InlineData(true, StaminaTestConfig.RegenMoving)]
    public void Regenerate_AfterTheDelay_IsSlowerWhileMoving(bool isMoving, float expectedPerSecond)
    {
        Harness h = new();
        h.Player.CurrentStamina = 50f;

        h.Stamina.Regenerate(h.Player, 1f, isMoving);

        h.Player.CurrentStamina.Should().BeApproximately(50f + expectedPerSecond, 0.001f);
    }

    [Theory]
    [InlineData(RestState.Tired, HungerState.Fed, StaminaTestConfig.RegenStill * StaminaTestConfig.TiredScale)]
    [InlineData(RestState.Rested, HungerState.Starving, StaminaTestConfig.RegenStill * StaminaTestConfig.StarvingScale)]
    public void Regenerate_TiredOrStarving_SlowsRegen(RestState fatigue, HungerState hunger, float expectedPerSecond)
    {
        Harness h = new();
        h.Simulation.Entities.SetComponent(h.Player.Entity, new NeedsComponent { Fatigue = fatigue, Hunger = hunger });
        h.Player.CurrentStamina = 50f;

        h.Stamina.Regenerate(h.Player, 1f, isMoving: false);

        h.Player.CurrentStamina.Should().BeApproximately(50f + expectedPerSecond, 0.001f);
    }

    [Fact]
    public void Attack_WhileDead_SpendsNothingAndCountsNoRefusal()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(1000f);
        h.Player.IsDead = true;

        h.Actions.Attack(h.Player);

        h.Player.CurrentStamina.Should().Be(100f);
        h.Player.StaminaRefusals.Should().Be(0U, "a dead player's input is not an exhaustion refusal");
        h.Simulation.Entities.GetHealth(mob).Current.Should().Be(1000f);
    }

    [Fact]
    public void Regenerate_WhileDead_RestoresNothingAndBuildWorkIsBlocked()
    {
        Harness h = new();
        h.Player.CurrentStamina = 50f;
        h.Player.IsDead = true;

        h.Stamina.Regenerate(h.Player, 5f, isMoving: false);

        h.Player.CurrentStamina.Should().Be(50f);
        h.Stamina.CanWork(h.Player).Should().BeFalse();
        h.Stamina.TrySprint(h.Player, 1f / 30f).Should().BeFalse();
    }

    [Fact]
    public void Regenerate_StatusEffectScale_SlowsRegen()
    {
        Harness h = new();
        h.Player.CurrentStamina = 50f;

        h.Stamina.Regenerate(h.Player, 1f, isMoving: false, effectScale: 0.5f);

        h.Player.CurrentStamina.Should().BeApproximately(50f + (StaminaTestConfig.RegenStill * 0.5f), 0.001f, "ADR-029 effects scale stamina regen");
    }

    [Fact]
    public void DrainWork_OneSecondOfBuilding_DebitsExactlyTheBuildCost()
    {
        Harness h = new();

        for (int i = 0; i < 30; i++)
        {
            h.Stamina.DrainWork(h.Player, 1f / 30f);
        }

        h.Player.CurrentStamina.Should().BeApproximately(100f - StaminaTestConfig.BuildWorkPerSecond, 0.01f);
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
            CombatService combat = new(this.Simulation, chunks, MonsterLayer, this.Outbound, (_, output) => output.Clear(), (_, _, _) => { }, () => this.Tick);
            this.Projectiles = new ProjectileService(this.Simulation, chunks, MonsterLayer, () => ++_nextNetworkId, new NoProjectileHits());
            NeedsService needs = new(this.Simulation, new BuildingGrid(), 32f, () => this.Rules, () => null, new NeedsConfig(), null, TestGameplayEntropy.Create(1UL));
            InventoryService inventory = new(this.Simulation, new BuildingGrid(), 32f, TestGameplayEntropy.Create(1UL), () => this.Rules, () => null, () => this.Tick, (_, _, _) => false, loot, this.Outbound, _ => { });
            PropHarvestService props = new(
                this.Simulation, chunks, 32f, (_, _, _) => 900UL, () => PropRegrowthRules.LoadEmbedded().Trees, 30, TestGameplayEntropy.Create(1UL), () => this.Rules, () => this.Tick, loot, construction, this.Outbound, dirty,
                (_, _) => (0, 0), (_, _, _) => false, (_, in slot, _, out updated) =>
                {
                    updated = slot;
                    return false;
                },
                (_, _) => { });
            this.Stamina = new StaminaService(this.Simulation, StaminaTestConfig.Create(), this.Outbound);
            this.Actions = new PlayerActionService(this.Simulation, 30, MonsterLayer, () => this.Rules, () => this.Tick, this.Outbound, combat, this.Projectiles, props, inventory, needs, this.Stamina, this.Host, TestGameplayEntropy.Create(1UL));

            ConnectedPlayerSession session = new(9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
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

        public StaminaService Stamina { get; }

        public Entity SpawnMonster(float health)
        {
            Entity mob = this.Simulation.SpawnNetworkEntity(++_nextNetworkId, new Vector2(5f, 0f), 8f);
            this.Simulation.Entities.SetCollider(mob, new Collider2D(ColliderKind.Circle, 8f, default, false, MonsterLayer, 0U));
            this.Simulation.Entities.SetHealth(mob, new HealthComponent(health, health));
            this.Player.PendingAttackAim = new Vector2(50f, 0f);
            return mob;
        }

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
        private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
        private static readonly ProgressionConfig s_progression = ProgressionConfig.LoadShippedDefaults();

        public int Wakes { get; private set; }

        public int InventoryDirtyMarks { get; private set; }

        public int StatRecomputes { get; private set; }

        public int EquipmentBroadcasts { get; private set; }

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

        public void RecomputeDerivedStats(PlayerState state)
        {
            this.StatRecomputes++;
            state.Stats = CharacterStatCalculator.Compute(s_rules, s_progression, state.Level, state.Inventory, state.Session.SelectedHotbarSlot, state.Progression);
        }

        public void BroadcastEquipment(PlayerState state) => this.EquipmentBroadcasts++;
    }

    private sealed class NoProjectileHits : IProjectileHitHandler
    {
        public void OnMonsterHit(in ProjectileState projectile, Entity monster, Vector2 segmentStart, Vector2 shotDirection)
        {
        }

        public bool TryHitPlayer(in ProjectileState projectile, Vector2 segmentStart, Vector2 segmentEnd) => false;
    }
}
