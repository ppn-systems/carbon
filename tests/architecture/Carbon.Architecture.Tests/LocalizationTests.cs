// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Graphics.Fonts.TrueType;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// CI guards for <c>resources/localization</c> (ADR-023 D7): key parity, placeholders, usage, NFC and font coverage.
/// How to add a string: <c>resources/localization/README.md</c>.
/// </summary>
public sealed class LocalizationTests
{
    [Fact]
    public void Manifest_ListsEnglishAsDefaultAndEveryLocaleFileExists()
    {
        LocaleManifest manifest = LocalizationRepository.Manifest;

        manifest.DefaultLocale.Should().Be(LocaleCatalog.FallbackLocale, "English is the default language");
        manifest.Locales.Select(l => l.Code).Should().Contain(["en", "vi"]).And.OnlyHaveUniqueItems();
        foreach (LocaleInfo locale in manifest.Locales)
        {
            File.Exists(Path.Combine(LocalizationRepository.LocalizationDirectory, locale.Code + LocaleCatalog.TableExtension)).Should().BeTrue($"locales.ini lists '{locale.Code}'");
            locale.NativeName.Should().NotBeNullOrWhiteSpace();
            locale.Fonts.Should().NotBeEmpty($"'{locale.Code}' needs a font stack");
            foreach (LocaleFontFace face in locale.Fonts)
            {
                File.Exists(Path.Combine(LocalizationRepository.FontsDirectory, face.File)).Should().BeTrue($"font '{face.File}' of '{locale.Code}' must be committed");
                face.PixelSize.Should().BePositive();
            }
        }

        string[] stray = [.. Directory.GetFiles(LocalizationRepository.LocalizationDirectory, "*" + LocaleCatalog.TableExtension)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != "locales" && !manifest.Locales.Any(l => l.Code == name))
            .Select(name => name!)];
        stray.Should().BeEmpty("every locale .ini file must be listed in locales.ini");
    }

    [Fact]
    public void LocaleKeys_AreWellFormedNamespacedKeys()
    {
        foreach ((string code, IReadOnlyDictionary<string, string> table) in LocalizationRepository.Tables)
        {
            string[] bad = [.. table.Keys.Where(k => !LocalizationRepository.IsWellFormedKey(k))];
            bad.Should().BeEmpty($"{code}.ini keys must be lowercase dot-namespaced (e.g. hud.not_enough_mana)");
        }
    }

    [Fact]
    public void Locales_HaveExactlyTheEnglishKeySet()
    {
        HashSet<string> english = [.. LocalizationRepository.English.Keys];
        foreach ((string code, IReadOnlyDictionary<string, string> table) in LocalizationRepository.Tables)
        {
            string[] missing = [.. english.Where(k => !table.ContainsKey(k)).Order(StringComparer.Ordinal)];
            string[] unknown = [.. table.Keys.Where(k => !english.Contains(k)).Order(StringComparer.Ordinal)];

            missing.Should().BeEmpty($"{code}.ini is missing keys that exist in en.ini");
            unknown.Should().BeEmpty($"{code}.ini has keys that en.ini (the source of truth) does not define");
        }
    }

    [Fact]
    public void LocaleValues_AreValidTemplatesWithPlaceholdersMatchingEnglish()
    {
        foreach ((string code, IReadOnlyDictionary<string, string> table) in LocalizationRepository.Tables)
        {
            foreach ((string key, string value) in table)
            {
                TextTemplate.TryParse(value, out TextTemplate template, out string? error).Should().BeTrue($"{code}.ini '{key}': {error}");
                value.Trim().Should().NotBeEmpty($"{code}.ini '{key}' must not be blank");
                if (!LocalizationRepository.English.TryGetValue(key, out string? englishValue))
                {
                    continue;
                }

                TextTemplate.TryParse(englishValue, out TextTemplate englishTemplate, out _).Should().BeTrue();
                template.Placeholders.Order(StringComparer.Ordinal).Should().Equal(
                    englishTemplate.Placeholders.Order(StringComparer.Ordinal),
                    $"{code}.ini '{key}' must use the same placeholders as en.ini");
            }
        }
    }

    [Fact]
    public void LocaleValues_AreNfcNormalized()
    {
        foreach ((string code, IReadOnlyDictionary<string, string> table) in LocalizationRepository.Tables)
        {
            string[] decomposed = [.. table.Where(p => !p.Value.IsNormalized(NormalizationForm.FormC)).Select(p => p.Key)];
            decomposed.Should().BeEmpty($"{code}.ini values must use precomposed (NFC) letters so the font atlas has a glyph for each");
        }
    }

    [Fact]
    public void PluralKeys_ComeInOneOtherPairs()
    {
        IReadOnlyDictionary<string, string> english = LocalizationRepository.English;
        foreach (string key in english.Keys)
        {
            string? baseKey = LocalizationRepository.PluralBase(key);
            if (baseKey is null)
            {
                continue;
            }

            english.Should().ContainKey($"{baseKey}.{PluralRules.One}", $"plural '{baseKey}' needs a singular form");
            english.Should().ContainKey($"{baseKey}.{PluralRules.Other}", $"plural '{baseKey}' needs an other form");
            english.Should().NotContainKey(baseKey, $"'{baseKey}' is a plural; use GameText.Plural instead of a plain key");
        }
    }

    [Fact]
    public void KeysUsedInCode_ExistInEnglish()
    {
        IReadOnlyDictionary<string, string> english = LocalizationRepository.English;
        string[] missing = [.. LocalizationRepository.KeyReferences()
            .Where(r => !english.ContainsKey(r.Text) && !english.ContainsKey($"{r.Text}.{PluralRules.Other}"))
            .Select(r => $"{r.Path}:{r.Line} \"{r.Text}\"")];

        missing.Should().BeEmpty("every localization key referenced in code must be defined in resources/localization/en.ini");
    }

    [Fact]
    public void ConfigDerivedKeys_ExistForEveryStableId()
    {
        IReadOnlyDictionary<string, string> english = LocalizationRepository.English;
        string[] missing = [.. LocalizationRepository.DerivedKeys().Where(k => !english.ContainsKey(k)).Order(StringComparer.Ordinal)];

        missing.Should().BeEmpty("every item, monster and biome id needs its display keys in en.ini (and vi.ini)");
    }

    [Fact]
    public void EnglishKeys_AreAllUsed()
    {
        HashSet<string> used = [.. LocalizationRepository.DerivedKeys()];
        foreach (SourceLiteral reference in LocalizationRepository.KeyReferences())
        {
            used.Add(reference.Text);
            used.Add($"{reference.Text}.{PluralRules.One}");
            used.Add($"{reference.Text}.{PluralRules.Other}");
        }

        string[] unused = [.. LocalizationRepository.English.Keys.Where(k => !used.Contains(k)).Order(StringComparer.Ordinal)];
        unused.Should().BeEmpty("en.ini keys must be referenced by code or derived from a config id; delete stale keys from every locale");
    }

    [Fact]
    public void FontStacks_CoverEveryCharacterOfTheirLocale()
    {
        Dictionary<string, byte[]> fonts = new(StringComparer.Ordinal);
        string nativeNames = string.Concat(LocalizationRepository.Manifest.Locales.Select(l => l.NativeName));
        List<string> gaps = [];

        foreach (LocaleInfo locale in LocalizationRepository.Manifest.Locales)
        {
            byte[][] stack = [.. locale.Fonts.Select(f => fonts.TryGetValue(f.File, out byte[]? data)
                ? data
                : fonts[f.File] = File.ReadAllBytes(Path.Combine(LocalizationRepository.FontsDirectory, f.File)))];

            // The language picker shows every locale's native name in the active locale's font.
            string text = string.Concat(LocalizationRepository.Tables[locale.Code].Values) + nativeNames;
            foreach (int codePoint in text.EnumerateRunes().Select(r => r.Value).Distinct().Order())
            {
                if (codePoint is ' ' or '\n' or '\t')
                {
                    continue;
                }

                if (!stack.Any(font => TrueTypeFontLoader.HasGlyph(font, codePoint)))
                {
                    gaps.Add($"{locale.Code}: '{char.ConvertFromUtf32(codePoint)}' U+{codePoint.ToString("X4", CultureInfo.InvariantCulture)}");
                }
            }
        }

        gaps.Should().BeEmpty("every character in a locale file must have a glyph in that locale's font stack (resources/localization/locales.ini)");
    }

    [Fact]
    public void FontStacks_CharactersFallInsideTheBakedRanges()
    {
        IReadOnlyList<CodePointRange> baked = UnicodeGlyphRanges.LatinFull;
        string[] outside = [.. LocalizationRepository.Tables.Values
            .SelectMany(t => t.Values)
            .SelectMany(v => v.EnumerateRunes())
            .Select(r => r.Value)
            .Distinct()
            .Where(cp => cp is not ('\n' or '\t') && !baked.Any(range => range.Contains(cp)))
            .Select(cp => $"U+{cp.ToString("X4", CultureInfo.InvariantCulture)}")];

        outside.Should().BeEmpty("GameFontLoader only bakes UnicodeGlyphRanges.LatinFull; extend the ranges before using other scripts");
    }
}
