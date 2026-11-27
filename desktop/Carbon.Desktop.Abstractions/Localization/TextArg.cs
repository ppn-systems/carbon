// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Text;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Named argument substituted into a <c>{name}</c> placeholder of a localized template (ADR-023 D2).
/// Numbers are formatted with the invariant culture.
/// </summary>
public readonly struct TextArg
{
    private static readonly string[] s_fixedFormats = ["F0", "F1", "F2", "F3"];

    private readonly string? _text;
    private readonly long _number;
    private readonly byte _decimals;

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
        _decimals = 0;
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
        _decimals = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextArg"/> struct with a fixed-point value such as a cooldown
    /// (<c>1.5</c> with one decimal). The value is stored scaled, so comparing and caching it never allocates.
    /// </summary>
    /// <param name="name">Placeholder name without braces.</param>
    /// <param name="value">Substituted value.</param>
    /// <param name="decimals">Digits after the decimal point (0..3).</param>
    public TextArg(string name, double value, int decimals)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(decimals, nameof(decimals));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, s_fixedFormats.Length - 1, nameof(decimals));
        this.Name = name;
        _text = null;
        _decimals = (byte)decimals;
        _number = (long)Math.Round(value * Math.Pow(10, decimals), MidpointRounding.AwayFromZero);
    }

    /// <summary>Gets the placeholder name.</summary>
    public string Name { get; }

    /// <summary>Gets whether this argument carries an integer value.</summary>
    public bool IsNumber => _text is null;

    /// <summary>Gets the integer value (0 for text arguments).</summary>
    public long Number => _number;

    /// <summary>Gets the text value, or the invariant integer text for numeric arguments.</summary>
    /// <returns>The substituted text.</returns>
    public override string ToString() =>
        _text ?? (_decimals == 0
            ? _number.ToString(CultureInfo.InvariantCulture)
            : this.ScaledValue().ToString(s_fixedFormats[_decimals], CultureInfo.InvariantCulture));

    /// <summary>Returns whether this argument has the same name and value as <paramref name="other"/> (no allocation).</summary>
    /// <param name="other">The argument to compare.</param>
    /// <returns><see langword="true"/> when name and value match.</returns>
    public bool SameAs(in TextArg other) =>
        string.Equals(this.Name, other.Name, StringComparison.Ordinal)
        && (_text is null
            ? other._text is null && _number == other._number && _decimals == other._decimals
            : string.Equals(_text, other._text, StringComparison.Ordinal));

    /// <summary>Appends the argument value to <paramref name="builder"/> without boxing.</summary>
    /// <param name="builder">Destination builder.</param>
    internal void AppendTo(StringBuilder builder)
    {
        if (_text is not null)
        {
            _ = builder.Append(_text);
            return;
        }

        Span<char> digits = stackalloc char[32];
        bool formatted = _decimals == 0
            ? _number.TryFormat(digits, out int written, default, CultureInfo.InvariantCulture)
            : this.ScaledValue().TryFormat(digits, out written, s_fixedFormats[_decimals], CultureInfo.InvariantCulture);
        if (formatted)
        {
            _ = builder.Append(digits[..written]);
        }
    }

    private decimal ScaledValue()
    {
        decimal scale = 1m;
        for (int i = 0; i < _decimals; i++)
        {
            scale *= 10m;
        }

        return _number / scale;
    }
}
