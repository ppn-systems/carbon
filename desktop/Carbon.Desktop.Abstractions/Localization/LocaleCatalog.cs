// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Loads <c>resources/localization</c>: the locale manifest and each locale's flat key → string table (ADR-020 D1).
/// </summary>
public sealed class LocaleCatalog
{
    /// <summary>The fallback locale code; its table is the source of truth.</summary>
    public const string FallbackLocale = "en";

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables;

    private LocaleCatalog(LocaleManifest manifest, Dictionary<string, IReadOnlyDictionary<string, string>> tables)
    {
        this.Manifest = manifest;
        _tables = tables;
    }

    /// <summary>Gets the parsed locale manifest.</summary>
    public LocaleManifest Manifest { get; }

    /// <summary>
    /// Creates a catalog from in-memory tables (tests and tools).
    /// </summary>
    /// <param name="manifest">The locale manifest.</param>
    /// <param name="tables">Locale code → key → text.</param>
    /// <returns>The catalog.</returns>
    public static LocaleCatalog FromTables(LocaleManifest manifest, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tables);
        Dictionary<string, IReadOnlyDictionary<string, string>> copy = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>> pair in tables)
        {
            copy[pair.Key] = pair.Value;
        }

        return new LocaleCatalog(manifest, copy);
    }

    /// <summary>
    /// Loads the manifest and every listed locale table from <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The <c>resources/localization</c> directory.</param>
    /// <returns>The catalog.</returns>
    public static LocaleCatalog Load(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory, nameof(directory));
        LocaleManifest manifest = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(directory, "locales.json")),
            LocalizationJsonContext.Default.LocaleManifest)
            ?? throw new InvalidDataException("locales.json is empty.");

        Dictionary<string, IReadOnlyDictionary<string, string>> tables = new(StringComparer.Ordinal);
        foreach (LocaleInfo locale in manifest.Locales)
        {
            tables[locale.Code] = LoadTable(Path.Combine(directory, locale.Code + ".json"));
        }

        return new LocaleCatalog(manifest, tables);
    }

    /// <summary>Parses one flat locale file.</summary>
    /// <param name="path">Path of <c>&lt;code&gt;.json</c>.</param>
    /// <returns>Key → text.</returns>
    public static Dictionary<string, string> LoadTable(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));
        Dictionary<string, string>? table = JsonSerializer.Deserialize(File.ReadAllText(path), LocalizationJsonContext.Default.DictionaryStringString);
        return table ?? [];
    }

    /// <summary>
    /// Searches the runtime base directory, the working directory and their parents for <c>resources/localization</c>.
    /// </summary>
    /// <returns>The directory path, or <see langword="null"/> when not found.</returns>
    public static string? TryFindDirectory()
    {
        string[] roots = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        for (int i = 0; i < roots.Length; i++)
        {
            for (DirectoryInfo? cursor = new(roots[i]); cursor is not null; cursor = cursor.Parent)
            {
                string candidate = Path.Combine(cursor.FullName, "resources", "localization");
                if (File.Exists(Path.Combine(candidate, "locales.json")))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the table of <paramref name="code"/>, or an empty table when the locale is unknown.</summary>
    /// <param name="code">Locale code.</param>
    /// <returns>Key → text.</returns>
    public IReadOnlyDictionary<string, string> GetTable(string code) =>
        _tables.TryGetValue(code, out IReadOnlyDictionary<string, string>? table) ? table : s_empty;

    private static readonly IReadOnlyDictionary<string, string> s_empty = new Dictionary<string, string>(StringComparer.Ordinal);
}
