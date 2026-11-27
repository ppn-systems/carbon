// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Contracts.Identity;
using Nalix.Abstractions.Primitives;

namespace Carbon.Abstractions.Identity;

/// <summary>
/// Result of an authentication or account registration attempt.
/// </summary>
/// <param name="Status">Authentication outcome.</param>
/// <param name="AccountId">Authenticated account ID when <paramref name="Status"/> is <see cref="AuthStatus.Ok"/>.</param>
public readonly record struct AuthOutcome(AuthStatus Status, Guid AccountId);

/// <summary>
/// Result of creating a character under an authenticated account.
/// </summary>
/// <param name="Succeeded">Whether creation succeeded.</param>
/// <param name="FailureReason">Human-readable rejection code when unsuccessful.</param>
/// <param name="Character">Created record when <paramref name="Succeeded"/> is <see langword="true"/>.</param>
public readonly record struct CharacterCreateResult(bool Succeeded, string? FailureReason, CharacterRecord? Character);

/// <summary>A newly issued remember-me token (ADR-037). The plaintext token exists only here and on the client.</summary>
/// <param name="Token">The bearer token: 32 random bytes.</param>
/// <param name="ExpiresAtUtc">When it stops working.</param>
public readonly record struct RememberTokenIssue(Bytes32 Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Application identity service handling registration, login, and character roster management.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new account and hashes its password.
    /// </summary>
    Task<AuthOutcome> RegisterAsync(string username, string password, ushort protocolVersion, CancellationToken ct = default);

    /// <summary>
    /// Authenticates an existing account by username and password.
    /// </summary>
    Task<AuthOutcome> LoginAsync(string username, string password, ushort protocolVersion, CancellationToken ct = default);

    /// <summary>
    /// Verifies credentials as <see cref="LoginAsync(string, string, ushort, CancellationToken)"/>, also counting failed
    /// attempts per <paramref name="clientAddress"/> (normalized IP, no port) so one address cannot spray usernames.
    /// </summary>
    Task<AuthOutcome> LoginAsync(string username, string password, ushort protocolVersion, string? clientAddress, CancellationToken ct = default);

    /// <summary>
    /// Issues a remember-me token for an account that just signed in (ADR-037). Only the token's hash is stored.
    /// </summary>
    Task<RememberTokenIssue> IssueRememberTokenAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Signs in with a remember-me token: <see cref="AuthStatus.InvalidCredentials"/> for an unknown, expired or revoked
    /// token, <see cref="AuthStatus.Locked"/> for a banned account (ADR-037).
    /// </summary>
    Task<AuthOutcome> LoginWithRememberTokenAsync(Bytes32 token, ushort protocolVersion, CancellationToken ct = default);

    /// <summary>
    /// Revokes a remember-me token so it can never sign in again; revoking an unknown token does nothing (ADR-037).
    /// </summary>
    Task RevokeRememberTokenAsync(Bytes32 token, CancellationToken ct = default);

    /// <summary>
    /// Lists all characters belonging to the authenticated account.
    /// </summary>
    Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new classless character with an empty bag at the world's starter spawn tile (ADR-030).
    /// </summary>
    Task<CharacterCreateResult> CreateCharacterAsync(
        Guid accountId,
        string characterName,
        byte appearancePreset,
        float spawnX,
        float spawnY,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a character belonging to <paramref name="accountId"/>, or returns <see langword="null"/> when unauthorized.
    /// </summary>
    Task<CharacterRecord?> FindOwnedCharacterAsync(Guid accountId, Guid characterId, CancellationToken ct = default);
}
