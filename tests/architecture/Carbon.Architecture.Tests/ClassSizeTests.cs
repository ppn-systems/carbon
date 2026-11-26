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
    /// <summary>Current maximum number of <c>AuthoritativeWorld*.cs</c> partial files.</summary>
    private const int MaxAuthoritativeWorldPartials = 10;

    /// <summary>
    /// Current maximum total lines across those partials. History: 5,933 before SOLID-1; 5,827 after extracting
    /// <c>GroundLootService</c>.
    /// </summary>
    private const int MaxAuthoritativeWorldLines = 5_827;

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
