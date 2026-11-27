// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Store of trader sales per restock epoch (ADR-025). The stock itself is derived from the world seed and the epoch;
/// only what was sold is persisted, so a restart never refills a trader mid-epoch.
/// </summary>
public interface ITraderStockRepository
{
    /// <summary>Loads every trader's latest sales.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows.</returns>
    Task<IReadOnlyList<PersistedTraderSales>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>Upserts sales rows (one per trader) in one transaction.</summary>
    /// <param name="sales">Rows.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveBatchAsync(IReadOnlyList<PersistedTraderSales> sales, CancellationToken ct = default);
}

/// <summary>In-memory <see cref="ITraderStockRepository"/> (tests and stores without a database).</summary>
public sealed class InMemoryTraderStockRepository : ITraderStockRepository
{
    private readonly Dictionary<long, PersistedTraderSales> _rows = new();
    private readonly System.Threading.Lock _gate = new();

    /// <inheritdoc/>
    public Task<IReadOnlyList<PersistedTraderSales>> LoadAllAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PersistedTraderSales>>([.. _rows.Values]);
        }
    }

    /// <inheritdoc/>
    public Task SaveBatchAsync(IReadOnlyList<PersistedTraderSales> sales, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(sales);
        lock (_gate)
        {
            foreach (PersistedTraderSales row in sales)
            {
                _rows[row.TraderKey] = row;
            }
        }

        return Task.CompletedTask;
    }
}
