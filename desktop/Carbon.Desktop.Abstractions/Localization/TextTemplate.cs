// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// A localized value precompiled once into literal and <c>{name}</c> placeholder segments (ADR-023 D2).
/// <c>{{</c> and <c>}}</c> escape literal braces. Formatting never re-parses the source string.
/// </summary>
public sealed class TextTemplate
{
    [ThreadStatic]
    private static StringBuilder? s_builder;

    private readonly Segment[] _segments;

    private TextTemplate(string source, Segment[] segments, string[] placeholders)
    {
        this.Source = source;
        _segments = segments;
        this.Placeholders = placeholders;
        this.LiteralText = segments.Length == 0 ? string.Empty : segments.Length == 1 && segments[0].IsLiteral ? segments[0].Text : null;
    }

    /// <summary>Gets the raw localized value as written in the locale file.</summary>
    public string Source { get; }

    /// <summary>Gets the distinct placeholder names in order of first appearance.</summary>
    public IReadOnlyList<string> Placeholders { get; }

    /// <summary>
    /// Gets the resolved text when the template has no placeholders (escapes already collapsed), otherwise <see langword="null"/>.
    /// </summary>
    public string? LiteralText { get; }

    /// <summary>
    /// Parses <paramref name="source"/> into a template.
    /// </summary>
    /// <param name="source">The localized value.</param>
    /// <param name="template">The compiled template on success.</param>
    /// <param name="error">A description of the syntax error on failure.</param>
    /// <returns><see langword="true"/> when <paramref name="source"/> is valid template syntax.</returns>
    public static bool TryParse(string source, out TextTemplate template, out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<Segment> segments = [];
        List<string> placeholders = [];
        StringBuilder literal = new();
        error = null;

        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '{')
            {
                if (i + 1 < source.Length && source[i + 1] == '{')
                {
                    _ = literal.Append('{');
                    i += 2;
                    continue;
                }

                int close = source.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = $"unclosed '{{' at index {i}";
                    template = Literal(source);
                    return false;
                }

                string name = source[(i + 1)..close];
                if (!IsValidName(name))
                {
                    error = $"invalid placeholder name '{name}' at index {i}";
                    template = Literal(source);
                    return false;
                }

                if (literal.Length > 0)
                {
                    segments.Add(new Segment(literal.ToString(), isLiteral: true));
                    _ = literal.Clear();
                }

                segments.Add(new Segment(name, isLiteral: false));
                if (!placeholders.Contains(name))
                {
                    placeholders.Add(name);
                }

                i = close + 1;
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < source.Length && source[i + 1] == '}')
                {
                    _ = literal.Append('}');
                    i += 2;
                    continue;
                }

                error = $"unescaped '}}' at index {i}";
                template = Literal(source);
                return false;
            }

            _ = literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(literal.ToString(), isLiteral: true));
        }

        template = new TextTemplate(source, [.. segments], [.. placeholders]);
        return true;
    }

    /// <summary>
    /// Creates a template that renders <paramref name="text"/> verbatim (no placeholder parsing).
    /// </summary>
    /// <param name="text">The verbatim text.</param>
    /// <returns>A literal template.</returns>
    public static TextTemplate Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextTemplate(text, text.Length == 0 ? [] : [new Segment(text, isLiteral: true)], []);
    }

    /// <summary>
    /// Substitutes <paramref name="args"/> into the placeholders. A placeholder without a matching argument is kept as <c>{name}</c>.
    /// </summary>
    /// <param name="args">Named arguments.</param>
    /// <returns>The formatted text (the cached literal when there are no placeholders).</returns>
    public string Format(params ReadOnlySpan<TextArg> args)
    {
        if (this.LiteralText is not null)
        {
            return this.LiteralText;
        }

        StringBuilder builder = s_builder ??= new StringBuilder(128);
        _ = builder.Clear();
        for (int s = 0; s < _segments.Length; s++)
        {
            Segment segment = _segments[s];
            if (segment.IsLiteral)
            {
                _ = builder.Append(segment.Text);
                continue;
            }

            bool found = false;
            for (int a = 0; a < args.Length; a++)
            {
                if (string.Equals(args[a].Name, segment.Text, StringComparison.Ordinal))
                {
                    args[a].AppendTo(builder);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                _ = builder.Append('{').Append(segment.Text).Append('}');
            }
        }

        string result = builder.ToString();
        _ = builder.Clear();
        return result;
    }

    private static bool IsValidName(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetterLower(name[0]))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct Segment(string text, bool isLiteral)
    {
        public string Text { get; } = text;

        public bool IsLiteral { get; } = isLiteral;
    }
}
