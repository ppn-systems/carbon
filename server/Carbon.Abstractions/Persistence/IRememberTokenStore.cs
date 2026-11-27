// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>A stored remember-me token (ADR-037). Only the SHA-256 hash of the token is ever persisted.</summary>
/// <param name="TokenHash">SHA-256 of the token bytes (32 bytes).</param>
/// <param name="AccountId">Account the token signs in.</param>
/// <param name="ExpiresAtUtc">When the token stops working.</param>
public sealed record RememberTokenRecord(byte[] TokenHash, Guid AccountId, DateTimeOffset ExpiresAtUtc);

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
    Task<RememberTokenRecord?> FindAsync(byte[] tokenHash, CancellationToken ct = default);

    /// <summary>Deletes a token by its hash; deleting a missing token is not an error.</summary>
    /// <param name="tokenHash">SHA-256 of the token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when a row was deleted.</returns>
    Task<bool> DeleteAsync(byte[] tokenHash, CancellationToken ct = default);
}

/// <summary>In-memory <see cref="IRememberTokenStore"/> (tests and stores without a database).</summary>
public sealed class InMemoryRememberTokenStore : IRememberTokenStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RememberTokenRecord> _tokens = new(StringComparer.Ordinal);
    private readonly List<string> _insertOrder = [];

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
    /// <returns>The stored records.</returns>
    public IReadOnlyList<RememberTokenRecord> Snapshot()
    {
        lock (_gate)
        {
            return [.. _tokens.Values];
        }
    }

    /// <inheritdoc/>
    public Task InsertAsync(RememberTokenRecord record, int keepPerAccount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepPerAccount, 1);
        lock (_gate)
        {
            string key = Convert.ToHexString(record.TokenHash);
            _tokens[key] = record;
            _insertOrder.Add(key);

            int kept = 0;
            for (int i = _insertOrder.Count - 1; i >= 0; i--)
            {
                if (!_tokens.TryGetValue(_insertOrder[i], out RememberTokenRecord? existing))
                {
                    _insertOrder.RemoveAt(i);
                }
                else if (existing.AccountId == record.AccountId && ++kept > keepPerAccount)
                {
                    _ = _tokens.Remove(_insertOrder[i]);
                    _insertOrder.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<RememberTokenRecord?> FindAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        lock (_gate)
        {
            return Task.FromResult(_tokens.TryGetValue(Convert.ToHexString(tokenHash), out RememberTokenRecord? record) ? record : null);
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        lock (_gate)
        {
            return Task.FromResult(_tokens.Remove(Convert.ToHexString(tokenHash)));
        }
    }
}
