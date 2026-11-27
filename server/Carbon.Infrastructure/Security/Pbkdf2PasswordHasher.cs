// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Security.Cryptography;
using Carbon.Abstractions.Security;
using Nalix.Abstractions.Primitives;

namespace Carbon.Infrastructure.Security;

/// <summary>
/// PBKDF2 SHA-256 password hasher with constant-time comparison and zero third-party native dependencies.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int Iterations = 100_000;

    /// <inheritdoc/>
    public void HashPassword(string password, out Bytes32 hash, out byte[] salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        salt = RandomNumberGenerator.GetBytes(SaltBytes);
        Span<byte> derived = stackalloc byte[Bytes32.Size];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, derived, Iterations, HashAlgorithmName.SHA256);
        hash = new Bytes32(derived);
        CryptographicOperations.ZeroMemory(derived);
    }

    /// <inheritdoc/>
    public bool Verify(string password, in Bytes32 expectedHash, ReadOnlySpan<byte> salt)
    {
        if (string.IsNullOrEmpty(password) || expectedHash.IsZero || salt.Length == 0)
        {
            return false;
        }

        Span<byte> derived = stackalloc byte[Bytes32.Size];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, derived, Iterations, HashAlgorithmName.SHA256);
        Bytes32 candidate = new(derived);
        CryptographicOperations.ZeroMemory(derived);
        bool match = candidate.Equals(expectedHash); // constant-time
        Bytes32.Wipe(ref candidate);
        return match;
    }
}
