// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;

namespace Carbon.Contracts.Identity;

/// <summary>
/// A character's structured appearance (ADR-042): a race id, a body type id and one choice index per appearance option
/// the race defines in <c>races.json</c>. Values are ids and indices only, never prose. Whether they are valid is
/// decided by the server's race catalog, never by this type.
/// </summary>
/// <remarks>
/// An unmanaged value type with value equality, so it can sit in records and components without allocating. Option
/// <c>i</c> is the choice index for the race's <c>options[i]</c>.
/// </remarks>
public readonly struct CharacterLook : IEquatable<CharacterLook>
{
    /// <summary>Most appearance options one race may define (and one look may carry).</summary>
    public const int MaxOptions = 16;

    private readonly OptionBuffer _options;

    /// <summary>Initializes a new look.</summary>
    /// <param name="raceId">Race id (<c>races.json</c> <c>races[].id</c>).</param>
    /// <param name="bodyType">Body type id within the race (<c>bodies[].id</c>).</param>
    /// <param name="options">One choice index per race option, in the race's option order.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more than <see cref="MaxOptions"/> options are given.</exception>
    public CharacterLook(ushort raceId, byte bodyType, ReadOnlySpan<byte> options)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Length, MaxOptions, nameof(options));
        this.RaceId = raceId;
        this.BodyType = bodyType;
        this.OptionCount = (byte)options.Length;
        OptionBuffer buffer = default;
        options.CopyTo(buffer);
        _options = buffer;
    }

    /// <summary>Builds a look from wire values without throwing.</summary>
    /// <param name="raceId">Race id.</param>
    /// <param name="bodyType">Body type id.</param>
    /// <param name="options">Option choices as received (may be <see langword="null"/>).</param>
    /// <param name="look">The look, or <see langword="default"/> when there are more than <see cref="MaxOptions"/> options.</param>
    /// <returns><see langword="true"/> when the values fit a look.</returns>
    public static bool TryCreate(ushort raceId, byte bodyType, byte[]? options, out CharacterLook look)
    {
        ReadOnlySpan<byte> span = options;
        if (span.Length > MaxOptions)
        {
            look = default;
            return false;
        }

        look = new CharacterLook(raceId, bodyType, span);
        return true;
    }

    /// <summary>Gets the race id.</summary>
    public ushort RaceId { get; }

    /// <summary>Gets the body type id within the race.</summary>
    public byte BodyType { get; }

    /// <summary>Gets the number of option choices carried.</summary>
    public byte OptionCount { get; }

    /// <summary>Gets the choice index of option <paramref name="index"/>, or 0 when the look carries fewer options.</summary>
    /// <param name="index">Option position in the race's option order.</param>
    /// <returns>The choice index.</returns>
    public byte GetOption(int index) => (uint)index < this.OptionCount ? _options[index] : (byte)0;

    /// <summary>Copies the carried option choices into a new array (for wire and storage).</summary>
    /// <returns>An array of <see cref="OptionCount"/> bytes.</returns>
    public byte[] ToOptionArray()
    {
        if (this.OptionCount == 0)
        {
            return [];
        }

        byte[] result = new byte[this.OptionCount];
        this.CopyOptionsTo(result);
        return result;
    }

    /// <summary>
    /// Copies the carried option choices into <paramref name="reuse"/> when it already has <see cref="OptionCount"/>
    /// bytes, so a pooled packet keeps its array; otherwise into a new array.
    /// </summary>
    /// <param name="reuse">Array a pooled packet already holds, or <see langword="null"/>.</param>
    /// <returns>An array of <see cref="OptionCount"/> bytes holding the choices.</returns>
    public byte[] ToOptionArray(byte[]? reuse)
    {
        if (reuse is null || reuse.Length != this.OptionCount)
        {
            return this.ToOptionArray();
        }

        this.CopyOptionsTo(reuse);
        return reuse;
    }

    /// <summary>Copies the carried option choices into <paramref name="destination"/>.</summary>
    /// <param name="destination">Span of at least <see cref="OptionCount"/> bytes.</param>
    public void CopyOptionsTo(Span<byte> destination)
    {
        ReadOnlySpan<byte> all = _options;
        all[..this.OptionCount].CopyTo(destination);
    }

    /// <inheritdoc/>
    public bool Equals(CharacterLook other)
    {
        if (this.RaceId != other.RaceId || this.BodyType != other.BodyType || this.OptionCount != other.OptionCount)
        {
            return false;
        }

        for (int i = 0; i < this.OptionCount; i++)
        {
            if (_options[i] != other._options[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CharacterLook other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(this.RaceId);
        hash.Add(this.BodyType);
        for (int i = 0; i < this.OptionCount; i++)
        {
            hash.Add(_options[i]);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        Span<char> digits = stackalloc char[(MaxOptions * 4) + 1];
        int written = 0;
        for (int i = 0; i < this.OptionCount; i++)
        {
            if (i > 0)
            {
                digits[written++] = ',';
            }

            _ = _options[i].TryFormat(digits[written..], out int n, provider: System.Globalization.CultureInfo.InvariantCulture);
            written += n;
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"race {this.RaceId} body {this.BodyType} options [{digits[..written]}]");
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(CharacterLook left, CharacterLook right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(CharacterLook left, CharacterLook right) => !left.Equals(right);

    [InlineArray(MaxOptions)]
    private struct OptionBuffer
    {
        private byte _element0;
    }
}
