// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// A test that CI never runs cannot catch anything. These rules keep <c>.github/workflows/ci.yml</c> and the
/// solution files in step with the repository, so a new layer solution or test project is never silently skipped.
/// </summary>
public sealed partial class CiCoverageTests
{
    private static string WorkflowPath => Path.Combine(RepositoryLayout.Root, ".github", "workflows", "ci.yml");

    [Fact]
    public void CiWorkflow_BuildsEveryLayerSolution()
    {
        HashSet<string> built = CiSolutions("build");

        string[] layerSolutions = [.. RepositoryLayout.Layers
            .Select(layer => Path.Combine(RepositoryLayout.Root, layer))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.slnx", SearchOption.TopDirectoryOnly))
            .Select(Relative)];

        layerSolutions.Should().NotBeEmpty();
        layerSolutions.Should().OnlyContain(s => built.Contains(s), "every layer solution must be built by ci.yml (dotnet build <slnx>)");
    }

    [Fact]
    public void CiWorkflow_TestsEveryTestSolution()
    {
        HashSet<string> tested = CiSolutions("test");

        string[] testSolutions = [.. Directory.GetFiles(Path.Combine(RepositoryLayout.Root, "tests"), "*.slnx", SearchOption.TopDirectoryOnly).Select(Relative)];

        testSolutions.Should().NotBeEmpty();
        testSolutions.Should().OnlyContain(s => tested.Contains(s), "every tests/*.slnx must be run by ci.yml (dotnet test <slnx>)");
    }

    [Fact]
    public void TestSolutions_IncludeEveryTestProject()
    {
        string testsRoot = Path.Combine(RepositoryLayout.Root, "tests");
        HashSet<string> listed = new(StringComparer.Ordinal);
        foreach (string slnx in Directory.GetFiles(testsRoot, "*.slnx", SearchOption.TopDirectoryOnly))
        {
            string slnxDir = Path.GetDirectoryName(slnx)!;
            foreach (XAttribute path in XDocument.Load(slnx).Descendants("Project").Attributes("Path"))
            {
                listed.Add(Relative(Path.GetFullPath(Path.Combine(slnxDir, path.Value.Replace('\\', '/')))));
            }
        }

        string[] projects = [.. Directory.GetFiles(testsRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Relative)];

        projects.Should().NotBeEmpty();
        projects.Should().OnlyContain(p => listed.Contains(p), "a test project outside every tests/*.slnx is never run by CI");
    }

    [Fact]
    public void VerifyScript_ReadsItsStepsFromTheCiWorkflow()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "tools", "ci", "verify.sh"));

        script.Should().Contain(".github/workflows/ci.yml", "tools/ci/verify.sh must run exactly the CI steps, not a hand-kept copy");
        CiSolutions("build").Should().NotBeEmpty("verify.sh extracts 'run: dotnet ...' lines, so ci.yml must keep that shape");
    }

    private static HashSet<string> CiSolutions(string verb)
    {
        HashSet<string> solutions = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(WorkflowPath))
        {
            Match match = CiDotnetStep().Match(line);
            if (match.Success && string.Equals(match.Groups["verb"].Value, verb, StringComparison.Ordinal))
            {
                solutions.Add(match.Groups["slnx"].Value);
            }
        }

        return solutions;
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryLayout.Root, path).Replace('\\', '/');

    [GeneratedRegex(@"^\s*run:\s*dotnet\s+(?<verb>build|test)\s+(?<slnx>\S+\.slnx)\b")]
    private static partial Regex CiDotnetStep();
}
