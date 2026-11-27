// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Primitives;

namespace Carbon.Abstractions.Persistence;

/// <summary>A stored remember-me token (ADR-037). Only the SHA-256 hash of the token is ever persisted.</summary>
/// <param name="TokenHash">SHA-256 of the token bytes.</param>
/// <param name="AccountId">Account the token signs in.</param>
/// <param name="ExpiresAtUtc">When the token stops working.</param>
public sealed record RememberTokenRecord(Bytes32 TokenHash, Guid AccountId, DateTimeOffset ExpiresAtUtc);

/// <summary>Store of remember-me token hashes (ADR-037, table <c>remember_tokens</c>).</summary>
public interface IRememberTokenStore
{
    /// <summary>
    /// Stores a new token hash, then keeps only the account's <paramref name="keepPerAccount"/> newest tokens so a
    /// client asking again and again cannot grow the table.
    /// </summary>
    /// <param name="record">The token to store.</param>
    /// <param name="keepPerAccount">How many tokens one account may hold (one per remembered device).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task InsertAsync(RememberTokenRecord record, int keepPerAccount, CancellationToken ct = default);

    /// <summary>Finds a token by its hash, or returns <see langword="null"/> when none is stored.</summary>
    /// <param name="tokenHash">SHA-256 of the token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored token, expired or not.</returns>
    Task<RememberTokenRecord?> FindAsync(Bytes32 tokenHash, CancellationToken ct = default);

    /// <summary>Deletes a token by its hash; deleting a missing token is not an error.</summary>
    /// <param name="tokenHash">SHA-256 of the token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when a row was deleted.</returns>
    Task<bool> DeleteAsync(Bytes32 tokenHash, CancellationToken ct = default);
}

/// <summary>
/// In-memory <see cref="IRememberTokenStore"/> (tests and stores without a database). Lookups scan with the constant-time
/// <see cref="Bytes32"/> equality, like a database index lookup by hash would match.
/// </summary>
public sealed class InMemoryRememberTokenStore : IRememberTokenStore
{
    private readonly Lock _gate = new();
    private readonly List<RememberTokenRecord> _tokens = [];

    /// <summary>Gets the number of stored tokens.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _tokens.Count;
            }
        }
    }

    /// <summary>Returns a snapshot of the stored records (tests inspect what is persisted).</summary>
    /// <returns>The stored records, oldest first.</returns>
    public IReadOnlyList<RememberTokenRecord> Snapshot()
    {
        lock (_gate)
        {
            return [.. _tokens];
        }
    }

    /// <inheritdoc/>
    public Task InsertAsync(RememberTokenRecord record, int keepPerAccount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepPerAccount, 1);
        lock (_gate)
        {
            _ = _tokens.RemoveAll(t => t.TokenHash == record.TokenHash);
            _tokens.Add(record);

            int kept = 0;
            for (int i = _tokens.Count - 1; i >= 0; i--)
            {
                if (_tokens[i].AccountId == record.AccountId && ++kept > keepPerAccount)
                {
                    _tokens.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<RememberTokenRecord?> FindAsync(Bytes32 tokenHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tokens.Find(t => t.TokenHash == tokenHash));
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(Bytes32 tokenHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tokens.RemoveAll(t => t.TokenHash == tokenHash) > 0);
        }
    }
}
