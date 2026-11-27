// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Caches one formatted localized string for per-frame UI (ADR-023 D3). <see cref="Format"/> returns the previous
/// string without allocating while the key, the arguments and <see cref="ILocalizer.Version"/> are unchanged, and
/// re-formats once when any of them changes. Keep one instance per on-screen label.
/// </summary>
public sealed class CachedText
{
    private readonly ILocalizer? _localizer;
    private TextArg[] _args = new TextArg[4];
    private int _argCount = -1;
    private string? _key;
    private int _version = -1;
    private long _pluralCount = long.MinValue;
    private string _value = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedText"/> class.
    /// </summary>
    /// <param name="localizer">The localizer to use; <see langword="null"/> uses <see cref="GameText.Current"/> at each call.</param>
    public CachedText(ILocalizer? localizer = null) => _localizer = localizer;

    /// <summary>Returns <paramref name="key"/> formatted with <paramref name="args"/>, reusing the cached string when nothing changed.</summary>
    /// <param name="key">Localization key.</param>
    /// <param name="args">Named arguments.</param>
    /// <returns>The formatted text.</returns>
    public string Format(string key, params ReadOnlySpan<TextArg> args)
    {
        ILocalizer localizer = _localizer ?? GameText.Current;
        if (this.IsCurrent(key, long.MinValue, localizer.Version, args))
        {
            return _value;
        }

        _value = localizer.Format(key, args);
        this.Remember(key, long.MinValue, localizer.Version, args);
        return _value;
    }

    /// <summary>Plural form of <see cref="Format"/> (<see cref="ILocalizer.Plural"/>), cached the same way.</summary>
    /// <param name="key">Base key without the plural suffix.</param>
    /// <param name="count">The counted quantity, also bound to <c>{count}</c>.</param>
    /// <param name="args">Additional named arguments.</param>
    /// <returns>The formatted text.</returns>
    public string Plural(string key, long count, params ReadOnlySpan<TextArg> args)
    {
        ILocalizer localizer = _localizer ?? GameText.Current;
        if (this.IsCurrent(key, count, localizer.Version, args))
        {
            return _value;
        }

        _value = localizer.Plural(key, count, args);
        this.Remember(key, count, localizer.Version, args);
        return _value;
    }

    private bool IsCurrent(string key, long pluralCount, int version, ReadOnlySpan<TextArg> args)
    {
        if (version != _version || pluralCount != _pluralCount || args.Length != _argCount || !string.Equals(key, _key, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].SameAs(in _args[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Remember(string key, long pluralCount, int version, ReadOnlySpan<TextArg> args)
    {
        if (_args.Length < args.Length)
        {
            _args = new TextArg[args.Length];
        }

        args.CopyTo(_args);
        _argCount = args.Length;
        _key = key;
        _version = version;
        _pluralCount = pluralCount;
    }
}
