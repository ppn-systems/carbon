// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Identity;
using Carbon.Items.Races;

namespace Carbon.Host.Tests;

/// <summary>Character looks for tests (ADR-042).</summary>
internal static class TestLooks
{
    /// <summary>Gets the shipped catalog's default look: human, first body, first choice of every option.</summary>
    public static CharacterLook Human { get; } = RaceCatalog.LoadShippedDefaults().DefaultLook();
}
