// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The repair window (ADR-028) lists its requirements from the synced <c>repair.json</c>, not from the client build, and
/// its animation only plays the server's answer. The synced document here is the shipped one with the iron family
/// changed to 6 ingots per full repair, so a client reading its own copy would show 3, not 9.
/// </summary>
public sealed class RepairWindowTests
{
    private const int SwordSlot = 0;
    private const int IngotSlot = 9;
    private static readonly ItemRules s_synced = SyncedRules();

    private static uint Id(string code) => s_synced.ItemCatalog.ResolveId(code);

    [Fact]
    public void Build_FullRepairAtTheBlacksmith_ListsSyncedMaterialsFeeAndStationWithMissingOnesUnmet()
    {
        // Nearly broken iron sword (1/200, tier 2 = 150 %; ADR-035: 0 no longer exists): 6 × 1.5 = 9 ingots, fee ceil(110 × 30 % × 199/200) = 33 gold.
        ItemSlot[] slots = Bag(swordDurability: 1f, ingots: 4);
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];

        RepairPlan plan = RepairPlan.Build(s_synced.Repair, slots, SwordSlot, RepairVenue.Blacksmith, stationTier: 3, gold: 20, partial: false, rows);

        plan.HasItem.Should().BeTrue();
        rows[..plan.RequirementCount].ToArray().Should().Equal(
            new RepairRequirement(RepairRequirementKind.Material, Id("iron_ingot"), 9, 4),
            new RepairRequirement(RepairRequirementKind.Gold, 0U, 33, 20),
            new RepairRequirement(RepairRequirementKind.Station, 0U, 2, 3));
        rows[0].IsMet.Should().BeFalse("4 of 9 ingots is drawn in red");
        rows[1].IsMet.Should().BeFalse("20 of 33 gold is drawn in red");
        rows[2].IsMet.Should().BeTrue();
        plan.CanRepair.Should().BeFalse();
        plan.Quote.NewDurability.Should().Be(200f);
    }

    [Fact]
    public void Build_PartialRepair_PlansTheLargestStepCountTheBagAndWalletPay()
    {
        // 4 ingots pay 4 steps (ceil(0.9 × 4) = 4); 20 gold pays 6 (ceil(3.3 × 6) = 20): 4 steps, 4 ingots, 14 gold.
        ItemSlot[] slots = Bag(swordDurability: 1f, ingots: 4);
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];

        RepairPlan plan = RepairPlan.Build(s_synced.Repair, slots, SwordSlot, RepairVenue.Blacksmith, stationTier: 3, gold: 20, partial: true, rows);

        plan.IsPartial.Should().BeTrue();
        plan.Quote.Steps.Should().Be(4);
        plan.Quote.NewDurability.Should().Be(81f);
        rows[0].Should().Be(new RepairRequirement(RepairRequirementKind.Material, Id("iron_ingot"), 4, 4));
        rows[1].Should().Be(new RepairRequirement(RepairRequirementKind.Gold, 0U, 14, 20));
        plan.CanRepair.Should().BeTrue();
    }

    [Fact]
    public void Build_PartialAtTheBlacksmithShortOfGold_StopsAtTheStepsTheWalletPays()
    {
        // 9 ingots pay the full 10 steps, but 20 gold pays only 6 (ceil(3.3 × 6) = 20; 7 steps would cost 24).
        ItemSlot[] slots = Bag(swordDurability: 1f, ingots: 9);
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];

        RepairPlan plan = RepairPlan.Build(s_synced.Repair, slots, SwordSlot, RepairVenue.Blacksmith, stationTier: 3, gold: 20, partial: true, rows);

        plan.Quote.Steps.Should().Be(6);
        rows[1].Should().Be(new RepairRequirement(RepairRequirementKind.Gold, 0U, 20, 20));
        plan.CanRepair.Should().BeTrue();
    }

    [Fact]
    public void Build_AtAWorkbench_ShowsNoFeeAndTheStationTierItemNeeds()
    {
        ItemSlot[] slots = Bag(swordDurability: 1f, ingots: 9);
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];

        RepairPlan plan = RepairPlan.Build(s_synced.Repair, slots, SwordSlot, RepairVenue.Station, stationTier: 1, gold: 0, partial: false, rows);

        rows[..plan.RequirementCount].ToArray().Should().Equal(
            new RepairRequirement(RepairRequirementKind.Material, Id("iron_ingot"), 9, 9),
            new RepairRequirement(RepairRequirementKind.Station, 0U, 2, 1));
        plan.CanRepair.Should().BeFalse("iron needs a forge anvil; a workbench is tier 1");
    }

    [Fact]
    public void CollectDamaged_EquippedFirstThenBag_SkipsWholeAndNonRepairableItems()
    {
        ItemSlot[] slots = Bag(swordDurability: 50f, ingots: 4);
        slots[3] = new ItemSlot(Id("wood_axe"), 1, InstanceId: 3, Durability: 120f);
        slots[PlayerInventoryLayout.ChestSlot] = new ItemSlot(Id("iron_chestplate"), 1, InstanceId: 4, Durability: 10f);
        List<ushort> damaged = [];

        RepairPlan.CollectDamaged(s_synced.Repair, slots, damaged);

        damaged.Should().Equal(PlayerInventoryLayout.ChestSlot, SwordSlot);
    }

    [Fact]
    public void RepairVfx_AnswerArrivesDuringTheStrikes_FillsTheBarOnlyAfterThemAndEndsAtTheNewValue()
    {
        RepairVfx vfx = new();
        vfx.Update(0f, resultSerial: 7, lastSucceeded: false);
        vfx.Begin(fromFraction: 0.25f, toFraction: 1f);

        vfx.Update(0.1f, resultSerial: 8, lastSucceeded: true);
        vfx.IsWorking.Should().BeTrue("the hammer strikes three times before the answer plays");
        vfx.GetBarFraction(0.25f).Should().BeLessThan(0.25f + (0.75f * RepairVfx.WorkFillShare) + 0.001f);

        vfx.Update(RepairVfx.WorkDuration, resultSerial: 8, lastSucceeded: true);
        vfx.IsPlayingSuccess.Should().BeTrue();
        vfx.Update(0.5f, resultSerial: 8, lastSucceeded: true);
        vfx.GetBarFraction(0.25f).Should().Be(1f, "a success fills the bar to the repaired durability");

        vfx.Update(RepairVfx.ResultDuration, resultSerial: 8, lastSucceeded: true);
        vfx.IsActive.Should().BeFalse();
    }

    [Fact]
    public void RepairVfx_Refused_DrainsTheBarBackToTheOldValue()
    {
        RepairVfx vfx = new();
        vfx.Update(0f, resultSerial: 0, lastSucceeded: false);
        vfx.Begin(fromFraction: 0.4f, toFraction: 0.9f);
        vfx.Update(RepairVfx.WorkDuration + 0.1f, resultSerial: 0, lastSucceeded: false);

        vfx.Update(0.5f, resultSerial: 1, lastSucceeded: false);
        vfx.Update(0.5f, resultSerial: 1, lastSucceeded: false);

        vfx.IsPlayingSuccess.Should().BeFalse();
        vfx.GetBarFraction(0.4f).Should().Be(0.4f, "nothing was repaired");
    }

    private static ItemSlot[] Bag(float swordDurability, int ingots)
    {
        ItemSlot[] slots = new ItemSlot[PlayerInventoryLayout.TotalCapacity];
        slots[SwordSlot] = new ItemSlot(Id("iron_sword"), 1, InstanceId: 1, Durability: swordDurability);
        slots[IngotSlot] = new ItemSlot(Id("iron_ingot"), ingots);
        return slots;
    }

    private static ItemRules SyncedRules()
    {
        string root = FindRepositoryRoot();
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(root, "resources", "config", name + ".json"));
        string ironFamily = "\"item\": \"iron_ingot\", \"qty\": 2 } ],\n      \"items\": [ \"iron_sword\"";
        string repair = Encoding.UTF8.GetString(Read("repair"));
        repair.Should().Contain(ironFamily);
        byte[] patched = Encoding.UTF8.GetBytes(repair.Replace(ironFamily, ironFamily.Replace("\"qty\": 2", "\"qty\": 6", StringComparison.Ordinal), StringComparison.Ordinal));
        ConfigCatalogEntry Entry(string name, byte[] bytes) => new(name, ConfigContentHash.Compute(bytes), 1, bytes);
        SyncedItemCatalog synced = new();
        synced.Apply(new ConfigCatalog(1, [Entry("items", Read("items")), Entry("recipes", Read("recipes")), Entry("enhancement", Read("enhancement")), Entry("repair", patched)]))
            .Should().BeTrue();
        return synced.Rules;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Carbon.slnx not found above the test output.");
    }
}
