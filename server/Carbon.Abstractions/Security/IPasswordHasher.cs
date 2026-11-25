// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

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
    void HashPassword(string password, out byte[] hash, out byte[] salt);

    /// <summary>
    /// Verifies a plaintext candidate against a stored salt and digest in constant time.
    /// </summary>
    /// <param name="password">Plaintext password candidate.</param>
    /// <param name="expectedHash">Stored hash bytes.</param>
    /// <param name="salt">Stored salt bytes.</param>
    /// <returns><see langword="true"/> when verified; otherwise <see langword="false"/>.</returns>
    bool Verify(string password, ReadOnlySpan<byte> expectedHash, ReadOnlySpan<byte> salt);
}
