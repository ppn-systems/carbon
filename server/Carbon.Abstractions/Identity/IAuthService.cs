// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Contracts.Identity;

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
    /// Lists all characters belonging to the authenticated account.
    /// </summary>
    Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new character spawning at the world's starter spawn tile with optional <paramref name="startingClass"/>.
    /// </summary>
    Task<CharacterCreateResult> CreateCharacterAsync(
        Guid accountId,
        string characterName,
        byte appearancePreset,
        float spawnX,
        float spawnY,
        byte startingClass = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a character belonging to <paramref name="accountId"/>, or returns <see langword="null"/> when unauthorized.
    /// </summary>
    Task<CharacterRecord?> FindOwnedCharacterAsync(Guid accountId, Guid characterId, CancellationToken ct = default);
}
