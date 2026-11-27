// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Progression;
using Carbon.Combat.Attacks;
using Carbon.Combat.Components;
using Carbon.Combat.Statuses;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Creatures;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Progression;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-021: server-side allocation, paid reset, budget refunds and monster hit resolution.</summary>
public sealed class CharacterProgressionServiceTests
{
    private static readonly ProgressionConfig s_config = ProgressionConfig.LoadShippedDefaults();
    private static readonly CreatureContent s_creatures = CreatureContent.LoadDefault();
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();

    [Fact]
    public void TryAllocateAttribute_LevelOneWithoutPoints_IsRejected()
    {
        Fixture f = new(level: 1);

        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Power, 1).Should().Be(OpStatus.Conflict, "level 1 has no attribute points");
        f.Player.Progression.GetTotalSpentAttributePoints().Should().Be(0);
        f.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void TryAllocateAttribute_RequestAboveBudget_SpendsOnlyWhatIsUnspentThenRejects()
    {
        Fixture f = new(level: 5);
        int budget = s_config.GetTotalStatPoints(5);

        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Dexterity, budget + 50).Should().Be(OpStatus.Ok);
        f.Player.Progression.GetAttribute(PrimaryAttributeId.Dexterity).Should().Be((ushort)budget, "a request never spends more than the budget");
        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Vitality, 1).Should().Be(OpStatus.Conflict, "the budget is spent");
        f.Player.Stats.CritChance.Should().BeGreaterThan(0f, "Dexterity raises crit chance");
    }

    [Fact]
    public void TryAllocateAttribute_UnknownAttribute_IsInvalid()
    {
        Fixture f = new(level: 5);
        f.Service.TryAllocateAttribute(f.Player, (PrimaryAttributeId)9, 1).Should().Be(OpStatus.Invalid);
    }

    [Fact]
    public void TryAllocateAttribute_Vitality_RaisesMaxHealthAndHealsTheDifference()
    {
        Fixture f = new(level: 5);
        float before = f.Player.Stats.MaxHealth;
        HealthComponent hpBefore = f.Simulation.Entities.GetHealth(f.Player.Entity);

        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Vitality, 2).Should().Be(OpStatus.Ok);

        float gained = f.Player.Stats.MaxHealth - before;
        gained.Should().BeApproximately(2f * s_config.Attributes.Vitality.MaxHealth, 1e-3f);
        f.Simulation.Entities.GetHealth(f.Player.Entity).Current.Should().BeApproximately(hpBefore.Current + gained, 1e-3f);
        f.Dirty.Should().Contain(f.Player, "allocations persist through the progression blob");
    }

    [Fact]
    public void TryResetAttributes_AtFreeLevel_RefundsEverythingWithoutGold()
    {
        Fixture f = new(level: (ushort)s_config.Respec.FreeThroughLevel);
        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Power, 5).Should().Be(OpStatus.Ok);

        f.Service.TryResetAttributes(f.Player).Should().Be(OpStatus.Ok);

        f.Player.Progression.GetTotalSpentAttributePoints().Should().Be(0);
        s_config.GetUnspentStatPoints(f.Player.Level, f.Player.Progression).Should().Be(s_config.GetTotalStatPoints(f.Player.Level));
        f.WalletSyncs.Should().BeEmpty("a free reset moves no gold");
    }

    [Fact]
    public void TryResetAttributes_PaidLevelWithGold_DebitsPriceAndSyncsWallet()
    {
        Fixture f = new(level: 30, gold: 5_000);
        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Agility, 20).Should().Be(OpStatus.Ok);
        long price = s_config.GetRespecCost(30);
        price.Should().BePositive();

        f.Service.TryResetAttributes(f.Player).Should().Be(OpStatus.Ok);

        f.Player.Wallet.Gold.Should().Be(5_000 - price);
        f.Player.Progression.GetTotalSpentAttributePoints().Should().Be(0);
        f.WalletSyncs.Should().ContainSingle();
    }

    [Fact]
    public void TryResetAttributes_NotEnoughGold_IsRejectedWithoutChange()
    {
        Fixture f = new(level: 30, gold: 10);
        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Agility, 20).Should().Be(OpStatus.Ok);

        f.Service.TryResetAttributes(f.Player).Should().Be(OpStatus.Conflict);

        f.Player.Wallet.Gold.Should().Be(10);
        f.Player.Progression.GetAttribute(PrimaryAttributeId.Agility).Should().Be(20);
    }

    [Fact]
    public void TryResetAttributes_NothingAllocated_IsRejected()
    {
        Fixture f = new(level: 30, gold: 5_000);
        f.Service.TryResetAttributes(f.Player).Should().Be(OpStatus.Conflict);
        f.Player.Wallet.Gold.Should().Be(5_000, "no gold is charged for an empty reset");
    }

    [Fact]
    public void RefundIfOverBudget_AllocationAboveBudget_ClearsIt()
    {
        CharacterProgressionState prog = new();
        prog.SetAttribute(PrimaryAttributeId.Vitality, 500);
        Fixture f = new(level: 2);

        f.Service.RefundIfOverBudget(2, prog).Should().BeTrue();
        prog.GetTotalSpentAttributePoints().Should().Be(0);

        prog.SetAttribute(PrimaryAttributeId.Vitality, 1);
        f.Service.RefundIfOverBudget(2, prog).Should().BeFalse("an allocation within budget is kept");
    }

    [Fact]
    public void ResolveMonsterDamageKind_CasterSpellAndBolt_AreMagicalOthersPhysical()
    {
        Fixture f = new(level: 1);
        ushort shaman = Archetype("orc_shaman");
        ushort slime = Archetype("wild_slime");

        f.Service.ResolveMonsterDamageKind(shaman, AttackKind.Spell).Should().Be(DamageKind.Magical);
        f.Service.ResolveMonsterDamageKind(shaman, AttackKind.Projectile).Should().Be(DamageKind.Magical);
        f.Service.ResolveMonsterDamageKind(shaman, AttackKind.Melee).Should().Be(DamageKind.Physical);
        f.Service.ResolveMonsterDamageKind(Archetype("skeleton_archer"), AttackKind.Projectile).Should().Be(DamageKind.Physical, "arrows are physical");
        f.Service.ResolveMonsterDamageKind(slime, AttackKind.Leap).Should().Be(DamageKind.Physical);
    }

    [Fact]
    public void ResolveIncomingDamage_ManyHits_DodgesAtTheEvasionRateAndMitigatesTheRest()
    {
        Fixture f = new(level: 60);
        f.Service.TryAllocateAttribute(f.Player, PrimaryAttributeId.Agility, 200).Should().Be(OpStatus.Ok);
        DerivedCharacterStats stats = f.Player.Stats;
        float chance = s_config.GetEvasionChance(in stats, 60);
        float expectedHit = s_config.Mitigate(500f, DamageKind.Physical, in stats, 60);
        chance.Should().BeGreaterThan(0.05f);

        int dodges = 0;
        const int Hits = 20_000;
        for (int i = 0; i < Hits; i++)
        {
            float dealt = f.Service.ResolveIncomingDamage(f.Player, 500f, DamageKind.Physical, 60, out bool dodged);
            if (dodged)
            {
                dodges++;
                dealt.Should().Be(0f);
            }
            else
            {
                dealt.Should().Be(expectedHit);
            }
        }

        (dodges / (float)Hits).Should().BeApproximately(chance, 0.015f);
        expectedHit.Should().BeLessThan(500f);
    }

    [Fact]
    public void ResolveIncomingDamage_InvulnerablePlayer_AvoidsTheHit()
    {
        Fixture f = new(level: 10);
        f.Simulation.Entities.SetComponent(f.Player.Entity, new CombatStateComponent(CrowdControlMask.Invulnerable));

        float dealt = f.Service.ResolveIncomingDamage(f.Player, 80f, DamageKind.Physical, 10, out bool avoided);

        avoided.Should().BeTrue("monster hits on players go through CombatDamage.TryResolve too (audit DOM-1)");
        dealt.Should().Be(0f);
    }

    [Fact]
    public void ResolveIncomingDamage_SameNonce_IsDeterministic()
    {
        Fixture a = new(level: 40);
        Fixture b = new(level: 40);
        a.Service.TryAllocateAttribute(a.Player, PrimaryAttributeId.Agility, 100).Should().Be(OpStatus.Ok);
        b.Service.TryAllocateAttribute(b.Player, PrimaryAttributeId.Agility, 100).Should().Be(OpStatus.Ok);

        for (int i = 0; i < 200; i++)
        {
            float x = a.Service.ResolveIncomingDamage(a.Player, 100f, DamageKind.Magical, 40, out bool dodgedA);
            float y = b.Service.ResolveIncomingDamage(b.Player, 100f, DamageKind.Magical, 40, out bool dodgedB);
            dodgedA.Should().Be(dodgedB);
            x.Should().Be(y);
        }
    }

    [Fact]
    public void RecomputeDerivedStats_DeadPlayerLevelsUp_StaysAtZeroHealth()
    {
        // A dead player's arrow still in flight can kill a monster and level them up (ADR-031): that must not revive them.
        Fixture f = new(level: 5);
        f.Player.IsDead = true;
        f.Simulation.Entities.SetHealth(f.Player.Entity, new HealthComponent(0f, f.Player.Stats.MaxHealth));
        (float Health, float Mana, float Stamina) previous = VitalPools.Capture(f.Player);
        f.Player.Level = 6;

        f.Service.RecomputeDerivedStats(f.Player);
        VitalPools.AddGrowth(f.Simulation, f.Player, previous);

        f.Simulation.Entities.GetHealth(f.Player.Entity).Current.Should().Be(0f, "only a respawn request brings a dead player back");
    }

    private static ushort Archetype(string id) =>
        s_creatures.Catalog.GetDefinition(s_creatures.Catalog.GetIndex(id)).ArchetypeId;

    private sealed class Fixture
    {
        public Fixture(ushort level, long gold = 0)
        {
            ConnectedPlayerSession session = new(9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
            CharacterRecord character = new(
                session.CharacterId, session.AccountId, "Tester", level, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            this.Player = new PlayerState(session, this.Simulation.SpawnNetworkEntity(900UL, Vector2.Zero, 7f), character, new InventoryContainer(PlayerInventoryLayout.TotalCapacity))
            {
                Level = level,
                Wallet = new Wallet(900UL),
            };
            this.Player.Wallet.Restore(gold, 0L, 0UL);
            this.Simulation.Entities.SetHealth(this.Player.Entity, new HealthComponent(50f, 100f));
            this.Service = new CharacterProgressionService(
                this.Simulation,
                s_config,
                s_creatures.Catalog,
                () => s_rules,
                () => 7UL,
                this.Dirty.Add,
                this.WalletSyncs.Add,
                TestGameplayEntropy.Create(1UL));
            this.Service.RecomputeDerivedStats(this.Player);
        }

        public WorldSimulation Simulation { get; } = new();

        public PlayerState Player { get; }

        public CharacterProgressionService Service { get; }

        public List<PlayerState> Dirty { get; } = [];

        public List<PlayerState> WalletSyncs { get; } = [];
    }
}
