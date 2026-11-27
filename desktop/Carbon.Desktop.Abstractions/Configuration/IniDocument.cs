// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// Minimal hand-written INI reader/writer for client data files (locale tables, user settings; ADR-023 D1).
/// No reflection, no packages, Native-AOT safe.
/// </summary>
/// <remarks>
/// Rules: UTF-8 (a BOM is rejected); <c>;</c> or <c>#</c> starts a comment line; <c>[section]</c> namespaces the keys
/// that follow (<c>[hud]</c> + <c>not_enough_mana</c> → <c>hud.not_enough_mana</c>); whitespace around <c>=</c> is trimmed;
/// values support <c>\n</c>, <c>\t</c>, <c>\\</c> and a trailing <c>\</c> for line continuation; section and key names match
/// <c>[a-z0-9_.]+</c>; duplicate sections, duplicate keys and malformed lines are load errors with file and line.
/// </remarks>
public sealed class IniDocument
{
    private readonly List<IniEntry> _entries;
    private readonly Dictionary<string, IniEntry> _byKey;

    private IniDocument(List<IniEntry> entries, Dictionary<string, IniEntry> byKey, List<string> sections)
    {
        _entries = entries;
        _byKey = byKey;
        this.Sections = sections;
    }

    /// <summary>Gets the entries in file order.</summary>
    public IReadOnlyList<IniEntry> Entries => _entries;

    /// <summary>Gets the section names in file order.</summary>
    public IReadOnlyList<string> Sections { get; }

    /// <summary>Parses the INI file at <paramref name="path"/>.</summary>
    /// <param name="path">File path; its name is used in error messages.</param>
    /// <returns>The document.</returns>
    /// <exception cref="IniFormatException">The file is not valid INI.</exception>
    public static IniDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));
        byte[] bytes = File.ReadAllBytes(path);
        string name = Path.GetFileName(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new IniFormatException(name, 1, "UTF-8 byte order mark is not allowed");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new IniFormatException($"{name}: file is not valid UTF-8", ex);
        }

        return Parse(text, name);
    }

    /// <summary>Parses INI <paramref name="text"/>.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="sourceName">Label used in error messages (usually the file name).</param>
    /// <returns>The document.</returns>
    /// <exception cref="IniFormatException">The text is not valid INI.</exception>
    public static IniDocument Parse(string text, string sourceName = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceName);

        List<IniEntry> entries = [];
        Dictionary<string, IniEntry> byKey = new(StringComparer.Ordinal);
        List<string> sections = [];
        HashSet<string> seenSections = new(StringComparer.Ordinal);
        string section = string.Empty;

        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = lines[i].TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (line[^1] != ']')
                {
                    throw new IniFormatException(sourceName, lineNumber, "section header must end with ']'");
                }

                section = line[1..^1].Trim();
                if (!IsValidName(section))
                {
                    throw new IniFormatException(sourceName, lineNumber, $"invalid section name '{section}' (allowed: a-z 0-9 _ .)");
                }

                if (!seenSections.Add(section))
                {
                    throw new IniFormatException(sourceName, lineNumber, $"duplicate section [{section}]");
                }

                sections.Add(section);
                continue;
            }

            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                throw new IniFormatException(sourceName, lineNumber, "expected 'key = value', '[section]' or a comment");
            }

            string key = line[..eq].Trim();
            if (!IsValidName(key))
            {
                throw new IniFormatException(sourceName, lineNumber, $"invalid key '{key}' (allowed: a-z 0-9 _ .)");
            }

            StringBuilder raw = new(line[(eq + 1)..].Trim());
            while (EndsWithContinuation(raw))
            {
                raw.Length--;
                if (i + 1 >= lines.Length)
                {
                    throw new IniFormatException(sourceName, lineNumber, "line continuation '\\' at end of file");
                }

                i++;
                _ = raw.Append(lines[i].TrimEnd('\r').Trim());
            }

            string value = Unescape(raw.ToString(), sourceName, lineNumber);
            string fullKey = section.Length == 0 ? key : string.Concat(section, ".", key);
            IniEntry entry = new(section, key, fullKey, value, lineNumber);
            if (!byKey.TryAdd(fullKey, entry))
            {
                throw new IniFormatException(sourceName, lineNumber, $"duplicate key '{fullKey}' (first defined on line {byKey[fullKey].Line})");
            }

            entries.Add(entry);
        }

        return new IniDocument(entries, byKey, sections);
    }

    /// <summary>Returns the value of <paramref name="fullKey"/> (<c>section.key</c>).</summary>
    /// <param name="fullKey">Section-qualified key.</param>
    /// <param name="value">The unescaped value.</param>
    /// <returns><see langword="true"/> when present.</returns>
    public bool TryGetValue(string fullKey, out string value)
    {
        ArgumentNullException.ThrowIfNull(fullKey);
        if (_byKey.TryGetValue(fullKey, out IniEntry? entry))
        {
            value = entry.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Returns the entries of <paramref name="section"/> in file order.</summary>
    /// <param name="section">Section name.</param>
    /// <returns>The entries.</returns>
    public IEnumerable<IniEntry> InSection(string section)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Section, section, StringComparison.Ordinal))
            {
                yield return _entries[i];
            }
        }
    }

    /// <summary>Returns a flat <c>section.key → value</c> map.</summary>
    /// <returns>A new dictionary.</returns>
    public Dictionary<string, string> ToDictionary()
    {
        Dictionary<string, string> map = new(_entries.Count, StringComparer.Ordinal);
        for (int i = 0; i < _entries.Count; i++)
        {
            map[_entries[i].FullKey] = _entries[i].Value;
        }

        return map;
    }

    /// <summary>Escapes <paramref name="value"/> for writing (<c>\</c>, newline, tab).</summary>
    /// <param name="value">Raw value.</param>
    /// <returns>The INI-encoded value.</returns>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Returns whether <paramref name="name"/> is a valid section or key name (<c>[a-z0-9_.]+</c>).</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_' && c != '.')
            {
                return false;
            }
        }

        return true;
    }

    private static bool EndsWithContinuation(StringBuilder raw)
    {
        int backslashes = 0;
        for (int i = raw.Length - 1; i >= 0 && raw[i] == '\\'; i--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    private static string Unescape(string raw, string sourceName, int lineNumber)
    {
        if (raw.IndexOf('\\', StringComparison.Ordinal) < 0)
        {
            return raw;
        }

        StringBuilder builder = new(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c != '\\')
            {
                _ = builder.Append(c);
                continue;
            }

            if (i + 1 >= raw.Length)
            {
                throw new IniFormatException(sourceName, lineNumber, "dangling '\\' escape");
            }

            char next = raw[++i];
            _ = next switch
            {
                'n' => builder.Append('\n'),
                't' => builder.Append('\t'),
                '\\' => builder.Append('\\'),
                _ => throw new IniFormatException(sourceName, lineNumber, $"unknown escape '\\{next}' (allowed: \\n \\t \\\\)"),
            };
        }

        return builder.ToString();
    }
}
