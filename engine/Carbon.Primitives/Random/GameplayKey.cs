// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Carbon.Primitives.Random;

/// <summary>
/// A 128-bit secret key for server-only gameplay rolls (ADR-026): forge, loot, trader stock, crit, evasion, food
/// poisoning and monster spawns. Every roll stream is a SipHash-2-4 PRF of this key over the roll inputs (tick, ids,
/// nonce), so a client that knows the public world seed and the tick still cannot predict an outcome.
/// </summary>
/// <remarks>
/// Never build one from the world seed and never put one in a packet or a log line: <see cref="ToString"/> is redacted.
/// The key only leaves the process through <see cref="WriteBytes"/>, for persistence.
/// </remarks>
public readonly struct GameplayKey : IEquatable<GameplayKey>
{
    /// <summary>Key size in bytes (128 bits).</summary>
    public const int SizeInBytes = 16;

    private readonly ulong _k0;
    private readonly ulong _k1;

    private GameplayKey(ulong k0, ulong k1)
    {
        _k0 = k0;
        _k1 = k1;
    }

    /// <summary>Gets a value indicating whether every key bit is zero (an uninitialized key).</summary>
    public bool IsZero => (_k0 | _k1) == 0UL;

    /// <summary>Compares two keys.</summary>
    public static bool operator ==(GameplayKey left, GameplayKey right) => left.Equals(right);

    /// <summary>Compares two keys.</summary>
    public static bool operator !=(GameplayKey left, GameplayKey right) => !left.Equals(right);

    /// <summary>Reads a key from <see cref="SizeInBytes"/> bytes (little endian), e.g. a persisted or generated secret.</summary>
    /// <param name="bytes">Exactly <see cref="SizeInBytes"/> bytes.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="bytes"/> is not <see cref="SizeInBytes"/> long or is all zero.</exception>
    public static GameplayKey FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
        {
            throw new ArgumentException($"A gameplay key is {SizeInBytes} bytes.", nameof(bytes));
        }

        GameplayKey key = new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
        return key.IsZero ? throw new ArgumentException("A gameplay key must not be all zero.", nameof(bytes)) : key;
    }

    /// <summary>Writes the key bytes (little endian) for persistence.</summary>
    /// <param name="destination">At least <see cref="SizeInBytes"/> bytes.</param>
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException($"A gameplay key is {SizeInBytes} bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination, _k0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], _k1);
    }

    /// <summary>
    /// SipHash-2-4 of the 32-byte little-endian message <c>domain ‖ a ‖ b ‖ c</c> under this key. A keyed PRF: without
    /// the key, outputs are unpredictable even when every input is known.
    /// </summary>
    /// <param name="domain">Roll kind (<see cref="GameplayDomain"/>), so two kinds never share a stream.</param>
    /// <param name="a">First input.</param>
    /// <param name="b">Second input.</param>
    /// <param name="c">Third input.</param>
    /// <returns>64 pseudo-random bits.</returns>
    public ulong Hash(ulong domain, ulong a, ulong b = 0UL, ulong c = 0UL)
    {
        ulong v0 = _k0 ^ 0x736F6D6570736575UL;
        ulong v1 = _k1 ^ 0x646F72616E646F6DUL;
        ulong v2 = _k0 ^ 0x6C7967656E657261UL;
        ulong v3 = _k1 ^ 0x7465646279746573UL;

        Compress(ref v0, ref v1, ref v2, ref v3, domain);
        Compress(ref v0, ref v1, ref v2, ref v3, a);
        Compress(ref v0, ref v1, ref v2, ref v3, b);
        Compress(ref v0, ref v1, ref v2, ref v3, c);
        Compress(ref v0, ref v1, ref v2, ref v3, 32UL << 56);

        v2 ^= 0xFFUL;
        Round(ref v0, ref v1, ref v2, ref v3);
        Round(ref v0, ref v1, ref v2, ref v3);
        Round(ref v0, ref v1, ref v2, ref v3);
        Round(ref v0, ref v1, ref v2, ref v3);
        return v0 ^ v1 ^ v2 ^ v3;
    }

    /// <summary>Derives an independent subkey (for example one per rotation epoch) from this key.</summary>
    /// <param name="domain">Derivation purpose (<see cref="GameplayDomain"/>).</param>
    /// <param name="a">First input (for example the epoch).</param>
    /// <param name="b">Second input (for example the boot salt).</param>
    /// <returns>The subkey.</returns>
    public GameplayKey Derive(ulong domain, ulong a, ulong b = 0UL)
    {
        GameplayKey derived = new(this.Hash(domain, a, b, 0UL), this.Hash(domain, a, b, 1UL));
        return derived.IsZero ? new GameplayKey(0x9E3779B97F4A7C15UL, 0UL) : derived;
    }

    /// <summary>Creates the roll stream of one roll: a <see cref="DeterministicRandom"/> seeded from <see cref="Hash"/>.</summary>
    /// <param name="domain">Roll kind (<see cref="GameplayDomain"/>).</param>
    /// <param name="a">First input.</param>
    /// <param name="b">Second input.</param>
    /// <param name="c">Third input.</param>
    /// <returns>The roll stream.</returns>
    public DeterministicRandom CreateRandom(ulong domain, ulong a, ulong b = 0UL, ulong c = 0UL) =>
        new(this.Hash(domain, a, b, c));

    /// <summary>Returns a uniform sample in <c>[0, 1)</c> for one roll.</summary>
    /// <param name="domain">Roll kind (<see cref="GameplayDomain"/>).</param>
    /// <param name="a">First input.</param>
    /// <param name="b">Second input.</param>
    /// <param name="c">Third input.</param>
    /// <returns>A value in <c>[0, 1)</c>.</returns>
    public double Sample(ulong domain, ulong a, ulong b = 0UL, ulong c = 0UL) =>
        (this.Hash(domain, a, b, c) >> 11) * (1.0 / (1UL << 53));

    /// <inheritdoc/>
    public bool Equals(GameplayKey other) => ((_k0 ^ other._k0) | (_k1 ^ other._k1)) == 0UL;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GameplayKey other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (int)this.Hash(GameplayDomain.HashCode, 0UL);

    /// <summary>Returns a redacted description: the key bytes never appear in logs or exception messages.</summary>
    /// <returns>A redacted string.</returns>
    public override string ToString() => this.IsZero ? "GameplayKey(zero)" : "GameplayKey(redacted)";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Compress(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3, ulong m)
    {
        v3 ^= m;
        Round(ref v0, ref v1, ref v2, ref v3);
        Round(ref v0, ref v1, ref v2, ref v3);
        v0 ^= m;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Round(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3)
    {
        v0 += v1;
        v1 = BitOperations.RotateLeft(v1, 13);
        v1 ^= v0;
        v0 = BitOperations.RotateLeft(v0, 32);
        v2 += v3;
        v3 = BitOperations.RotateLeft(v3, 16);
        v3 ^= v2;
        v0 += v3;
        v3 = BitOperations.RotateLeft(v3, 21);
        v3 ^= v0;
        v2 += v1;
        v1 = BitOperations.RotateLeft(v1, 17);
        v1 ^= v2;
        v2 = BitOperations.RotateLeft(v2, 32);
    }
}
