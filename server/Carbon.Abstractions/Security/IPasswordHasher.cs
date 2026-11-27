// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Primitives;

namespace Carbon.Abstractions.Security;

/// <summary>
/// Computes and verifies constant-time salted cryptographic password hashes.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Derives a salted password hash using a random cryptographic salt.
    /// </summary>
    /// <param name="password">Plaintext password.</param>
    /// <param name="hash">Derived hash digest.</param>
    /// <param name="salt">Generated random salt.</param>
    void HashPassword(string password, out Bytes32 hash, out byte[] salt);

    /// <summary>
    /// Verifies a plaintext candidate against a stored salt and digest in constant time.
    /// </summary>
    /// <param name="password">Plaintext password candidate.</param>
    /// <param name="expectedHash">Stored hash; <see cref="Bytes32.Zero"/> (an unreadable stored hash) never verifies.</param>
    /// <param name="salt">Stored salt bytes.</param>
    /// <returns><see langword="true"/> when verified; otherwise <see langword="false"/>.</returns>
    bool Verify(string password, in Bytes32 expectedHash, ReadOnlySpan<byte> salt);
}
