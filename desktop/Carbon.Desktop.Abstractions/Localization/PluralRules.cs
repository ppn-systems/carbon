// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// CLDR plural category selection for the supported languages (ADR-020 D2).
/// A plural string is stored as <c>&lt;key&gt;.one</c> and <c>&lt;key&gt;.other</c>.
/// </summary>
public static class PluralRules
{
    /// <summary>Category suffix for the singular form.</summary>
    public const string One = "one";

    /// <summary>Category suffix for the general form.</summary>
    public const string Other = "other";

    /// <summary>
    /// Returns the plural category of <paramref name="count"/> in <paramref name="language"/>.
    /// Vietnamese has no grammatical plural and always selects <see cref="Other"/>; English (and unknown languages)
    /// select <see cref="One"/> for exactly 1.
    /// </summary>
    /// <param name="language">Locale code such as <c>en</c> or <c>vi</c>.</param>
    /// <param name="count">The counted quantity.</param>
    /// <returns><see cref="One"/> or <see cref="Other"/>.</returns>
    public static string Select(string language, long count)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (string.Equals(language, "vi", StringComparison.Ordinal))
        {
            return Other;
        }

        return count == 1 ? One : Other;
    }
}
