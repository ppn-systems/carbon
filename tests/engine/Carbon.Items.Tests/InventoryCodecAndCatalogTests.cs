// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;
using Carbon.Items.Progression;
using Carbon.Items.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class InventoryCodecAndCatalogTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly ProgressionConfig s_progression = ProgressionConfig.LoadShippedDefaults();

    [Fact]
    public void InventoryBinaryCodec_ShouldRoundTripStarterLoadoutAndRejectCorruptPayloads()
    {
        InventoryContainer source = new(27);
        StarterGameCatalog.GrantStarterLoadout(source);
        source.SetSlot(12, new ItemSlot(StarterGameCatalog.ManaCrystal, 17));

        byte[] encoded = InventoryBinaryCodec.Serialize(source);
        encoded.Length.Should().BeGreaterThan(5);

        InventoryContainer restored = new(27);
        InventoryBinaryCodec.TryDeserializeInto(encoded, restored).Should().BeTrue();
        restored.OccupiedSlotCount.Should().Be(source.OccupiedSlotCount);
        restored.GetSlot(0).ItemId.Should().Be(StarterGameCatalog.WoodAxe);
        restored.GetSlot(2).ItemId.Should().Be(StarterGameCatalog.IronSword);
        restored.GetSlot(12).ItemId.Should().Be(StarterGameCatalog.ManaCrystal);
        restored.GetSlot(12).Quantity.Should().Be(17);

        InventoryBinaryCodec.TryDeserializeInto([], restored).Should().BeFalse();
        InventoryBinaryCodec.TryDeserializeInto([99, 0, 0, 0, 0], restored).Should().BeFalse();
    }

    [Fact]
    public void StarterGameCatalog_EveryDataDrivenRecipe_CraftsAtomicallyFromItsIngredients()
    {
        ReadOnlySpan<CraftingRecipe> recipes = s_rules.Recipes.All;
        recipes.Length.Should().Be(s_rules.RecipeCatalog.Count, "recipes come from recipes.json");
        recipes.Length.Should().BeGreaterThan(40);

        for (int i = 0; i < recipes.Length; i++)
        {
            CraftingRecipe recipe = recipes[i];
            InventoryContainer bag = new(16);
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                bag.TryAdd(new ItemSlot(ingredient.ItemId, ingredient.Quantity), s_rules.GetMaxStackSize(ingredient.ItemId)).Should().Be(0);
            }

            RecipeMatcher.TryCraft(bag, recipe, s_rules.ItemCatalog, CraftContext.Unrestricted).IsOk.Should().BeTrue($"recipe '{recipe.Code}' should craft from exactly its ingredients");
            for (int o = 0; o < recipe.OutputCount; o++)
            {
                ItemSlot output = recipe.GetOutput(o);
                bag.CountItem(output.ItemId).Should().BeGreaterThanOrEqualTo(output.Quantity, $"'{recipe.Code}' output {o}");
            }

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                bool alsoOutput = false;
                for (int o = 0; o < recipe.OutputCount; o++)
                {
                    alsoOutput |= recipe.GetOutput(o).ItemId == ingredient.ItemId;
                }

                if (!alsoOutput)
                {
                    bag.CountItem(ingredient.ItemId).Should().Be(0, $"'{recipe.Code}' consumes its ingredients");
                }
            }
        }
    }

    [Fact]
    public void StarterGameCatalog_ItemArchetypes_ComeFromItemsJsonStats()
    {
        s_rules.Items.Count.Should().Be(s_rules.ItemCatalog.Count, "every items.json entry has an archetype");
        ItemArchetype axe = s_rules.Items.GetRequired(StarterGameCatalog.WoodAxe);
        axe.EquipSlot.Should().Be(EquipmentSlotKind.Tool);
        axe.HarvestTier.Should().Be(1);
        axe.Capabilities.Should().Be(ToolCapabilities.ChopFlora);
        axe.FloraHarvestDamage.Should().Be(55f);
        s_rules.Items.GetRequired(StarterGameCatalog.HuntingBow).ProjectileSpeed.Should().Be(520f);
        s_rules.Items.GetRequired(StarterGameCatalog.WoodWallKit).PlaceableStructureType.Should().Be(1);
        s_rules.Items.TryGet(s_rules.ItemCatalog.ResolveId("ember_blade"), out ItemArchetype? blade).Should().BeTrue("items beyond the starter set are simulated too");
        blade!.Capabilities.Should().HaveFlag(ToolCapabilities.MeleeWeapon);
    }

    [Fact]
    public void PlayerInventoryLayout_ItemEnhancement_And_CharacterProgression_ShouldEnforceRpgRules()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.IronHelmet, 1, 0UL, 220f));
        inv.SetSlot(10, new ItemSlot(StarterGameCatalog.WoodLog, 5));

        ItemSlot logSlot = inv.GetSlot(10);
        PlayerInventoryLayout.CanAcceptSlotItem(s_rules, PlayerInventoryLayout.HeadSlot, in logSlot).Should().BeFalse();

        PlayerInventoryLayout.TryQuickMove(s_rules, inv, 9, out int equippedSlot).Should().BeTrue();
        equippedSlot.Should().Be(PlayerInventoryLayout.HeadSlot);
        inv.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId.Should().Be(StarterGameCatalog.IronHelmet);

        ItemSlot helmet = inv.GetSlot(PlayerInventoryLayout.HeadSlot);
        ItemSlot upgraded = ItemEnhancementPolicy.WithUpgradeLevel(in helmet, 5);
        ItemEnhancementPolicy.GetUpgradeLevel(in upgraded).Should().Be(5);
        inv.SetSlot(PlayerInventoryLayout.HeadSlot, in upgraded);

        DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(s_rules, s_progression, 3, inv, byte.MaxValue, null);
        stats.Defense.Should().BeGreaterThan(10f);
        stats.MaxHealth.Should().BeGreaterThan(140f);
        stats.MaxMana.Should().Be(s_progression.BaseStats.MaxMana + (2f * s_progression.PerLevel.MaxMana), "level 3 with no mana gear");
        s_progression.GetXpToNextLevel(1).Should().BeGreaterThan(0U);
        s_progression.Mitigate(100f, DamageKind.Physical, in stats, attackerLevel: 3).Should().BeInRange(25f, 99f, "armor mitigates part of a hit, never all of it");

        ItemEnhancementPolicy.TryGetStep(s_rules, in upgraded, out EnhancementStep? step).Should().BeTrue();
        step!.FromLevel.Should().Be(5);
        step.MaterialSpan.ToArray().Should().Contain(m => m.ItemId == StarterGameCatalog.IronOre && m.Quantity > 0);
        step.MaterialSpan.ToArray().Should().Contain(m => m.ItemId == StarterGameCatalog.ManaCrystal && m.Quantity > 0);
        step.SuccessPermille.Should().BeInRange(1, EnhancementTable.WeightTotal);
    }

    [Fact]
    public void ToolUsagePolicy_ShouldPenalizeMisuseDeflectRockAndDegradeWhenWornOut()
    {
        ItemSlot freshSword = new(StarterGameCatalog.IronSword, 1, 0, 200f);
        ItemSlot freshAxe = new(StarterGameCatalog.WoodAxe, 1, 0, 120f);
        ItemSlot freshPick = new(StarterGameCatalog.StonePickaxe, 1, 0, 160f);
        ItemSlot wornSword = new(StarterGameCatalog.IronSword, 1, 0, 0f);

        ToolUsagePolicy.IsWornOut(s_rules, in freshSword).Should().BeFalse();
        ToolUsagePolicy.IsWornOut(s_rules, in wornSword).Should().BeTrue();

        PropStrikeEvaluation swordOnTree = ToolUsagePolicy.EvaluatePropStrike(s_rules, in freshSword, ToolTargetKind.Flora, 1);
        swordOnTree.Outcome.Should().Be(ToolStrikeOutcome.SwordMisuseOnWood);
        swordOnTree.DurabilityLoss.Should().BeGreaterThan(3f);
        swordOnTree.Damage.Should().BeLessThan(s_rules.GetFloraHarvestDamage(StarterGameCatalog.WoodAxe));

        PropStrikeEvaluation swordOnRock = ToolUsagePolicy.EvaluatePropStrike(s_rules, in freshSword, ToolTargetKind.Mineral, 1);
        swordOnRock.Outcome.Should().Be(ToolStrikeOutcome.BladeBluntOnStone);
        swordOnRock.Damage.Should().Be(0f);
        swordOnRock.DurabilityLoss.Should().BeGreaterThan(4f);

        PropStrikeEvaluation axeOnTree = ToolUsagePolicy.EvaluatePropStrike(s_rules, in freshAxe, ToolTargetKind.Flora, 1);
        axeOnTree.Outcome.Should().Be(ToolStrikeOutcome.Optimal);

        PropStrikeEvaluation pickOnOre = ToolUsagePolicy.EvaluatePropStrike(s_rules, in freshPick, ToolTargetKind.Mineral, 2);
        pickOnOre.Outcome.Should().Be(ToolStrikeOutcome.Optimal);

        CombatStrikeEvaluation wornHit = ToolUsagePolicy.EvaluateCombatStrike(s_rules, in wornSword);
        wornHit.Outcome.Should().Be(ToolStrikeOutcome.WornOutTool);
        wornHit.Damage.Should().BeLessThan(s_rules.GetWeaponBaseDamage(StarterGameCatalog.IronSword));
    }

    [Fact]
    public void NonStackableGear_CodecV1V2_And_ZoneScaling_ShouldHoldInvariants()
    {
        ItemSlot instancedGear = new(StarterGameCatalog.IronSword, 1, InstanceId: 100UL, Durability: 200f);
        instancedGear.IsStackable.Should().BeFalse();
        ItemSlot forgedGear = new(StarterGameCatalog.IronSword, 1, 0UL, 200f, UpgradeLevel: 1);
        forgedGear.IsStackable.Should().BeFalse();
        ItemSlot mat = new(StarterGameCatalog.WoodLog, 5);
        mat.IsStackable.Should().BeTrue();

        InventoryContainer gearBag = new(PlayerInventoryLayout.TotalCapacity);
        gearBag.TryAdd(in forgedGear, 99).Should().Be(0);
        gearBag.TryAdd(in forgedGear, 99).Should().Be(0);
        gearBag.GetSlot(0).Quantity.Should().Be(1);
        gearBag.GetSlot(1).Quantity.Should().Be(1);

        int swordMaxStack = s_rules.GetMaxStackSize(StarterGameCatalog.IronSword);
        swordMaxStack.Should().Be(1);

        // Full bag (0..35) with armor slots (36..39) empty must refuse craft rather than spilling into armor slots.
        InventoryContainer fullBag = new(PlayerInventoryLayout.TotalCapacity);
        for (int i = 0; i < PlayerInventoryLayout.BagLimit - 1; i++)
        {
            fullBag.SetSlot(i, new ItemSlot(StarterGameCatalog.WoodAxe, 1, 0UL, 120f));
        }

        fullBag.SetSlot(PlayerInventoryLayout.BagLimit - 1, new ItemSlot(StarterGameCatalog.WoodLog, 10));
        s_rules.Recipes.TryGet(1, out CraftingRecipe? wallRecipe).Should().BeTrue();
        PlayerInventoryLayout.TryCraftInBag(fullBag, wallRecipe!, s_rules.GetMaxStackSize(StarterGameCatalog.WoodWallKit)).Should().BeFalse();
        fullBag.GetSlot(PlayerInventoryLayout.HeadSlot).IsEmpty.Should().BeTrue();

        // Codec v2 preserves UpgradeLevel & explicit InstanceId cleanly.
        InventoryContainer v2Source = new(PlayerInventoryLayout.TotalCapacity);
        v2Source.SetSlot(2, new ItemSlot(StarterGameCatalog.IronSword, 1, 42UL, 250f, UpgradeLevel: 7));
        byte[] v2Bytes = InventoryBinaryCodec.Serialize(v2Source);
        InventoryContainer v2Restored = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(v2Bytes, v2Restored).Should().BeTrue();
        v2Restored.GetSlot(2).UpgradeLevel.Should().Be(7);
        v2Restored.GetSlot(2).InstanceId.Should().Be(42UL);

        // Synthetic Format v1 payload migrates legacy InstanceId (<= 10) into UpgradeLevel.
        byte[] v1Payload = new byte[5 + 22];
        v1Payload[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(v1Payload.AsSpan(1, 2), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(v1Payload.AsSpan(3, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(v1Payload.AsSpan(5, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(v1Payload.AsSpan(7, 4), StarterGameCatalog.IronSword);
        BinaryPrimitives.WriteInt32LittleEndian(v1Payload.AsSpan(11, 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(v1Payload.AsSpan(15, 8), 4UL);
        BinaryPrimitives.WriteInt32LittleEndian(v1Payload.AsSpan(23, 4), BitConverter.SingleToInt32Bits(200f));

        InventoryContainer v1Restored = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(v1Payload, v1Restored).Should().BeTrue();
        v1Restored.GetSlot(3).UpgradeLevel.Should().Be(4);
        v1Restored.GetSlot(3).InstanceId.Should().Be(0UL);

        // Zone scaling produces monotonically stronger monster stats for deeper tiers.
        ScaledMonsterStats scaled = WorldActorCatalog.ScaleMonsterForZone(WorldActorCatalog.RockGolem, 3);
        scaled.Level.Should().BeGreaterThan(3);
        scaled.MaxHealth.Should().BeGreaterThan(150f);
        scaled.AttackDamage.Should().BeGreaterThan(16f);
        scaled.ExperienceReward.Should().BeGreaterThan(25U);

        // Class starter loadouts grant Vanguard sword, Ranger HuntingBow, Arcanist ArcaneStaff (+MaxMP), and armor slots across all 3 classes.
        InventoryContainer vanguardBag = new(PlayerInventoryLayout.TotalCapacity);
        StarterGameCatalog.GrantStarterLoadout(vanguardBag, startingClass: 0);
        vanguardBag.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId.Should().Be(StarterGameCatalog.LeatherCap);
        vanguardBag.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId.Should().Be(StarterGameCatalog.LeatherTunic);
        vanguardBag.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId.Should().Be(StarterGameCatalog.LeatherBoots);

        InventoryContainer rangerBag = new(PlayerInventoryLayout.TotalCapacity);
        StarterGameCatalog.GrantStarterLoadout(rangerBag, startingClass: 1);
        rangerBag.GetSlot(2).ItemId.Should().Be(StarterGameCatalog.HuntingBow);
        rangerBag.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId.Should().Be(StarterGameCatalog.LeatherTunic);

        InventoryContainer mageBag = new(PlayerInventoryLayout.TotalCapacity);
        StarterGameCatalog.GrantStarterLoadout(mageBag, startingClass: 2);
        mageBag.GetSlot(2).ItemId.Should().Be(StarterGameCatalog.ArcaneStaff);
        mageBag.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId.Should().Be(StarterGameCatalog.LeatherCap);
        mageBag.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId.Should().Be(StarterGameCatalog.LeatherTunic);
        mageBag.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId.Should().Be(StarterGameCatalog.LeatherBoots);
        DerivedCharacterStats baseStats = CharacterProgressionPolicy.ComputeStats(s_rules, s_progression, 1, mageBag, byte.MaxValue, null);
        DerivedCharacterStats staffStats = CharacterProgressionPolicy.ComputeStats(s_rules, s_progression, 1, mageBag, 2, null);
        staffStats.MaxMana.Should().BeGreaterThan(baseStats.MaxMana);

        ItemSlot bowSlot = rangerBag.GetSlot(2);
        ToolUsagePolicy.EvaluatePropStrike(s_rules, in bowSlot, ToolTargetKind.Mineral, 1).Outcome.Should().Be(ToolStrikeOutcome.BladeBluntOnStone);

        ScaledMonsterStats archer = WorldActorCatalog.ScaleMonsterForZone(WorldActorCatalog.SkeletonArcher, 1);
        archer.AttackRange.Should().BeGreaterThan(200f);
        archer.ProjectileSpeed.Should().BeGreaterThan(0f);

        // Progression codec, SP cap, SkillCatalog, Capabilities flags, and TryCraftInBag safety guards
        CharacterProgressionState zeroPts = new();
        s_progression.GetUnspentStatPoints(1, zeroPts).Should().Be(0);
        CharacterProgressionPolicy.GetUnspentSkillPoints(1, zeroPts).Should().Be(1);
        s_progression.GetUnspentStatPoints(5, zeroPts).Should().Be(s_progression.GetTotalStatPoints(5));
        CharacterProgressionPolicy.GetUnspentSkillPoints(5, zeroPts).Should().Be(5);
        CharacterProgressionPolicy.GetTotalSkillPointsForLevel(20).Should().Be((ushort)SkillCatalog.TotalLearnableRanks);
        CharacterProgressionPolicy.TryGetSkillSpec(99, 1, out _).Should().BeFalse();
        CharacterProgressionPolicy.TryGetSkillSpec(SkillCatalog.WhirlwindId, 1, out SkillSpec r1).Should().BeTrue();
        CharacterProgressionPolicy.TryGetSkillSpec(SkillCatalog.WhirlwindId, 3, out SkillSpec r3).Should().BeTrue();
        r3.DamageMultiplier.Should().BeGreaterThan(r1.DamageMultiplier);

        CharacterProgressionState allocated = new();
        allocated.SetAttribute(PrimaryAttributeId.Vitality, 3);
        allocated.SetAttribute(PrimaryAttributeId.Wisdom, 2);
        allocated.SetAttribute(PrimaryAttributeId.Power, 4);
        allocated.SetAttribute(PrimaryAttributeId.Agility, 2);
        allocated.SetSkillRank(SkillCatalog.WhirlwindId, 2);
        allocated.SetSkillRank(SkillCatalog.ArcaneNovaId, 1);

        byte[] encodedProg = CharacterProgressionBinaryCodec.Serialize(allocated);
        CharacterProgressionState decodedProg = new();
        CharacterProgressionBinaryCodec.TryDeserializeInto(encodedProg, decodedProg).Should().BeTrue();
        decodedProg.GetAttribute(PrimaryAttributeId.Vitality).Should().Be(3);
        decodedProg.GetAttribute(PrimaryAttributeId.Power).Should().Be(4);
        decodedProg.GetSkillRank(SkillCatalog.WhirlwindId).Should().Be(2);
        decodedProg.GetSkillRank(SkillCatalog.ArcaneNovaId).Should().Be(1);
        CharacterSkillMaskHelper.HasSkill(decodedProg.UnlockedSkillMask, SkillCatalog.WhirlwindId).Should().BeTrue();
        CharacterSkillMaskHelper.HasSkill(decodedProg.UnlockedSkillMask, SkillCatalog.ArcaneNovaId).Should().BeTrue();
        CharacterSkillMaskHelper.HasSkill(decodedProg.UnlockedSkillMask, SkillCatalog.VitalSurgeId).Should().BeFalse();

        DerivedCharacterStats boosted = CharacterProgressionPolicy.ComputeStats(s_rules, s_progression, 5, mageBag, 2, allocated);
        DerivedCharacterStats unboosted = CharacterProgressionPolicy.ComputeStats(s_rules, s_progression, 5, mageBag, 2, null);
        boosted.MaxHealth.Should().BeGreaterThan(unboosted.MaxHealth);
        boosted.MaxMana.Should().BeGreaterThan(unboosted.MaxMana);
        boosted.AttackMultiplier.Should().BeGreaterThan(unboosted.AttackMultiplier);
        boosted.MoveSpeedMultiplier.Should().BeGreaterThan(unboosted.MoveSpeedMultiplier);

        s_rules.Items.TryGet(StarterGameCatalog.RoastMeat, out _).Should().BeTrue();
        s_rules.Items.TryGet(StarterGameCatalog.ArcanePie, out _).Should().BeTrue();
        s_rules.Items.TryGet(StarterGameCatalog.DemonCleaver, out _).Should().BeTrue();
        s_rules.Items.TryGet(StarterGameCatalog.ShamanStaff, out ItemArchetype? shamanStaff).Should().BeTrue();
        shamanStaff!.Capabilities.HasFlag(ToolCapabilities.MagicFocus).Should().BeTrue();
        WorldActorCatalog.GetNpcSpec(WorldActorCatalog.MasterBlacksmith).Capabilities.HasFlag(NpcCapabilities.ForgeArtisan).Should().BeTrue();
        WorldActorCatalog.GetNpcSpec(WorldActorCatalog.HarbourMaster).Capabilities.HasFlag(NpcCapabilities.ForgeArtisan).Should().BeFalse();
        WorldActorCatalog.ScaleMonsterForZone(WorldActorCatalog.OrcShaman, 1).ProjectileSpeed.Should().BeGreaterThan(0f);
        WorldActorCatalog.ScaleMonsterForZone(WorldActorCatalog.CrimsonDemon, 1).AttackDamage.Should().BeGreaterThan(25f);

        // TryCraftInBag must NOT consume equipped armor (36..39) or forge-enhanced (+1+) ingredients
        s_rules.Recipes.TryGet(15, out CraftingRecipe? ironHelmRecipe).Should().BeTrue();
        InventoryContainer craftGuardBag = new(PlayerInventoryLayout.TotalCapacity);
        craftGuardBag.SetSlot(0, new ItemSlot(StarterGameCatalog.IronOre, 5, 0UL, 0f, UpgradeLevel: 1));
        PlayerInventoryLayout.TryCraftInBag(craftGuardBag, ironHelmRecipe!, 1).Should().BeFalse();
    }

    [Fact]
    public void All_StructureTypes_Are_Craftable_And_Monster_Scaling_Grows_With_Distance_And_Night()
    {
        for (byte structureType = 1; structureType <= 8; structureType++)
        {
            uint kitId = s_rules.ResolveStructureKitItemId(structureType);
            kitId.Should().NotBe(0U, $"StructureType {structureType} must have a placeable kit item");
            s_rules.Items.TryGet(kitId, out ItemArchetype? kitDef).Should().BeTrue();
            kitDef!.Category.Should().Be(ItemCategory.Placeable);

            bool hasRecipe = false;
            foreach (CraftingRecipe rec in s_rules.Recipes.All)
            {
                if (rec.Output.ItemId == kitId)
                {
                    hasRecipe = true;
                    break;
                }
            }

            hasRecipe.Should().BeTrue($"StructureType {structureType} kit {kitId} must have a crafting recipe");
        }

        // Distance-from-spawn scaling & night empowerment
        ScaledMonsterStats nearDay = WorldActorCatalog.ScaleMonster(WorldActorCatalog.FrostWolf, 0, distanceTilesFromSpawn: 15f, isNight: false);
        ScaledMonsterStats farDay = WorldActorCatalog.ScaleMonster(WorldActorCatalog.FrostWolf, 0, distanceTilesFromSpawn: 250f, isNight: false);
        ScaledMonsterStats farNight = WorldActorCatalog.ScaleMonster(WorldActorCatalog.FrostWolf, 0, distanceTilesFromSpawn: 250f, isNight: true);

        farDay.Level.Should().BeGreaterThan(nearDay.Level);
        farDay.MaxHealth.Should().BeGreaterThan(nearDay.MaxHealth);
        farDay.AttackDamage.Should().BeGreaterThan(nearDay.AttackDamage);
        farDay.ExperienceReward.Should().BeGreaterThan(nearDay.ExperienceReward);

        farNight.MaxHealth.Should().BeGreaterThan(farDay.MaxHealth);
        farNight.AttackDamage.Should().BeGreaterThan(farDay.AttackDamage);
        farNight.MoveSpeed.Should().BeGreaterThan(farDay.MoveSpeed);
        farNight.ExperienceReward.Should().BeGreaterThan(farDay.ExperienceReward);

        // Monster economy drops must all be valid catalog items
        ushort[] monsters =
        [
            WorldActorCatalog.WildSlime,
            WorldActorCatalog.RockGolem,
            WorldActorCatalog.FrostWolf,
            WorldActorCatalog.AshWraith,
            WorldActorCatalog.SkeletonArcher,
            WorldActorCatalog.OrcShaman,
            WorldActorCatalog.CrimsonDemon,
        ];
        foreach (ushort m in monsters)
        {
            MonsterArchetypeSpec spec = WorldActorCatalog.GetMonsterSpec(m);
            s_rules.Items.TryGet(spec.DropItemId, out _).Should().BeTrue();
            if (spec.SecondaryDropItemId != 0U)
            {
                s_rules.Items.TryGet(spec.SecondaryDropItemId, out _).Should().BeTrue();
            }

            if (spec.RareDropItemId != 0U)
            {
                s_rules.Items.TryGet(spec.RareDropItemId, out _).Should().BeTrue();
            }
        }
    }
}
