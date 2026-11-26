// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Audit SOLID-1: <c>AuthoritativeWorld</c> is a god class being decomposed into domain services, one small
/// extraction at a time. These caps stop it from growing back. Each extraction PR lowers them to the new size;
/// never raise them. New behavior belongs in a service under <c>server/Carbon.Application/World/&lt;Domain&gt;/</c>.
/// </summary>
public sealed class ClassSizeTests
{
    /// <summary>Current maximum number of <c>AuthoritativeWorld*.cs</c> partial files (10 before folding Construction and DebugOverrides).</summary>
    private const int MaxAuthoritativeWorldPartials = 8;

    /// <summary>
    /// Current maximum total lines across those partials. History: 5,933 before SOLID-1; 5,827 after extracting
    /// <c>GroundLootService</c>; 5,517 after extracting <c>OutboundTickBuffer</c>; 5,376 after extracting
    /// <c>ProjectileService</c>; 5,174 after extracting <c>NeedsService</c>; 4,831 after extracting
    /// <c>ConstructionService</c>; 4,714 after extracting <c>AoiReplicator</c>; 4,694 after extracting
    /// <c>WorldCommandExecutor</c>; 4,639 after moving the player state to <c>PlayerState</c> / <c>PlayerRegistry</c>;
    /// 4,277 after extracting <c>InventoryService</c>; 4,107 after extracting <c>CombatService</c>; 3,870 after extracting
    /// <c>PropHarvestService</c>; 3,722 after extracting <c>CharacterPersistence</c>; 3,696 after folding the
    /// Construction and DebugOverrides partials; 3,357 after extracting <c>PlayerActionService</c>; 3,172 after extracting
    /// <c>SettlementNpcSpawner</c>.
    /// </summary>
    private const int MaxAuthoritativeWorldLines = 3_172;

    [Fact]
    public void AuthoritativeWorld_ShouldNotGainPartialFiles()
    {
        AuthoritativeWorldFiles().Length.Should().BeLessThanOrEqualTo(
            MaxAuthoritativeWorldPartials,
            "add a domain service instead of another AuthoritativeWorld partial (audit SOLID-1)");
    }

    [Fact]
    public void AuthoritativeWorld_ShouldNotGrow()
    {
        int lines = AuthoritativeWorldFiles().Sum(f => File.ReadAllLines(f).Length);
        lines.Should().BeLessThanOrEqualTo(
            MaxAuthoritativeWorldLines,
            "AuthoritativeWorld is being decomposed (audit SOLID-1); put new logic in a domain service, or extract code and lower the cap");
    }

    private static string[] AuthoritativeWorldFiles() =>
        Directory.GetFiles(Path.Combine(RepositoryLayout.Root, "server", "Carbon.Application", "World"), "AuthoritativeWorld*.cs");
}
