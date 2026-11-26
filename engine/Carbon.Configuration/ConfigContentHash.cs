// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Primitives.Hashing;

namespace Carbon.Configuration;

/// <summary>
/// The single content-hash function of the config sync protocol (64-bit FNV-1a over canonical UTF-8 bytes).
/// Server and client must both hash through this type so the algorithm can only change in one place.
/// </summary>
public static class ConfigContentHash
{
    /// <summary>Hashes a document's canonical UTF-8 bytes.</summary>
    /// <param name="utf8">Document bytes.</param>
    /// <returns>The 64-bit content hash.</returns>
    public static ulong Compute(ReadOnlySpan<byte> utf8) => Fnv1a64.Hash(utf8);
}
