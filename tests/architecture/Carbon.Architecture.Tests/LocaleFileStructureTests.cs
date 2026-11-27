// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Carbon.Desktop.Abstractions.Configuration;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Structure guards for <c>resources/localization/*.ini</c> that run on the raw text, so a broken file is reported here
/// by name instead of surfacing as unrelated desktop test failures when the localizer cannot load (ADR-023 D7).
/// </summary>
public sealed class LocaleFileStructureTests
{
    [Fact]
    public void LocaleFiles_HaveNoDuplicateSections()
    {
        List<string> violations = [];
        foreach (string path in LocaleFiles())
        {
            Dictionary<string, int> firstLine = new(StringComparer.Ordinal);
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length < 2 || line[0] != '[' || line[^1] != ']')
                {
                    continue;
                }

                string section = line[1..^1].Trim();
                if (!firstLine.TryAdd(section, i + 1))
                {
                    violations.Add(
                        $"{RepositoryLayout.Relative(path)}: [{section}] is declared on line {firstLine[section]} and again on line {i + 1}. " +
                        $"Move the keys under line {i + 1} into the existing [{section}] section and delete the second header. " +
                        "Two branches that each add the same section merge cleanly in git but fail to load.");
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "a locale file may declare each [section] only once (resources/localization/README.md)");
    }

    [Fact]
    public void LocaleFiles_ParseWithTheClientLoader()
    {
        List<string> violations = [];
        foreach (string path in LocaleFiles())
        {
            try
            {
                _ = IniDocument.Load(path);
            }
            catch (IniFormatException ex)
            {
                violations.Add($"{RepositoryLayout.Relative(path)}: {ex.Message}");
            }
        }

        Violations.ShouldBeEmpty(violations, "the desktop client loads these files at startup; a load error breaks every localized screen");
    }

    private static IEnumerable<string> LocaleFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryLayout.Root, "resources", "localization"), "*.ini")
            .OrderBy(p => p, StringComparer.Ordinal);
}
