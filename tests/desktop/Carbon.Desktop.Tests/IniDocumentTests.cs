// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Rendering.Hud;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The hand-written INI reader used by locale files and user settings (ADR-023 D1).</summary>
public sealed class IniDocumentTests
{
    [Fact]
    public void Parse_SectionsNamespaceKeysAndTrimWhitespace()
    {
        IniDocument doc = IniDocument.Parse("[hud]\nnot_enough_mana   =   Not enough Mana!  \n[item]\niron_sword.name=Iron Sword\n");

        doc.ToDictionary().Should().Equal(new System.Collections.Generic.Dictionary<string, string>
        {
            ["hud.not_enough_mana"] = "Not enough Mana!",
            ["item.iron_sword.name"] = "Iron Sword",
        });
        doc.Sections.Should().Equal("hud", "item");
    }

    [Fact]
    public void Parse_SkipsCommentsAndBlankLines()
    {
        IniDocument doc = IniDocument.Parse("; comment\n# another\n\n[hud]\n  ; indented comment\nkey = value ; not a comment\n");

        doc.Entries.Should().ContainSingle();
        doc.TryGetValue("hud.key", out string value).Should().BeTrue();
        value.Should().Be("value ; not a comment", "comments only start a line; values are raw");
    }

    [Fact]
    public void Parse_DecodesEscapesAndLineContinuation()
    {
        IniDocument doc = IniDocument.Parse("[t]\nlines = a\\nb\\tc\\\\d\nlong = first \\\n  second\nslash = ends with \\\\\n");

        doc.TryGetValue("t.lines", out string lines).Should().BeTrue();
        lines.Should().Be("a\nb\tc\\d");
        doc.TryGetValue("t.long", out string joined).Should().BeTrue();
        joined.Should().Be("first second");
        doc.TryGetValue("t.slash", out string slash).Should().BeTrue();
        slash.Should().Be("ends with \\", "an escaped backslash at the end is not a continuation");
    }

    [Fact]
    public void Parse_KeepsUtf8AndPlaceholdersVerbatim()
    {
        IniDocument doc = IniDocument.Parse("[hud]\npicked = Đã nhặt {count} {name}\r\n");

        doc.TryGetValue("hud.picked", out string value).Should().BeTrue();
        value.Should().Be("Đã nhặt {count} {name}");
    }

    [Theory]
    [InlineData("[hud]\nkey = a\nkey = b\n", 3, "duplicate key 'hud.key'")]
    [InlineData("[hud]\na = 1\n[hud]\nb = 2\n", 3, "duplicate section")]
    [InlineData("[hud]\nno equals sign\n", 2, "expected 'key = value'")]
    [InlineData("[hud\nkey = a\n", 1, "must end with ']'")]
    [InlineData("[Hud]\n", 1, "invalid section name")]
    [InlineData("[hud]\nBad-Key = a\n", 2, "invalid key")]
    [InlineData("[hud]\n = a\n", 2, "invalid key")]
    [InlineData("[hud]\nkey = bad \\q escape\n", 2, "unknown escape")]
    [InlineData("[hud]\nkey = continues \\", 2, "end of file")]
    public void Parse_RejectsMalformedInputWithFileAndLine(string text, int line, string reason)
    {
        Action parse = () => IniDocument.Parse(text, "vi.ini");

        IniFormatException ex = parse.Should().Throw<IniFormatException>().Which;
        ex.Line.Should().Be(line);
        ex.Message.Should().StartWith($"vi.ini:{line}:").And.Contain(reason);
    }

    [Fact]
    public void Parse_DuplicateKeyAcrossSectionSyntax_IsRejected()
    {
        Action parse = () => IniDocument.Parse("hud.key = a\n[hud]\nkey = b\n", "en.ini");

        parse.Should().Throw<IniFormatException>().WithMessage("*duplicate key 'hud.key'*");
    }

    [Fact]
    public void Load_RejectsUtf8ByteOrderMark()
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-bom-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[hud]\nkey = a\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Action load = () => IniDocument.Load(path);

            load.Should().Throw<IniFormatException>().WithMessage("*byte order mark*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Escape_RoundTripsThroughParse()
    {
        const string original = "tab\there\nnew line \\ slash";

        IniDocument doc = IniDocument.Parse("[x]\nv = " + IniDocument.Escape(original) + "\n");

        doc.TryGetValue("x.v", out string value).Should().BeTrue();
        value.Should().Be(original);
    }

    [Fact]
    public void LocaleManifest_FromIni_ReadsLocalesAndFontStacks()
    {
        IniDocument doc = IniDocument.Parse("[general]\ndefault = en\n[locale.en]\nname = English\nfonts = a.ttf@16, b.ttf@12.5\n[locale.vi]\nname = Tiếng Việt\nfonts = b.ttf@20\n");

        LocaleManifest manifest = LocaleManifest.FromIni(doc);

        manifest.DefaultLocale.Should().Be("en");
        manifest.Locales.Should().HaveCount(2);
        manifest.Locales[0].Fonts.Should().HaveCount(2);
        manifest.Locales[0].Fonts[1].File.Should().Be("b.ttf");
        manifest.Locales[0].Fonts[1].PixelSize.Should().Be(12.5f);
        manifest.Locales[1].NativeName.Should().Be("Tiếng Việt");
    }

    [Fact]
    public void LocaleManifest_FromIni_RejectsMalformedFontEntry()
    {
        IniDocument doc = IniDocument.Parse("[locale.en]\nname = English\nfonts = a.ttf\n");

        Action read = () => LocaleManifest.FromIni(doc);

        read.Should().Throw<IniFormatException>().WithMessage("*file@pixelSize*");
    }

    [Fact]
    public void UiSettings_SaveAndLoad_RoundTripsLanguageThroughSettingsIni()
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-settings-{Guid.NewGuid():N}.ini");
        try
        {
            new UiSettings { Language = "vi", CameraZoomPreset = 0, ShowMinimap = true }.Save(path);

            string text = File.ReadAllText(path);
            UiSettings loaded = UiSettings.LoadOrDefault(path);

            text.Should().Contain("[general]").And.Contain("language = vi");
            loaded.Language.Should().Be("vi");
            loaded.CameraZoomPreset.Should().Be(0);
            loaded.ShowMinimap.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UiSettings_LoadOrDefault_MalformedFileFallsBackToDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-settings-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[general]\nthis line is broken\n");

            UiSettings loaded = UiSettings.LoadOrDefault(path);

            loaded.Language.Should().BeEmpty();
            loaded.CameraZoomPreset.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
