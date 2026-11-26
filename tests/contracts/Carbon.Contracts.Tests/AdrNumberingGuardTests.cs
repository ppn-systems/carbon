// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>
/// Wire changes must be justified by an ADR (AGENTS.md rule 5), so ADRs are referenced by number from code and docs.
/// Two ADRs sharing a number make those references ambiguous; this guard keeps every number unique.
/// </summary>
public sealed partial class AdrNumberingGuardTests
{
    [Fact]
    public void AdrFiles_Numbers_AreUniqueAndWellFormed()
    {
        string adrDirectory = Path.Combine(FindRepositoryRoot(), "docs", "adr");
        string[] files = Directory.GetFiles(adrDirectory, "*.md");
        Assert.NotEmpty(files);

        List<(int Number, string Name)> numbered = [];
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            Match match = AdrFileName().Match(name);
            Assert.True(match.Success, $"ADR file '{name}' must be named ADR-NNN-<slug>.md.");
            numbered.Add((int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), name));
        }

        IGrouping<int, (int Number, string Name)>? duplicate = numbered.GroupBy(static a => a.Number).FirstOrDefault(static g => g.Count() > 1);
        Assert.True(duplicate is null, $"Duplicate ADR number {duplicate?.Key:D3}: {string.Join(", ", duplicate?.Select(static a => a.Name) ?? [])}");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root (Carbon.slnx) not found above the test output directory.");
    }

    [GeneratedRegex(@"^ADR-(\d{3})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$")]
    private static partial Regex AdrFileName();
}
