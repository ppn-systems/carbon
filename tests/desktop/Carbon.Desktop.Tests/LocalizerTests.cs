// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Maps.Biomes;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Behavior of the ADR-023 client localizer: templates, plurals, fallback and language selection.</summary>
public sealed class LocalizerTests
{
    [Fact]
    public void TextTemplate_WithNamedPlaceholders_SubstitutesByName()
    {
        TextTemplate.TryParse("{name} picked up {count} coins", out TextTemplate template, out string? error).Should().BeTrue(error);

        string text = template.Format(new TextArg("count", 12), new TextArg("name", "Hero"));

        text.Should().Be("Hero picked up 12 coins");
        template.Placeholders.Should().Equal(["name", "count"]);
    }

    [Fact]
    public void TextTemplate_WithEscapedBraces_RendersLiteralBraces()
    {
        TextTemplate.TryParse("{{not}} a {value}", out TextTemplate template, out _).Should().BeTrue();

        template.Format(new TextArg("value", "slot")).Should().Be("{not} a slot");
    }

    [Fact]
    public void TextTemplate_WithoutPlaceholders_ReturnsCachedLiteralWithoutAllocating()
    {
        TextTemplate.TryParse("Not enough Mana!", out TextTemplate template, out _).Should().BeTrue();

        template.LiteralText.Should().Be("Not enough Mana!");
        template.Format().Should().BeSameAs(template.LiteralText, "a placeholder-free value is returned as the string stored at load");
    }

    [Fact]
    public void TextTemplate_WithMissingArgument_KeepsPlaceholderVisible()
    {
        TextTemplate.TryParse("Hello {name}", out TextTemplate template, out _).Should().BeTrue();

        template.Format().Should().Be("Hello {name}");
    }

