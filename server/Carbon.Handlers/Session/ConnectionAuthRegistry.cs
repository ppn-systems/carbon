// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Concurrent;
using System;

namespace Carbon.Handlers.Session;

/// <summary>
/// Server-authoritative registry binding active transport connection IDs to authenticated account IDs.
/// Keeps identity strictly in server memory rather than trusting connection attribute bags.
/// </summary>
public static class ConnectionAuthRegistry
{
    private static readonly ConcurrentDictionary<ulong, Guid> s_accountsByConnection = new();

    /// <summary>Associates a connection ID with an authenticated account ID.</summary>
    public static void SetAuthenticated(ulong connectionId, Guid accountId) =>
        s_accountsByConnection[connectionId] = accountId;

    /// <summary>Attempts to resolve the authenticated account ID bound to a connection.</summary>
    public static bool TryGetAccountId(ulong connectionId, out Guid accountId) =>
        s_accountsByConnection.TryGetValue(connectionId, out accountId);

    /// <summary>Clears any authenticated binding when a session logs out or disconnects.</summary>
    public static bool Clear(ulong connectionId) =>
        s_accountsByConnection.TryRemove(connectionId, out _);
}
