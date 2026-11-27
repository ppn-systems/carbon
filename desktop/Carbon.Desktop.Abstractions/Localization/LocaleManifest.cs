// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Carbon.Desktop.Abstractions.Configuration;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// <c>resources/localization/locales.ini</c>: the selectable locales and their font stacks (ADR-023 D1, D5).
/// </summary>
/// <remarks>
/// <code>
/// [general]
/// default = en
///
/// [locale.vi]
/// name = Tiếng Việt
/// fonts = vt323.ttf@20
/// </code>
/// <c>fonts</c> is a comma-separated, primary-first list of <c>file@pixelSize</c> under <c>resources/fonts</c>.
/// </remarks>
public sealed class LocaleManifest
{
    private const string LocaleSectionPrefix = "locale.";

    /// <summary>Gets or sets the default locale code.</summary>
    public string DefaultLocale { get; set; } = "en";

    /// <summary>Gets or sets the selectable locales in display order.</summary>
    public List<LocaleInfo> Locales { get; set; } = [];

    /// <summary>Builds the manifest from a parsed <c>locales.ini</c>.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="sourceName">Label used in error messages.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="IniFormatException">A locale section is incomplete or a font entry is malformed.</exception>
    public static LocaleManifest FromIni(IniDocument document, string sourceName = "locales.ini")
    {
        ArgumentNullException.ThrowIfNull(document);
        LocaleManifest manifest = new();
        if (document.TryGetValue("general.default", out string defaultLocale) && defaultLocale.Length > 0)
        {
            manifest.DefaultLocale = defaultLocale;
        }

        foreach (string section in document.Sections)
        {
            if (!section.StartsWith(LocaleSectionPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            LocaleInfo locale = new() { Code = section[LocaleSectionPrefix.Length..] };
            foreach (IniEntry entry in document.InSection(section))
            {
                switch (entry.Key)
                {
                    case "name":
                        locale.NativeName = entry.Value;
                        break;
                    case "fonts":
                        locale.Fonts = ParseFonts(entry, sourceName);
                        break;
                    default:
                        throw new IniFormatException(sourceName, entry.Line, $"unknown locale setting '{entry.Key}' (expected name, fonts)");
                }
            }

            if (locale.NativeName.Length == 0 || locale.Fonts.Count == 0)
            {
                throw new IniFormatException(sourceName, 0, $"[{section}] needs both 'name' and 'fonts'");
            }

            manifest.Locales.Add(locale);
        }

        return manifest;
    }

    private static List<LocaleFontFace> ParseFonts(IniEntry entry, string sourceName)
    {
        List<LocaleFontFace> fonts = [];
        foreach (string part in entry.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int at = part.LastIndexOf('@');
            if (at <= 0 || !float.TryParse(part[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float size) || size <= 0f)
            {
                throw new IniFormatException(sourceName, entry.Line, $"font '{part}' must be 'file@pixelSize'");
            }

            fonts.Add(new LocaleFontFace { File = part[..at].Trim(), PixelSize = size });
        }

        return fonts;
    }
}
