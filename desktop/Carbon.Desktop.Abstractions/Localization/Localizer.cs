// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Default <see cref="ILocalizer"/> over a <see cref="LocaleCatalog"/>. Every value is precompiled into a
/// <see cref="TextTemplate"/> when its language is activated, so lookups and placeholder-free values never allocate.
/// </summary>
public sealed class Localizer : ILocalizer
{
    private readonly LocaleCatalog _catalog;
    private readonly Dictionary<string, TextTemplate> _fallback;
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextTemplate> _missingKeys = new(StringComparer.Ordinal);
    private readonly Action<string> _reportMissing;
    private Dictionary<string, TextTemplate> _active;
    private LocaleInfo _locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="Localizer"/> class with the catalog's default locale active.
    /// </summary>
    /// <param name="catalog">Loaded locale files.</param>
    /// <param name="reportMissing">Receives one message per missing key and language (defaults to standard error).</param>
    public Localizer(LocaleCatalog catalog, Action<string>? reportMissing = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _reportMissing = reportMissing ?? (static message => Console.Error.WriteLine(message));
        _fallback = Compile(catalog.GetTable(LocaleCatalog.FallbackLocale));
        _locale = this.FindLocale(catalog.Manifest.DefaultLocale) ?? this.FindLocale(LocaleCatalog.FallbackLocale) ?? new LocaleInfo { Code = LocaleCatalog.FallbackLocale, NativeName = "English" };
        _active = string.Equals(_locale.Code, LocaleCatalog.FallbackLocale, StringComparison.Ordinal) ? _fallback : Compile(catalog.GetTable(_locale.Code));
    }

    /// <inheritdoc/>
    public event Action? LanguageChanged;

    /// <inheritdoc/>
    public string Language => _locale.Code;

    /// <inheritdoc/>
    public LocaleInfo Locale => _locale;

    /// <inheritdoc/>
    public IReadOnlyList<LocaleInfo> Locales => _catalog.Manifest.Locales;

    /// <inheritdoc/>
    public int Version { get; private set; }

    /// <inheritdoc/>
    public bool SetLanguage(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        LocaleInfo? locale = this.FindLocale(code.Trim().ToLowerInvariant());
        if (locale is null)
        {
            return false;
        }

        if (string.Equals(locale.Code, _locale.Code, StringComparison.Ordinal))
        {
            return true;
        }

        _locale = locale;
        _reportedMissing.Clear();
        _active = string.Equals(locale.Code, LocaleCatalog.FallbackLocale, StringComparison.Ordinal) ? _fallback : Compile(_catalog.GetTable(locale.Code));
        this.Version++;
        this.LanguageChanged?.Invoke();
        return true;
    }

    /// <inheritdoc/>
    public string Text(string key)
    {
        TextTemplate template = this.Resolve(key);
        return template.LiteralText ?? template.Source;
    }

    /// <inheritdoc/>
    public bool TryGet(string key, out string text)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_active.TryGetValue(key, out TextTemplate? template) || _fallback.TryGetValue(key, out template))
        {
            text = template.LiteralText ?? template.Source;
            return true;
        }

        text = key;
        return false;
    }

    /// <inheritdoc/>
    public string Format(string key, params ReadOnlySpan<TextArg> args) => this.Resolve(key).Format(args);

    /// <inheritdoc/>
    public string Plural(string key, long count, params ReadOnlySpan<TextArg> args)
    {
        ArgumentNullException.ThrowIfNull(key);
        string category = PluralRules.Select(_locale.Code, count);
        string formKey = string.Concat(key, ".", category);
        TextTemplate template = this.TryResolve(formKey) ?? this.Resolve(string.Concat(key, ".", PluralRules.Other));

        TextArg countArg = new("count", count);
        if (args.Length == 0)
        {
            return template.Format(new ReadOnlySpan<TextArg>(in countArg));
        }

        TextArg[] all = new TextArg[args.Length + 1];
        all[0] = countArg;
        args.CopyTo(all.AsSpan(1));
        return template.Format(all);
    }

    private TextTemplate Resolve(string key) => this.TryResolve(key) ?? this.Missing(key);

    private TextTemplate? TryResolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_active.TryGetValue(key, out TextTemplate? template))
        {
            return template;
        }

        if (!ReferenceEquals(_active, _fallback) && _fallback.TryGetValue(key, out template))
        {
            if (_reportedMissing.Add(key))
            {
                _reportMissing($"[i18n] Missing '{key}' in '{_locale.Code}', using '{LocaleCatalog.FallbackLocale}'.");
            }

            return template;
        }

        return null;
    }

    private TextTemplate Missing(string key)
    {
        if (_reportedMissing.Add(key))
        {
            _reportMissing($"[i18n] Missing key '{key}' in '{_locale.Code}' and '{LocaleCatalog.FallbackLocale}'.");
        }

        if (!_missingKeys.TryGetValue(key, out TextTemplate? literal))
        {
            literal = TextTemplate.Literal(key);
            _missingKeys[key] = literal;
        }

        return literal;
    }

    private LocaleInfo? FindLocale(string code)
    {
        List<LocaleInfo> locales = _catalog.Manifest.Locales;
        for (int i = 0; i < locales.Count; i++)
        {
            if (string.Equals(locales[i].Code, code, StringComparison.Ordinal))
            {
                return locales[i];
            }
        }

        return null;
    }

    private static Dictionary<string, TextTemplate> Compile(IReadOnlyDictionary<string, string> table)
    {
        Dictionary<string, TextTemplate> compiled = new(table.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in table)
        {
            compiled[pair.Key] = TextTemplate.TryParse(pair.Value, out TextTemplate template, out _) ? template : TextTemplate.Literal(pair.Value);
        }

        return compiled;
    }
}