    [Theory]
    [InlineData("broken {name")]
    [InlineData("stray } brace")]
    [InlineData("bad {Name}")]
    [InlineData("empty {}")]
    public void TextTemplate_WithInvalidSyntax_FailsToParse(string source)
    {
        TextTemplate.TryParse(source, out _, out string? error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("en", 1, "one")]
    [InlineData("en", 0, "other")]
    [InlineData("en", 2, "other")]
    [InlineData("vi", 1, "other")]
    [InlineData("vi", 5, "other")]
    public void PluralRules_Select_FollowsLanguageRule(string language, long count, string expected) =>
        PluralRules.Select(language, count).Should().Be(expected);

    [Fact]
    public void Plural_InEnglishAndVietnamese_PicksFormAndBindsCount()
    {
        Localizer localizer = CreateLocalizer(out _);

        localizer.Plural("hud.coins", 1).Should().Be("1 coin");
        localizer.Plural("hud.coins", 3).Should().Be("3 coins");

        localizer.SetLanguage("vi").Should().BeTrue();
        localizer.Plural("hud.coins", 1).Should().Be("1 xu");
    }

    [Fact]
    public void Text_WhenKeyMissingInActiveLanguage_FallsBackToEnglishAndReportsOnce()
    {
        Localizer localizer = CreateLocalizer(out List<string> reports);
        localizer.SetLanguage("vi").Should().BeTrue();

        localizer.Text("hud.only_english").Should().Be("Only English");
        localizer.Text("hud.only_english").Should().Be("Only English");

        reports.Should().ContainSingle("a missing key is logged once per language, not every frame");
    }

    [Fact]
    public void Text_WhenKeyMissingEverywhere_ReturnsKeyAndReportsOnce()
    {
        Localizer localizer = CreateLocalizer(out List<string> reports);

        localizer.Text("hud.unknown_key").Should().Be("hud.unknown_key");
        localizer.Text("hud.unknown_key").Should().BeSameAs(localizer.Text("hud.unknown_key"));
        reports.Should().ContainSingle();
        localizer.TryGet("hud.unknown_key", out _).Should().BeFalse();
    }

    [Fact]
    public void SetLanguage_WithKnownCode_SwitchesTextAndBumpsVersion()
    {
        Localizer localizer = CreateLocalizer(out _);
        int changes = 0;
        localizer.LanguageChanged += () => changes++;

        localizer.SetLanguage("VI").Should().BeTrue("codes are case-insensitive");

        localizer.Language.Should().Be("vi");
        localizer.Text("settings.language").Should().Be("Ngôn ngữ");
        localizer.Version.Should().Be(1);
        changes.Should().Be(1);
    }

    [Fact]
    public void SetLanguage_WithUnknownCode_KeepsCurrentLanguage()
    {
        Localizer localizer = CreateLocalizer(out _);

        localizer.SetLanguage("xx").Should().BeFalse();

        localizer.Language.Should().Be("en");
        localizer.Version.Should().Be(0);
    }

    [Fact]
    public void SelectLanguage_EnvironmentVariable_WinsOverSavedSetting()
    {
        Localizer localizer = CreateLocalizer(out _);

        string language = GameText.SelectLanguage(localizer, savedLanguage: "en", readEnv: name => name == GameText.LanguageEnvironmentVariable ? "vi" : null);

        language.Should().Be("vi");
    }

    [Fact]
    public void SelectLanguage_WithoutEnvironment_UsesSavedSettingThenDefault()
    {
        Localizer saved = CreateLocalizer(out _);
        Localizer fresh = CreateLocalizer(out _);

        GameText.SelectLanguage(saved, "vi", static _ => null).Should().Be("vi");
        GameText.SelectLanguage(fresh, null, static _ => null).Should().Be("en");
    }

    [Theory]
    [InlineData(BiomeType.LushPlains, "biome.lush_plains")]
    [InlineData(BiomeType.DeepOcean, "biome.deep_ocean")]
    [InlineData(BiomeType.Savanna, "biome.savanna")]
    public void BiomeKey_DerivesSnakeCaseKeyFromEnumName(BiomeType biome, string expected) =>
        GameText.BiomeKey(biome).Should().Be(expected);

    [Fact]
    public void CachedText_WithUnchangedInputs_ReturnsTheSameInstance()
    {
        Localizer localizer = CreateLocalizer(out _);
        CachedText cache = new(localizer);

        string first = cache.Plural("hud.coins", 3);
        string second = cache.Plural("hud.coins", 3);

        second.Should().BeSameAs(first, "per-frame labels must not allocate while nothing changed");
    }

    [Fact]
    public void CachedText_WhenArgumentOrLanguageChanges_Reformats()
    {
        Localizer localizer = CreateLocalizer(out _);
        CachedText cache = new(localizer);

        cache.Plural("hud.coins", 3).Should().Be("3 coins");
        cache.Plural("hud.coins", 1).Should().Be("1 coin");

        localizer.SetLanguage("vi").Should().BeTrue();
        cache.Plural("hud.coins", 1).Should().Be("1 xu", "a language switch bumps ILocalizer.Version");
    }

    [Fact]
    public void TextArg_SameAs_ComparesNameAndValueWithoutBoxing()
    {
        new TextArg("count", 2).SameAs(new TextArg("count", 2)).Should().BeTrue();
        new TextArg("count", 2).SameAs(new TextArg("count", 3)).Should().BeFalse();
        new TextArg("name", "Hero").SameAs(new TextArg("name", "Hero")).Should().BeTrue();
        new TextArg("name", "Hero").SameAs(new TextArg("item", "Hero")).Should().BeFalse();
        new TextArg("count", 2).SameAs(new TextArg("count", "2")).Should().BeFalse();
    }

    private static Localizer CreateLocalizer(out List<string> reports)
    {
        LocaleManifest manifest = new()
        {
            DefaultLocale = "en",
            Locales =
            [
                new LocaleInfo { Code = "en", NativeName = "English" },
                new LocaleInfo { Code = "vi", NativeName = "Tiếng Việt" },
            ],
        };

        Dictionary<string, IReadOnlyDictionary<string, string>> tables = new(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["settings.language"] = "Language",
                ["hud.only_english"] = "Only English",
                ["hud.coins.one"] = "{count} coin",
                ["hud.coins.other"] = "{count} coins",
            },
            ["vi"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["settings.language"] = "Ngôn ngữ",
                ["hud.coins.one"] = "{count} xu",
                ["hud.coins.other"] = "{count} xu",
            },
        };

        List<string> sink = [];
        reports = sink;
        return new Localizer(LocaleCatalog.FromTables(manifest, tables), sink.Add);
    }
}
