// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Resolves localization keys to player-facing text for the active language (ADR-020 D3).
/// Missing keys fall back to <c>en</c>, then to the key itself, and are reported once per language.
/// </summary>
public interface ILocalizer
{
    /// <summary>Gets the active locale code (e.g. <c>en</c>, <c>vi</c>).</summary>
    string Language { get; }

    /// <summary>Gets the active locale entry (code, native name and font stack).</summary>
    LocaleInfo Locale { get; }

    /// <summary>Gets the selectable locales in display order.</summary>
    IReadOnlyList<LocaleInfo> Locales { get; }

    /// <summary>
    /// Gets a counter that increases every time the active language changes. Callers that cache formatted text
    /// re-format when it differs from the value they cached with.
    /// </summary>
    int Version { get; }

    /// <summary>Raised after the active language changed.</summary>
    event Action? LanguageChanged;

    /// <summary>Returns the text of <paramref name="key"/>; allocation-free (returns the string stored at load).</summary>
    /// <param name="key">Localization key, e.g. <c>hud.not_enough_mana</c>.</param>
    /// <returns>The localized text, the English fallback, or the key itself.</returns>
    string Text(string key);

    /// <summary>Returns whether <paramref name="key"/> exists in the active language or the English fallback.</summary>
    /// <param name="key">Localization key.</param>
    /// <param name="text">The resolved text (placeholders unformatted).</param>
    /// <returns><see langword="true"/> when the key exists.</returns>
    bool TryGet(string key, out string text);

    /// <summary>Formats <paramref name="key"/> with named <paramref name="args"/> (allocates one string).</summary>
    /// <param name="key">Localization key.</param>
    /// <param name="args">Named placeholder arguments.</param>
    /// <returns>The formatted text.</returns>
    string Format(string key, params ReadOnlySpan<TextArg> args);

    /// <summary>
    /// Selects <c>&lt;key&gt;.one</c> or <c>&lt;key&gt;.other</c> for <paramref name="count"/> using the language's plural rule,
    /// and formats it with <c>{count}</c> plus <paramref name="args"/>.
    /// </summary>
    /// <param name="key">Base key without the plural suffix.</param>
    /// <param name="count">The counted quantity, also bound to <c>{count}</c>.</param>
    /// <param name="args">Additional named arguments.</param>
    /// <returns>The formatted text.</returns>
    string Plural(string key, long count, params ReadOnlySpan<TextArg> args);

    /// <summary>Switches the active language.</summary>
    /// <param name="code">Locale code from <see cref="Locales"/>.</param>
    /// <returns><see langword="false"/> when <paramref name="code"/> is unknown (the language is unchanged).</returns>
    bool SetLanguage(string code);
}
