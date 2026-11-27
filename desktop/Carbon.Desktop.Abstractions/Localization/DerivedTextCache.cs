// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Per-widget cache of keys derived from stable ids (e.g. <c>skill.&lt;code&gt;.name</c>, ADR-023 D4), so a label drawn
/// every frame builds its key string once. The text itself is looked up each call, so a language switch applies at once.
/// </summary>
public sealed class DerivedTextCache
{
    private readonly string _prefix;
    private readonly string _suffix;
    private readonly ILocalizer? _localizer;
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="DerivedTextCache"/> class.</summary>
    /// <param name="prefix">Key prefix including the trailing dot, e.g. <c>skill.</c>.</param>
    /// <param name="suffix">Key suffix including the leading dot, e.g. <c>.name</c>.</param>
    /// <param name="localizer">The localizer to use; <see langword="null"/> uses <see cref="GameText.Current"/>.</param>
    public DerivedTextCache(string prefix, string suffix, ILocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        _prefix = prefix;
        _suffix = suffix;
        _localizer = localizer;
    }

    /// <summary>Returns the localized text for <paramref name="id"/>, or <paramref name="fallback"/> when no locale has the key.</summary>
    /// <param name="id">Stable id, e.g. a skill code.</param>
    /// <param name="fallback">Text used when the key is missing (typically the catalog's English name).</param>
    /// <returns>The text.</returns>
    public string Text(string id, string fallback)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_keys.TryGetValue(id, out string? key))
        {
            key = string.Concat(_prefix, id, _suffix);
            _keys[id] = key;
        }

        return (_localizer ?? GameText.Current).TryGet(key, out string text) ? text : fallback;
    }
}
