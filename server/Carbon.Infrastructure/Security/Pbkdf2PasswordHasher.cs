// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Security.Cryptography;
using Carbon.Abstractions.Security;

namespace Carbon.Infrastructure.Security;

/// <summary>
/// PBKDF2 SHA-256 password hasher with constant-time comparison and zero third-party native dependencies.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 100_000;

    /// <inheritdoc/>
    public void HashPassword(string password, out byte[] hash, out byte[] salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        salt = RandomNumberGenerator.GetBytes(SaltBytes);
        hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
    }

    /// <inheritdoc/>
    public bool Verify(string password, ReadOnlySpan<byte> expectedHash, ReadOnlySpan<byte> salt)
    {
        if (string.IsNullOrEmpty(password) || expectedHash.Length == 0 || salt.Length == 0)
        {
            return false;
        }

        Span<byte> candidate = stackalloc byte[KeyBytes];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, candidate, Iterations, HashAlgorithmName.SHA256);
        return CryptographicOperations.FixedTimeEquals(candidate, expectedHash);
    }
}
