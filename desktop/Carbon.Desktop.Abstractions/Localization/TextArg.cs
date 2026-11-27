// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Text;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Named argument substituted into a <c>{name}</c> placeholder of a localized template (ADR-020 D2).
/// Numbers are formatted with the invariant culture.
/// </summary>
public readonly struct TextArg
{
    private readonly string? _text;
    private readonly long _number;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextArg"/> struct with a text value.
    /// </summary>
    /// <param name="name">Placeholder name without braces.</param>
    /// <param name="value">Substituted text (already localized when it is player-facing).</param>
    public TextArg(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        this.Name = name;
        _text = value ?? string.Empty;
        _number = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextArg"/> struct with an integer value.
    /// </summary>
    /// <param name="name">Placeholder name without braces.</param>
    /// <param name="value">Substituted integer.</param>
    public TextArg(string name, long value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        this.Name = name;
        _text = null;
        _number = value;
    }

    /// <summary>Gets the placeholder name.</summary>
    public string Name { get; }

    /// <summary>Gets whether this argument carries an integer value.</summary>
    public bool IsNumber => _text is null;

    /// <summary>Gets the integer value (0 for text arguments).</summary>
    public long Number => _number;

    /// <summary>Gets the text value, or the invariant integer text for numeric arguments.</summary>
    /// <returns>The substituted text.</returns>
    public override string ToString() => _text ?? _number.ToString(CultureInfo.InvariantCulture);

    /// <summary>Appends the argument value to <paramref name="builder"/> without boxing.</summary>
    /// <param name="builder">Destination builder.</param>
    internal void AppendTo(StringBuilder builder)
    {
        if (_text is not null)
        {
            _ = builder.Append(_text);
            return;
        }

        Span<char> digits = stackalloc char[24];
        if (_number.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture))
        {
            _ = builder.Append(digits[..written]);
        }
    }
}
