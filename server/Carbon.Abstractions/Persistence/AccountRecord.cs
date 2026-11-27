// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Primitives;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Persisted player account record mapped cleanly without reflection.
/// </summary>
/// <param name="Id">Unique account identifier.</param>
/// <param name="Username">Original display username.</param>
/// <param name="NormalizedUsername">Invariant uppercase username used for uniqueness lookups.</param>
/// <param name="PasswordHash">PBKDF2 SHA-256 derived password digest (stored as a 32-byte blob).</param>
/// <param name="PasswordSalt">Random cryptographic salt.</param>
/// <param name="CreatedAtUtc">Account creation UTC timestamp.</param>
/// <param name="IsBanned">Whether the account is prevented from signing in.</param>
public sealed record AccountRecord(
    Guid Id,
    string Username,
    string NormalizedUsername,
    Bytes32 PasswordHash,
    byte[] PasswordSalt,
    DateTimeOffset CreatedAtUtc,
    bool IsBanned);
