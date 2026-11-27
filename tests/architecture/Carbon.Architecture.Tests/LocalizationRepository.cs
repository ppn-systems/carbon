// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carbon.Architecture.Tests;

/// <summary>A string literal found in production C# source.</summary>
/// <param name="Path">Repo-relative file path.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Text">Literal value (interpolations rendered as <c>{}</c>).</param>
/// <param name="Node">The literal or interpolated-string syntax node.</param>
internal sealed record SourceLiteral(string Path, int Line, string Text, SyntaxNode Node);

/// <summary>Reads <c>resources/localization</c> (INI) and scans production code for localization keys (ADR-023 D7).</summary>
internal static partial class LocalizationRepository
{
    /// <summary>Key namespaces reserved for client text even before a key exists in <c>en.ini</c>.</summary>
    private static readonly string[] s_reservedNamespaces =
    [
        "biome", "building", "character", "crafting", "error", "forge", "hud", "inventory", "item", "login",
        "menu", "monster", "notice", "npc", "settings", "skill", "stat", "structure", "toast", "tooltip", "ui", "vendor",
    ];

    /// <summary>Trailing segments that make a dotted literal a file name rather than a key.</summary>
    private static readonly HashSet<string> s_fileExtensions = new(StringComparer.Ordinal)
    {
        "json", "ini", "png", "ttf", "cfg", "txt", "bmp", "md", "cs", "glsl", "frag", "vert", "csproj", "slnx",
    };

    private static readonly Lazy<LocaleManifest> s_manifest = new(LoadManifest);
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> s_tables = new(LoadTables);
    private static readonly Lazy<IReadOnlyList<SourceLiteral>> s_codeLiterals = new(ScanCodeLiterals);

    public static string LocalizationDirectory => Path.Combine(RepositoryLayout.Root, "resources", "localization");

    public static string FontsDirectory => Path.Combine(RepositoryLayout.Root, "resources", "fonts");

    public static LocaleManifest Manifest => s_manifest.Value;

    /// <summary>Locale code → key → value.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables => s_tables.Value;

    public static IReadOnlyDictionary<string, string> English => Tables[LocaleCatalog.FallbackLocale];

    /// <summary>Every string literal in desktop/, graphics/ and server/ production code.</summary>
    public static IReadOnlyList<SourceLiteral> CodeLiterals => s_codeLiterals.Value;

    public static bool IsWellFormedKey(string key) => KeyPattern().IsMatch(key);

    /// <summary>Returns whether a code literal looks like a localization key that must exist in <c>en.ini</c>.</summary>
    public static bool LooksLikeKeyReference(string text)
    {
        if (!IsWellFormedKey(text))
        {
            return false;
        }

        string[] segments = text.Split('.');
        if (s_fileExtensions.Contains(segments[^1]))
        {
            return false;
        }

        string ns = segments[0];
        return s_reservedNamespaces.Contains(ns, StringComparer.Ordinal) || English.Keys.Any(k => k.StartsWith(ns + ".", StringComparison.Ordinal));
    }

    /// <summary>Key literals referenced from production code, with their locations.</summary>
    public static IReadOnlyList<SourceLiteral> KeyReferences() => [.. CodeLiterals.Where(l => LooksLikeKeyReference(l.Text))];

    /// <summary>Keys derived from stable config ids and <c>BiomeType</c> names (ADR-023 D4).</summary>
    public static IReadOnlySet<string> DerivedKeys()
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        string config = Path.Combine(RepositoryLayout.Root, "resources", "config");

        using (JsonDocument items = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "items.json"))))
        {
            foreach (JsonElement item in items.RootElement.GetProperty("items").EnumerateArray())
            {
                string code = item.GetProperty("code").GetString()!;
                keys.Add($"item.{code}.name");
                if (item.TryGetProperty("actionHint", out JsonElement hint) && hint.GetString() is { Length: > 0 })
                {
                    keys.Add($"item.{code}.hint");
                }
            }
        }

        using (JsonDocument monsters = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "monsters.json"))))
        {
            foreach (JsonElement monster in monsters.RootElement.GetProperty("monsters").EnumerateArray())
            {
                keys.Add($"monster.{monster.GetProperty("id").GetString()}.name");
            }
        }

        foreach (Carbon.Maps.Biomes.BiomeType biome in Enum.GetValues<Carbon.Maps.Biomes.BiomeType>())
        {
            keys.Add(GameText.BiomeKey(biome));
        }

        return keys;
    }

    /// <summary>Returns the base key of a plural form (<c>a.b.one</c> → <c>a.b</c>), or <see langword="null"/>.</summary>
    public static string? PluralBase(string key)
    {
        if (key.EndsWith("." + PluralRules.One, StringComparison.Ordinal))
        {
            return key[..^(PluralRules.One.Length + 1)];
        }

        return key.EndsWith("." + PluralRules.Other, StringComparison.Ordinal) ? key[..^(PluralRules.Other.Length + 1)] : null;
    }

    /// <summary>Parses every C# file under <paramref name="relativeDirectories"/> (excluding build output).</summary>
    public static IEnumerable<(string Path, SyntaxTree Tree)> ParseSources(params string[] relativeDirectories)
    {
        foreach (string relative in relativeDirectories)
        {
            string absolute = Path.Combine(RepositoryLayout.Root, relative);
            if (!Directory.Exists(absolute))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(absolute, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string repoPath = RepositoryLayout.Relative(file);
                if (repoPath.Contains("/obj/", StringComparison.Ordinal) || repoPath.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (repoPath, CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: repoPath));
            }
        }
    }

    /// <summary>Extracts string literals and interpolated strings from a syntax tree.</summary>
    public static IEnumerable<SourceLiteral> Literals(string path, SyntaxTree tree)
    {
        SyntaxNode root = tree.GetRoot();
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                yield return new SourceLiteral(path, LineOf(node), literal.Token.ValueText, node);
            }
            else if (node is InterpolatedStringExpressionSyntax interpolated)
            {
                string text = string.Concat(interpolated.Contents.Select(static c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{}"));
                yield return new SourceLiteral(path, LineOf(node), text, node);
            }
        }
    }

    private static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static IReadOnlyList<SourceLiteral> ScanCodeLiterals() =>
        [.. ParseSources("desktop", "graphics", "server").SelectMany(static s => Literals(s.Path, s.Tree))];

    private static LocaleManifest LoadManifest() =>
        LocaleManifest.FromIni(IniDocument.Load(Path.Combine(LocalizationDirectory, LocaleCatalog.ManifestFileName)));

    /// <summary>Loads each locale table with the production INI parser, which rejects duplicates and malformed lines.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadTables()
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> tables = new(StringComparer.Ordinal);
        foreach (LocaleInfo locale in Manifest.Locales)
        {
            tables[locale.Code] = LocaleCatalog.LoadTable(Path.Combine(LocalizationDirectory, locale.Code + LocaleCatalog.TableExtension));
        }

        return tables;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")]
    private static partial Regex KeyPattern();
}
