// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// ADR-017 D7: world ports used by packet handlers expose mutations only as tick-queued <c>Submit*Async</c> commands,
/// never as synchronous <c>Try*</c> methods that would mutate the world on a network I/O thread.
/// </summary>
public sealed class WorldPortTests
{
    /// <summary>
    /// Synchronous mutators still on the ports, waiting for ADR-017 rollout parts 2 (inventory) and 3 (economy and
    /// progression). Entry format: <c>Interface.Method</c>. Do NOT add new entries: add a <c>Submit*Async</c> method.
    /// </summary>
    private static readonly HashSet<string> s_pendingSyncMutatorAllowlist = new(StringComparer.Ordinal)
    {
        // Empty: ADR-017 parts 1-3 converted every mutating handler to Submit*Async.
    };

    [Fact]
    public void WorldPorts_ShouldNotExposeNewSynchronousMutators()
    {
        Violations.ShouldBeEmpty(
            FindSyncMutators().Where(m => !s_pendingSyncMutatorAllowlist.Contains(m)),
            "handlers must submit mutations to the tick queue (ADR-017); expose a Submit*Async method instead");
    }

    [Fact]
    public void PendingSyncMutatorAllowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. FindSyncMutators()];
        Violations.ShouldBeEmpty(s_pendingSyncMutatorAllowlist.Where(e => !found.Contains(e)), "converted mutators must be removed from the allowlist");
    }

    /// <summary>
    /// Synchronous mutators: <c>Try*</c> methods on <c>Carbon.Abstractions.World</c> interfaces returning a mutation
    /// result (<see cref="OpStatus"/> or <see cref="WorldMutationOutcome"/>). Queries returning <c>bool</c> and the session
    /// lifecycle (<c>TryEnterWorld</c>, which ADR-017 D8 keeps synchronous) are not mutators in this sense.
    /// </summary>
    private static IEnumerable<string> FindSyncMutators() =>
        typeof(IWorldMapService).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface && t.Namespace == typeof(IWorldMapService).Namespace)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("Try", StringComparison.Ordinal) &&
                            (m.ReturnType == typeof(OpStatus) || m.ReturnType == typeof(WorldMutationOutcome)))
                .Select(m => $"{t.Name}.{m.Name}"));
}
