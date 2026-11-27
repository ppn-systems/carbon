// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Carbon.Maps.Biomes;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Process-wide access to the client <see cref="ILocalizer"/> (ADR-020 D3), so widgets created with <c>new()</c>
/// can localize without constructor changes. The localizer loads <c>resources/localization</c> on first use;
/// the composition root picks the language with <see cref="Initialize"/>.
/// </summary>
public static class GameText
{
    /// <summary>Environment variable that forces the client language (e.g. <c>CARBON_LANG=vi</c>).</summary>
    public const string LanguageEnvironmentVariable = "CARBON_LANG";

    private static readonly Lock s_gate = new();
    private static readonly string[] s_biomeKeys = BuildBiomeKeys();
    private static readonly Dictionary<string, string> s_itemNameKeys = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> s_itemHintKeys = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> s_monsterNameKeys = new(StringComparer.Ordinal);
    private static ILocalizer? s_current;

    /// <summary>Gets or sets the active localizer (tests may replace it).</summary>
    public static ILocalizer Current
    {
        get
        {
            ILocalizer? current = s_current;
            if (current is not null)
            {
                return current;
            }

            lock (s_gate)
            {
                s_current ??= CreateDefault();
                return s_current;
            }
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            s_current = value;
        }
    }

    /// <summary>
    /// Selects the startup language: <c>CARBON_LANG</c> wins, then <paramref name="savedLanguage"/>, then the manifest default (<c>en</c>).
    /// Unknown codes are ignored.
    /// </summary>
    /// <param name="savedLanguage">The language saved in the player's settings, if any.</param>
    /// <param name="readEnv">Optional environment reader (defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>).</param>
    /// <returns>The active locale code.</returns>
    public static string Initialize(string? savedLanguage, Func<string, string?>? readEnv = null) =>
        SelectLanguage(Current, savedLanguage, readEnv);

    /// <summary>Applies the startup language precedence of <see cref="Initialize"/> to <paramref name="localizer"/>.</summary>
    /// <param name="localizer">The localizer to configure.</param>
    /// <param name="savedLanguage">The language saved in the player's settings, if any.</param>
    /// <param name="readEnv">Optional environment reader.</param>
    /// <returns>The active locale code.</returns>
    public static string SelectLanguage(ILocalizer localizer, string? savedLanguage, Func<string, string?>? readEnv = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        Func<string, string?> get = readEnv ?? Environment.GetEnvironmentVariable;
        if (get(LanguageEnvironmentVariable) is { Length: > 0 } forced && localizer.SetLanguage(forced))
        {
            return localizer.Language;
        }

        if (savedLanguage is { Length: > 0 })
        {
            _ = localizer.SetLanguage(savedLanguage);
        }

        return localizer.Language;
    }

    /// <inheritdoc cref="ILocalizer.Text(string)"/>
    public static string Get(string key) => Current.Text(key);

    /// <inheritdoc cref="ILocalizer.Format(string, ReadOnlySpan{TextArg})"/>
    public static string Format(string key, params ReadOnlySpan<TextArg> args) => Current.Format(key, args);

    /// <inheritdoc cref="ILocalizer.Plural(string, long, ReadOnlySpan{TextArg})"/>
    public static string Plural(string key, long count, params ReadOnlySpan<TextArg> args) => Current.Plural(key, count, args);

    /// <summary>Returns the localized name of an item (<c>item.&lt;code&gt;.name</c>), or <paramref name="fallbackName"/> when unknown.</summary>
    /// <param name="code">Stable item code from <c>items.json</c>.</param>
    /// <param name="fallbackName">The config <c>name</c> used when no locale has the key.</param>
    /// <returns>The display name.</returns>
    public static string ItemName(string code, string fallbackName) => Derived(s_itemNameKeys, "item.", code, ".name", fallbackName);

    /// <summary>Returns the localized usage hint of an item (<c>item.&lt;code&gt;.hint</c>), or <paramref name="fallbackHint"/> when unknown.</summary>
    /// <param name="code">Stable item code from <c>items.json</c>.</param>
    /// <param name="fallbackHint">The config <c>actionHint</c> used when no locale has the key.</param>
    /// <returns>The hint text.</returns>
    public static string ItemHint(string code, string fallbackHint) => Derived(s_itemHintKeys, "item.", code, ".hint", fallbackHint);

    /// <summary>Returns the localized name of a monster (<c>monster.&lt;id&gt;.name</c>), or <paramref name="fallbackName"/> when unknown.</summary>
    /// <param name="monsterId">Stable monster id from <c>monsters.json</c>.</param>
    /// <param name="fallbackName">The config <c>displayName</c> used when no locale has the key.</param>
    /// <returns>The display name.</returns>
    public static string MonsterName(string monsterId, string fallbackName) => Derived(s_monsterNameKeys, "monster.", monsterId, ".name", fallbackName);

    /// <summary>Returns the localized biome name (<c>biome.&lt;snake_case&gt;</c>).</summary>
    /// <param name="biome">The biome.</param>
    /// <returns>The display name.</returns>
    public static string BiomeName(BiomeType biome)
    {
        int index = (int)biome;
        return index >= 0 && index < s_biomeKeys.Length && s_biomeKeys[index].Length > 0
            ? Current.Text(s_biomeKeys[index])
            : biome.ToString();
    }

    /// <summary>Returns the localization key of <paramref name="biome"/> (<c>biome.lush_plains</c>).</summary>
    /// <param name="biome">The biome.</param>
    /// <returns>The key.</returns>
    public static string BiomeKey(BiomeType biome) => "biome." + ToSnakeCase(biome.ToString());

    /// <summary>Converts a PascalCase identifier to <c>snake_case</c>.</summary>
    /// <param name="pascal">PascalCase text such as <c>LushPlains</c>.</param>
    /// <returns><c>lush_plains</c>.</returns>
    public static string ToSnakeCase(string pascal)
    {
        ArgumentNullException.ThrowIfNull(pascal);
        StringBuilder builder = new(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    _ = builder.Append('_');
                }

                _ = builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Derived(Dictionary<string, string> keyCache, string prefix, string id, string suffix, string fallback)
    {
        ArgumentNullException.ThrowIfNull(id);
        string key;
        lock (keyCache)
        {
            if (!keyCache.TryGetValue(id, out key!))
            {
                key = string.Concat(prefix, id, suffix);
                keyCache[id] = key;
            }
        }

        return Current.TryGet(key, out string text) ? text : fallback;
    }

    private static string[] BuildBiomeKeys()
    {
        BiomeType[] values = Enum.GetValues<BiomeType>();
        int max = 0;
        for (int i = 0; i < values.Length; i++)
        {
            max = Math.Max(max, (int)values[i]);
        }

        string[] keys = new string[max + 1];
        Array.Fill(keys, string.Empty);
        for (int i = 0; i < values.Length; i++)
        {
            int index = (int)values[i];
            if (index >= 0)
            {
                keys[index] = BiomeKey(values[i]);
            }
        }

        return keys;
    }

    private static ILocalizer CreateDefault()
    {
        string? directory = LocaleCatalog.TryFindDirectory();
        if (directory is null)
        {
            Console.Error.WriteLine("[i18n] resources/localization not found; showing raw keys.");
            LocaleManifest manifest = new() { Locales = [new LocaleInfo { Code = LocaleCatalog.FallbackLocale, NativeName = "English" }] };
            return new Localizer(LocaleCatalog.FromTables(manifest, new Dictionary<string, IReadOnlyDictionary<string, string>>()));
        }

        return new Localizer(LocaleCatalog.Load(directory));
    }
}
