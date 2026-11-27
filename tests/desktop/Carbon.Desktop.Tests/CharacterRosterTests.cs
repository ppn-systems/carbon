// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Menu;
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
    public void Randomize_SeededRandom_PicksValidVariantsAndPacksThem()
    {
        HeroAppearance appearance = new();

        appearance.Randomize(new Random(7));

        byte[] traits = [appearance.HairStyle, appearance.HairColor, appearance.FaceStyle, appearance.SkinTone];
        traits.Should().OnlyContain(t => t < CharacterAppearance.TraitVariantCount);
        appearance.Preset.Should().Be(CharacterAppearance.Pack(appearance.HairStyle, appearance.HairColor, appearance.FaceStyle, appearance.SkinTone));

        Random a = new(7);
        byte[] expected = [(byte)a.Next(4), (byte)a.Next(4), (byte)a.Next(4), (byte)a.Next(4)];
        traits.Should().Equal(expected, "hair, color, eyes, skin are drawn in that order");
    }
}
