// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Store of the world's gameplay secret (ADR-026, table <c>gameplay_secret</c>): one row, written once on the first boot
/// of a world and read on every later boot, so trader stock per restock epoch survives restarts. The value is a server
/// secret: never log it and never send it to a client.
/// </summary>
public interface IGameplaySecretStore
{
    /// <summary>
    /// Returns the persisted secret, storing <paramref name="candidate"/> first when none exists yet. Concurrent first
    /// boots agree on one value (insert-if-absent, then read).
    /// </summary>
    /// <param name="candidate">A freshly generated secret, stored only when the world has none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The world's secret bytes (a copy the caller may wipe).</returns>
    Task<byte[]> GetOrCreateAsync(ReadOnlyMemory<byte> candidate, CancellationToken ct = default);
}

/// <summary>In-memory <see cref="IGameplaySecretStore"/> (tests and stores without a database).</summary>
public sealed class InMemoryGameplaySecretStore : IGameplaySecretStore
{
    private readonly Lock _gate = new();
    private byte[]? _secret;

    /// <inheritdoc/>
    public Task<byte[]> GetOrCreateAsync(ReadOnlyMemory<byte> candidate, CancellationToken ct = default)
    {
        if (candidate.IsEmpty)
        {
            throw new ArgumentException("The candidate secret must not be empty.", nameof(candidate));
        }

        lock (_gate)
        {
            _secret ??= candidate.ToArray();
            return Task.FromResult((byte[])_secret.Clone());
        }
    }
}
