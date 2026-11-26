// Copyright (c) 2026 PPN Systems. All rights reserved.

using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Live-host tests share process-wide Nalix state (object pool manager, injected handler dependencies),
/// so they must never run in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveHostGroup
{
    /// <summary>Collection name.</summary>
    public const string Name = "Live host";
}
