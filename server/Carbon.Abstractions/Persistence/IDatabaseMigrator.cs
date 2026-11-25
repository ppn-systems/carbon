// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Executes idempotent Native AOT SQL schema migrations at server startup.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// Ensures all required tables and indexes exist for the configured database provider.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task MigrateAsync(CancellationToken ct = default);
}
