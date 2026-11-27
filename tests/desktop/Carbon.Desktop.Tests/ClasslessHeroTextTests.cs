// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Localization;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Roster rows, the dossier and the skills title name a classless hero (ADR-030) in the player's language.</summary>
public sealed class ClasslessHeroTextTests
{
    [Fact]
    public void ClassName_ClasslessHero_IsTheLocalizedSurvivorLabel()
    {
        string label = GameText.ClassName(HeroArchetypeClass.None);

        label.Should().Be(GameText.Get("character_creator.survivor"));
        label.Should().NotBe("character_creator.survivor", "the key must exist in the locale files");
        label.Should().NotBe(nameof(HeroArchetypeClass.None), "the enum name is not player text");
    }

    [Fact]
    public void ClassName_HeroCreatedWithAClass_KeepsItsClassName() =>
        GameText.ClassName(HeroArchetypeClass.Ranger).Should().Be(GameText.Get("character.class.ranger"));
}
