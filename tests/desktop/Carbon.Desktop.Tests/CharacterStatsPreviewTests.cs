// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Progression;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-021: the character panel previews stats from the synced progression.json with the server's own math.</summary>
public sealed class CharacterStatsPreviewTests
{
    private static readonly byte[] s_progressionJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "progression.json"));
    private static readonly ProgressionConfig s_config = ProgressionConfig.LoadFromJson(System.Text.Encoding.UTF8.GetString(s_progressionJson));
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();

    [Fact]
    public void Update_HoveringPlus_PreviewEqualsServerComputationWithOneMorePoint()
    {
        CharacterStatsPreview preview = new();

        Run(preview, s_config, 20, 10, PrimaryAttributeId.Vitality, vitality: 4, power: 6).Should().BeTrue();

        CharacterProgressionState expected = new();
        expected.SetAttribute(PrimaryAttributeId.Vitality, 5);
        expected.SetAttribute(PrimaryAttributeId.Power, 6);
        DerivedCharacterStats server = CharacterStatCalculator.Compute(s_rules, s_config, 20, Inventory(), 0, expected);
        preview.Preview.Should().Be(server, "the client preview runs the same calculator the server runs");
        preview.PreviewAttribute.Should().Be(PrimaryAttributeId.Vitality);
        (preview.Preview.MaxHealth - preview.Current.MaxHealth).Should().BeApproximately(s_config.Attributes.Vitality.MaxHealth, 1e-3f);
        preview.PreviewChanges(0).Should().BeTrue("Max HP changes with a Vitality point");
        preview.PreviewChanges(7).Should().BeFalse("crit does not change with Vitality");
    }

    [Fact]
    public void Update_NoUnspentPoints_ShowsNoPreview()
    {
        CharacterStatsPreview preview = new();
        Run(preview, s_config, 20, 0, PrimaryAttributeId.Power).Should().BeTrue();

        preview.PreviewAttribute.Should().BeNull("the server would reject the point, so nothing is previewed");
        preview.Preview.Should().Be(preview.Current);
    }

    [Fact]
    public void Update_BeforeConfigSync_ReportsNoStats()
    {
        CharacterStatsPreview preview = new();
        Run(preview, null, 5, 3, PrimaryAttributeId.Power).Should().BeFalse();
        preview.DescribePerPoint(PrimaryAttributeId.Vitality).Should().BeEmpty();
    }

    [Fact]
    public void DescribePerPoint_ShippedConfig_ListsEveryNonZeroEffect()
    {
        CharacterStatsPreview preview = new();
        _ = Run(preview, s_config, 5, 3, null);

        preview.DescribePerPoint(PrimaryAttributeId.Vitality).Should().Be("+14 HP +0.7 DEF +0.15 HP/s");
        preview.DescribePerPoint(PrimaryAttributeId.Dexterity).Should().Contain("% Crit").And.Contain("% CritDmg");
        foreach (PrimaryAttributeId attr in CharacterStatsPreview.Attributes)
        {
            preview.DescribePerPoint(attr).Should().NotBeEmpty($"{attr} must show what a point does");
        }
    }

    [Fact]
    public void FormatStat_EveryLine_HasLabelAndValue()
    {
        CharacterStatsPreview preview = new();
        _ = Run(preview, s_config, 30, 0, null, vitality: 20);

        for (int i = 0; i < CharacterStatsPreview.DisplayRowCount; i++)
        {
            CharacterStatsPreview.StatLabel(i).Should().NotBeEmpty();
            preview.FormatStat(i, preview.Current).Should().NotBeEmpty();
        }

        preview.FormatStat(5, preview.Current).Should().EndWith("%)", "defense shows its reduction against a same-level monster");
    }

    [Fact]
    public void SyncedProgressionConfigApply_ValidAndInvalidDocuments_KeepsLastGoodConfig()
    {
        SyncedProgressionConfig.Apply(Catalog(s_progressionJson)).Should().BeTrue();
        ProgressionConfig? good = SyncedProgressionConfig.Current;
        good.Should().NotBeNull();

        SyncedProgressionConfig.Apply(Catalog("{\"maxLevel\": 1}"u8.ToArray())).Should().BeFalse();
        SyncedProgressionConfig.Apply(new ConfigCatalog(9, [])).Should().BeFalse();
        SyncedProgressionConfig.Current.Should().BeSameAs(good);
    }

    private static ConfigCatalog Catalog(byte[] json) =>
        new(1, [new ConfigCatalogEntry(ProgressionConfig.DocumentName, ConfigContentHash.Compute(json), 1, json)]);

    private static InventoryContainer Inventory()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(0, new ItemSlot(StarterGameCatalog.IronSword, 1, 0UL, 200f));
        inv.SetSlot(PlayerInventoryLayout.ChestSlot, new ItemSlot(StarterGameCatalog.IronChestplate, 1, 0UL, 260f));
        return inv;
    }

    private static bool Run(CharacterStatsPreview preview, ProgressionConfig? config, ushort level, ushort unspent, PrimaryAttributeId? hovered, ushort vitality = 0, ushort power = 0)
    {
        // Points in CharacterStatsPreview.Attributes order: Vitality, Power, Dexterity, Agility, Wisdom.
        ushort[] points = [vitality, power, 0, 0, 0];
        InventoryContainer inv = Inventory();
        ItemSlot[] slots = new ItemSlot[inv.Capacity];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = inv.GetSlot(i);
        }

        return preview.Update(config, s_rules, level, unspent, points, slots, 0, hovered);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Carbon.slnx not found above the test output directory.");
    }
}
