// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Carbon.Combat.Statuses.Config;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-036 D6: effect levels as Roman numerals on the strip icon, in the tooltip and in the details panel.</summary>
public sealed class StatusEffectLevelTests
{
    private static readonly Localizer s_english = CreateLocalizer("en");
    private static readonly Localizer s_vietnamese = CreateLocalizer("vi");

    [Theory]
    [InlineData(1, "I")]
    [InlineData(2, "II")]
    [InlineData(3, "III")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(10, "X")]
    [InlineData(11, "11")]
    [InlineData(255, "255")]
    [InlineData(0, "I")]
    public void Numeral_Levels_AreRomanUpToTenThenArabic(int level, string expected) =>
        new StatusLevelLabels(s_english).Numeral((byte)level).Should().Be(expected);

    [Fact]
    public void NameWithLevel_EnglishAndVietnamese_AppendTheNumeralExceptAtLevelOne()
    {
        StatusLevelLabels english = new(s_english);
        StatusLevelLabels vietnamese = new(s_vietnamese);

        english.NameWithLevel("Poisoned", 2).Should().Be("Poisoned II");
        english.NameWithLevel("Poisoned", 1).Should().Be("Poisoned", "level I keeps the bare name, as in Minecraft");
        english.IconNumeral(1).Should().BeNull("level I draws no numeral on the icon");
        english.PanelLine(3).Should().Be("Level: III");
        vietnamese.NameWithLevel("Trúng độc", 4).Should().Be("Trúng độc IV");
        vietnamese.PanelLine(2).Should().Be("Cấp độ: II");
    }

    [Fact]
    public void Numeral_LanguageSwitch_ReadsTheNewLanguagesNumerals()
    {
        LocaleManifest manifest = new()
        {
            DefaultLocale = "en",
            Locales = [new LocaleInfo { Code = "en", NativeName = "English" }, new LocaleInfo { Code = "xx", NativeName = "Test" }],
        };
        Dictionary<string, IReadOnlyDictionary<string, string>> tables = new(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["status_levels.numeral.2"] = "II" },
            ["xx"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["status_levels.numeral.2"] = "ii" },
        };
        Localizer localizer = new(LocaleCatalog.FromTables(manifest, tables));
        StatusLevelLabels labels = new(localizer);
        labels.Numeral(2).Should().Be("II");

        localizer.SetLanguage("xx").Should().BeTrue();

        labels.Numeral(2).Should().Be("ii", "a cached numeral is dropped when the language changes");
    }

    [Fact]
    public void Prepare_PoisonLevelTwoSelected_IconShowsTwoAndThePanelShowsTheLevel()
    {
        ILocalizer previous = GameText.Current;
        GameText.Current = s_english;
        try
        {
            LocalStatusEffect poison = new(1, 2, 8f, 10f, 4f, 1, 106, Level: 2);
            LocalStatusEffect rested = new(9, 1, 300f, 600f, 1.25f, 5, 0);
            IWorldReadModel world = Substitute.For<IWorldReadModel>();
            world.LocalStatusEffects.Returns([poison, rested]);
            world.StatusEffectDefinitions.Returns(StatusEffectCatalog.LoadShippedDefaults());
            StatusEffectStripWidget strip = new();
            strip.Open(1);

            using BitmapFont font = CreateFont();
            strip.Prepare(world, font, mouse: null, viewportWidth: 1280);

            strip.IconNumeral(in poison).Should().Be("II", "the icon of a level-2 effect shows II");
            strip.IconNumeral(in rested).Should().BeNull();
            strip.PanelTitle.Should().Be("Poisoned II");
            strip.PanelLines.Should().Contain("Level: II", "the details panel states the level");
            strip.PanelLines.Should().Contain("Stacks: 2", "level and stacks are shown apart");
        }
        finally
        {
            GameText.Current = previous;
        }
    }

    [Fact]
    public void Prepare_EffectWithoutLevels_HasNoLevelLine()
    {
        ILocalizer previous = GameText.Current;
        GameText.Current = s_english;
        try
        {
            IWorldReadModel world = Substitute.For<IWorldReadModel>();
            world.LocalStatusEffects.Returns([new LocalStatusEffect(9, 1, 300f, 600f, 1.25f, 5, 0)]);
            world.StatusEffectDefinitions.Returns(StatusEffectCatalog.LoadShippedDefaults());
            StatusEffectStripWidget strip = new();
            strip.Open(9);

            using BitmapFont font = CreateFont();
            strip.Prepare(world, font, mouse: null, viewportWidth: 1280);

            strip.PanelTitle.Should().Be("Rested");
            strip.PanelLines.Should().NotContain(line => line.StartsWith("Level", System.StringComparison.Ordinal), "rested has a single level");
        }
        finally
        {
            GameText.Current = previous;
        }
    }

    [Fact]
    public void ToLocalStatusEffects_SyncWithLevels_KeepsEachLevelAndReadsMissingOrZeroAsOne()
    {
        using StatusEffectSync sync = new()
        {
            EffectIds = [1, 2, 3],
            Stacks = [2, 1, 1],
            RemainingMs = [8000U, 2000U, 4000U],
            DurationMs = [10_000U, 3000U, 6000U],
            Magnitudes = [4f, 0.5f, 1.25f],
            SourceKinds = [1, 1, 1],
            SourceIds = [106, 101, 103],
            Levels = [2, 0],
        };

        LocalStatusEffect[] effects = MultiplayerSession.ToLocalStatusEffects(sync);

        effects.Select(static e => e.Level).Should().Equal([2, 1, 1], "level 0 and a short array both mean I");
        (effects[0].EffectId, effects[0].Stacks, effects[0].RemainingSeconds).Should().Be((1, 2, 8f));
    }

    [Fact]
    public void ComposeText_PulseOfALevelThreeEffect_ShowsTheNumeral()
    {
        StatusLevelLabels labels = new(s_english);

        CombatFeedbackTracker.ComposeText(new CombatFeedbackEvent(default, 2.4f, false, false, StatusEffectId: 1, StatusEffectLevel: 3), labels)
            .Should().Be("-3 III", "CombatHit.StatusEffectLevel names the pulse's level (ADR-036 D4)");
        CombatFeedbackTracker.ComposeText(new CombatFeedbackEvent(default, 2.4f, false, false, StatusEffectId: 1, StatusEffectLevel: 1), labels)
            .Should().Be("-3", "level I keeps the bare number, like the strip icon");
        CombatFeedbackTracker.ComposeText(new CombatFeedbackEvent(default, 7f, true, false, StatusEffectLevel: 3), labels)
            .Should().Be("-7!", "a direct hit has no effect level");
    }

    private static Localizer CreateLocalizer(string language)
    {
        Localizer localizer = new(LocaleCatalog.Load(LocaleCatalog.TryFindDirectory()!));
        _ = localizer.SetLanguage(language);
        return localizer;
    }

    private static BitmapFont CreateFont()
    {
        BitmapFontAtlas atlas = new(Substitute.For<ITexture>());
        for (char c = ' '; c <= '~'; c++)
        {
            atlas.AddGlyph(new FontGlyph(c, new RectangleF(0f, 0f, 8f, 16f), 0f, 0f, 8f));
        }

        return new BitmapFont("test", atlas, new FontMetrics(fontSize: 16f, lineHeight: 20f, baseLine: 16f, spaceWidth: 6f));
    }
}
