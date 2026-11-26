// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Primitives.Hashing;

/// <summary>
/// 64-bit FNV-1a content hash. Platform-independent and allocation-free; used to fingerprint
/// configuration documents so client caches can be validated against the server manifest.
/// </summary>
/// <remarks>Not a cryptographic hash: it detects corruption and staleness, not tampering by an attacker.</remarks>
public static class Fnv1a64
{
    /// <summary>FNV-1a 64-bit offset basis.</summary>
    public const ulong OffsetBasis = 0xCBF29CE484222325UL;

    /// <summary>FNV-1a 64-bit prime.</summary>
    public const ulong Prime = 0x00000100000001B3UL;

    /// <summary>Hashes <paramref name="data"/>.</summary>
    /// <param name="data">Bytes to hash.</param>
    /// <returns>The 64-bit FNV-1a hash.</returns>
    public static ulong Hash(ReadOnlySpan<byte> data) => Append(OffsetBasis, data);

    /// <summary>Continues a running hash with more bytes (incremental hashing).</summary>
    /// <param name="hash">Running hash, starting from <see cref="OffsetBasis"/>.</param>
    /// <param name="data">Bytes to append.</param>
    /// <returns>The updated running hash.</returns>
    public static ulong Append(ulong hash, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= Prime;
        }

        return hash;
    }
}
