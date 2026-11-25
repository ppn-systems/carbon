// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Native AOT persistence store for player characters.
/// </summary>
public interface ICharacterStore
{
    /// <summary>
    /// Lists all characters owned by <paramref name="accountId"/>.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of character records.</returns>
    Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single character by its identifier.
    /// </summary>
    /// <param name="characterId">Character identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The character record if found; otherwise <see langword="null"/>.</returns>
    Task<CharacterRecord?> FindByIdAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new character if the display name is unique.
    /// </summary>
    /// <param name="character">Character record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if created; <see langword="false"/> on duplicate name.</returns>
    Task<bool> TryCreateAsync(CharacterRecord character, CancellationToken ct = default);

    /// <summary>
    /// Persists updated character positions, health, and inventory blobs in a single batch transaction.
    /// </summary>
    /// <param name="characters">Dirty character snapshots.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveBatchAsync(IReadOnlyList<CharacterRecord> characters, CancellationToken ct = default);
}
