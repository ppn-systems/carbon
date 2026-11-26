// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;

namespace Carbon.Architecture.Tests;

/// <summary>Asserts a violation list is empty and prints every entry (one per line) when it is not.</summary>
internal static class Violations
{
    public static void ShouldBeEmpty(IEnumerable<string> violations, string because)
    {
        List<string> list = [.. violations.OrderBy(v => v, StringComparer.Ordinal)];
        string report = list.Count == 0
            ? string.Empty
            : $"{list.Count} violation(s):{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", list)}";
        report.Should().BeEmpty(because);
    }
}
