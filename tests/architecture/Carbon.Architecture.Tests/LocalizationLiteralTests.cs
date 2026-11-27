// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Fails on new hardcoded player-facing string literals in desktop and graphics UI code (ADR-023 D7).
/// Existing literals are listed in <c>LocalizationLiteralAllowlist.txt</c>; that list can only shrink:
/// a listed literal that no longer exists also fails, so delete its line when you migrate it to a key.
/// </summary>
public sealed partial class LocalizationLiteralTests
{
    /// <summary>Directories scanned for UI literals.</summary>
    private static readonly string[] s_scannedDirectories = ["desktop", "graphics/Carbon.Graphics.UI"];

    /// <summary>Developer tooling and the localization infrastructure itself (diagnostics only).</summary>
    private static readonly string[] s_excludedPrefixes = ["desktop/Carbon.Desktop/Capture/", "desktop/Carbon.Desktop.Abstractions/Localization/"];

    /// <summary>Invocations whose string arguments are diagnostics, comparisons or identifiers, not player text.</summary>
    private static readonly HashSet<string> s_nonUiMethods = new(StringComparer.Ordinal)
    {
        "WriteLine", "Write", "Assert", "Fail", "Fatal", "Error", "Warn", "Warning", "Info", "Debug", "Trace",
        "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical",
        "Equals", "StartsWith", "EndsWith", "Contains", "IndexOf", "LastIndexOf", "Replace", "Split", "Trim",
        "GetEnvironmentVariable", "Combine", "GetProperty", "TryGetProperty", "Parse", "TryParse",
        "GetFiles", "EnumerateFiles", "GetDirectories", "Exists", "ReadAllText", "ReadAllBytes", "Open",
        "ThrowIfNullOrEmpty", "ThrowIfNullOrWhiteSpace", "GetUniformLocation", "GetAttribLocation",
    };

    private static readonly HashSet<string> s_nonUiReceivers = new(StringComparer.Ordinal)
    {
        "Console", "Debug", "Trace", "Path", "File", "Directory", "Environment", "Logger", "_logger", "logger", "Error",
    };

    [Fact]
    public void UiCode_HasNoNewHardcodedPlayerFacingLiterals()
    {
        HashSet<string> allowlist = LoadAllowlist(out List<string> malformed);
        malformed.Should().BeEmpty("allowlist lines must be '<repo path><TAB><literal>'");

        Dictionary<string, SourceLiteral> found = ScanUiLiterals();

        string[] added = [.. found
            .Where(pair => !allowlist.Contains(pair.Key))
            .Select(pair => $"{pair.Value.Path}:{pair.Value.Line} \"{pair.Value.Text}\"")];

        string[] stale = [.. allowlist.Where(entry => !found.ContainsKey(entry)).Order(StringComparer.Ordinal)];

        added.Should().BeEmpty(
            "player-facing text must come from resources/localization (GameText.Get/Format/Plural, see resources/localization/README.md); " +
            "do not add new lines to LocalizationLiteralAllowlist.txt");
        stale.Should().BeEmpty(
            "these allowlisted literals no longer exist; delete their lines from LocalizationLiteralAllowlist.txt (the list only shrinks)");
    }

    [Theory]
    [InlineData("Not enough Mana!", true)]
    [InlineData("RESUME", true)]
    [InlineData("Settings", true)]
    [InlineData("LEVEL UP! Lv.{}", true)]
    [InlineData("hud.not_enough_mana", false)]
    [InlineData("resources/fonts", false)]
    [InlineData("wood_axe", false)]
    [InlineData("#FF8800", false)]
    [InlineData("; settings file comment", false)]
    [InlineData("x", false)]
    [InlineData("{}/{}", false)]
    [InlineData("+{} {} Forged!", true)]
    [InlineData("+{} XP", true)]
    [InlineData("{}%", false)]
    [InlineData("{} x{}", false)]
    public void LooksPlayerFacing_ClassifiesLiterals(string text, bool expected) =>
        LooksPlayerFacing(text).Should().Be(expected);

