// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Items.Races;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The character screen's roster and appearance models, for the paths the scene-level tests cannot reach.</summary>
public sealed class CharacterRosterTests
{
    private static readonly CharacterRosterEntry s_a = new(Guid.NewGuid(), "Ayla", 1, 1, 0f, 0f);
    private static readonly CharacterRosterEntry s_b = new(Guid.NewGuid(), "Bram", 1, 1, 0f, 0f);
    private static readonly CharacterRosterEntry s_c = new(Guid.NewGuid(), "Cora", 1, 1, 0f, 0f);

    [Fact]
    public void Load_ShorterRoster_ClampsTheSelectionToTheLastHero()
    {
        CharacterRoster roster = new();
        roster.Load([s_a, s_b, s_c]);
        roster.TrySelect(2).Should().BeTrue();

        roster.Load([s_a, s_b]);

        roster.SelectedIndex.Should().Be(1, "the selected row no longer exists, so the last hero is selected");
    }

    [Fact]
    public void Load_EmptyRoster_KeepsTheSelectionAtZeroWithoutAHero()
    {
        CharacterRoster roster = new();
        roster.Load([s_a, s_b]);
        roster.TrySelect(1).Should().BeTrue();

        roster.Load([]);

        roster.SelectedIndex.Should().Be(0);
        roster.HasSelection.Should().BeFalse();
        roster.HasFreeSlot.Should().BeTrue();
    }

    [Fact]
    public void Load_AfterSelectAfterNextLoad_SelectsThatHeroOnceThenForgetsTheName()
    {
        CharacterRoster roster = new();
        roster.SelectAfterNextLoad("CORA");

        roster.Load([s_a, s_b, s_c]);
        roster.SelectedIndex.Should().Be(2, "the name matches ignoring case");

        roster.TrySelect(0).Should().BeTrue();
        roster.Load([s_a, s_b, s_c]);
        roster.SelectedIndex.Should().Be(0, "the name applies to one load only");
    }

    [Fact]
    public void SelectAndCycle_EmptyOrOutOfRange_ChangeNothing()
    {
        CharacterRoster roster = new();

        roster.SelectNext();
        roster.SelectPrevious();
        roster.TrySelect(0).Should().BeFalse();
        roster.SelectedIndex.Should().Be(0);

        roster.Load([s_a]);
        roster.TrySelect(-1).Should().BeFalse();
        roster.TrySelect(1).Should().BeFalse();
        roster.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void HasFreeSlot_FourHeroes_IsFalse()
    {
        CharacterRoster roster = new();
        roster.Load([s_a, s_b, s_c, new CharacterRosterEntry(Guid.NewGuid(), "Dex", 1, 1, 0f, 0f)]);

        roster.HasFreeSlot.Should().BeFalse("an account holds at most four heroes");
    }

    [Fact]
    public void Look_BeforeRacesSync_IsDefault()
    {
        HeroAppearance appearance = new();

        appearance.Look.Should().Be(default(CharacterLook), "the creator has nothing to offer before races.json arrives");
        appearance.OptionCount.Should().Be(0);
    }

    [Fact]
    public void Bind_ShippedRaces_StartsOnFirstRaceBodyAndChoices()
    {
        HeroAppearance appearance = new();

        appearance.Bind(TestRaces.Synced().Current);

        appearance.Look.Should().Be(new CharacterLook(1, 0, [0, 0, 0, 0]), "human, male body, first choice of each of its four options");
    }

    [Fact]
    public void CycleOption_PastLastChoice_WrapsAndMovesOnlyThatOption()
    {
        HeroAppearance appearance = new();
        appearance.Bind(TestRaces.Synced().Current);

        appearance.CycleOption(3, -1);
        appearance.CycleOption(1, 1);
        appearance.CycleOption(1, 4);

        appearance.Look.Should().Be(new CharacterLook(1, 0, [0, 1, 0, 3]), "skin wraps 0 → 3 and hair colour 1 + 4 wraps back to 1");
    }

    [Fact]
    public void Bind_NewCatalogWithFewerChoices_ClampsChoicesAndKeepsRace()
    {
        HeroAppearance appearance = new();
        appearance.Bind(TestRaces.Synced().Current);
        appearance.CycleOption(0, 3);

        RaceCatalog smaller = RaceCatalog.LoadFromJson("""
            {"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human",
             "bodies":[{"id":0,"code":"male","nameKey":"race.body.male","rig":"characters/base_hero.cvg.json"}],
             "options":[{"code":"hair_style","nameKey":"appearance.hair","role":"hair","choices":[{"nameKey":"appearance.hair.crop","part":"hair_crop"}]}]}]}
            """);
        appearance.Bind(smaller);

        appearance.Look.Should().Be(new CharacterLook(1, 0, [0]), "a choice the new catalog no longer offers falls back to its first");
    }

    [Fact]
    public void Randomize_SeededRandom_PicksAValidChoicePerOption()
    {
        HeroAppearance appearance = new();
        RaceCatalog races = TestRaces.Synced().Current!;
        appearance.Bind(races);

        appearance.Randomize(new Random(7));

        races.Check(appearance.Look).Should().Be(CharacterLookStatus.Valid);
        Random a = new(7);
        byte[] expected = [(byte)a.Next(4), (byte)a.Next(4), (byte)a.Next(4), (byte)a.Next(4)];
        appearance.Look.Should().Be(new CharacterLook(1, 0, expected), "options are drawn in the race's order, each within its choices");
    }
}