    /// <summary>Returns whether <paramref name="text"/> reads like words shown to a player.</summary>
    internal static bool LooksPlayerFacing(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim();
        if (trimmed.Length < 2 || !LetterPair().IsMatch(trimmed))
        {
            return false;
        }

        // Keys, hex colors and INI comment lines written to config files are not player text.
        if (LocalizationRepository.IsWellFormedKey(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
        {
            return false;
        }

        if (!trimmed.Contains(' ', StringComparison.Ordinal) && (trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Contains('_', StringComparison.Ordinal)))
        {
            return false;
        }

        if (WordsWithSpace().IsMatch(trimmed) || CapitalizedWord().IsMatch(trimmed))
        {
            return true;
        }

        // Interpolated text such as $"+{n} {name} Forged!" or $"+{xp} XP": judge the words around the holes.
        string core = trimmed.Replace("{}", " ", StringComparison.Ordinal).Trim(' ', '+', '-', '(', ')', '[', ']', '/', ':', '%', '.', ',', '|', 'x');
        return core.Length >= 2 && core != trimmed && (WordsWithSpace().IsMatch(core) || CapitalizedWord().IsMatch(core) || AllCapsWord().IsMatch(core));
    }

    private static Dictionary<string, SourceLiteral> ScanUiLiterals()
    {
        Dictionary<string, SourceLiteral> found = new(StringComparer.Ordinal);
        foreach ((string path, SyntaxTree tree) in LocalizationRepository.ParseSources(s_scannedDirectories))
        {
            if (s_excludedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (SourceLiteral literal in LocalizationRepository.Literals(path, tree))
            {
                if (!LooksPlayerFacing(literal.Text) || IsNonUiContext(literal.Node))
                {
                    continue;
                }

                found.TryAdd(EntryOf(literal.Path, literal.Text), literal);
            }
        }

        return found;
    }

    private static bool IsNonUiContext(SyntaxNode node)
    {
        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AttributeArgumentSyntax:
                case ThrowStatementSyntax:
                case ThrowExpressionSyntax:
                case SwitchLabelSyntax:
                case ConstantPatternSyntax:
                case SwitchExpressionArmSyntax arm when arm.Pattern.Span.Contains(node.Span):
                    return true;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                    return true;
                case ObjectCreationExpressionSyntax creation when creation.Type.ToString().EndsWith("Exception", StringComparison.Ordinal):
                    return true;
                case ConstructorInitializerSyntax when node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText.EndsWith("Exception", StringComparison.Ordinal) == true:
                    return true;
                case InvocationExpressionSyntax invocation when IsNonUiInvocation(invocation):
                    return true;
                case MemberDeclarationSyntax or LocalFunctionStatementSyntax:
                    return false;
                default:
                    break;
            }
        }

        return false;
    }

    private static bool IsNonUiInvocation(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case IdentifierNameSyntax identifier:
                return identifier.Identifier.ValueText == "nameof" || s_nonUiMethods.Contains(identifier.Identifier.ValueText);
            case MemberAccessExpressionSyntax member:
                string receiver = member.Expression is MemberAccessExpressionSyntax nested ? nested.Name.Identifier.ValueText : member.Expression.ToString();
                return s_nonUiMethods.Contains(member.Name.Identifier.ValueText) || s_nonUiReceivers.Contains(receiver);
            default:
                return false;
        }
    }

    private static string EntryOf(string path, string text) => path + "\t" + Escape(text);

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static HashSet<string> LoadAllowlist(out List<string> malformed)
    {
        malformed = [];
        HashSet<string> entries = new(StringComparer.Ordinal);
        string path = Path.Combine(RepositoryLayout.Root, "tests", "architecture", "Carbon.Architecture.Tests", "LocalizationLiteralAllowlist.txt");
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.IndexOf('\t', StringComparison.Ordinal) <= 0)
            {
                malformed.Add(line);
                continue;
            }

            entries.Add(line);
        }

        return entries;
    }

    [GeneratedRegex(@"\p{L}\p{L}")]
    private static partial Regex LetterPair();

    [GeneratedRegex(@"\p{L}{2,}[^\n]*\s+[^\n]*\p{L}|\p{L}\s+\p{L}{2,}")]
    private static partial Regex WordsWithSpace();

    [GeneratedRegex(@"^\p{Lu}[\p{L}&]*[!?.:]?$")]
    private static partial Regex CapitalizedWord();

    [GeneratedRegex(@"^\p{Lu}{2,}$")]
    private static partial Regex AllCapsWord();
}
