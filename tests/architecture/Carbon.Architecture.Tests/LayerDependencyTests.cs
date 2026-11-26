// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Enforces AGENTS.md rule 3 (layer boundaries) on both the <c>.csproj</c> graph and the compiled assembly metadata.
/// </summary>
public sealed class LayerDependencyTests
{
    /// <summary>Which layers each layer may reference (itself included).</summary>
    private static readonly Dictionary<string, string[]> s_allowedLayerReferences = new(StringComparer.Ordinal)
    {
        ["contracts"] = ["contracts"],
        ["engine"] = ["contracts", "engine"],
        ["graphics"] = ["contracts", "engine", "graphics"],
        ["server"] = ["contracts", "engine", "server"],
        ["desktop"] = ["contracts", "engine", "graphics", "desktop"],
    };

    /// <summary>NuGet package id prefixes that are only allowed in specific layers.</summary>
    private static readonly (string PackagePrefix, string[] AllowedLayers)[] s_restrictedPackages =
    [
        ("Silk.NET", ["graphics", "desktop"]),
        ("StbImageSharp", ["graphics", "desktop"]),
        ("Npgsql", ["server"]),
        ("Microsoft.Data.Sqlite", ["server"]),
        ("SQLitePCLRaw", ["server"]),
        ("Nalix.Hosting", ["server"]),
        ("Nalix.Network", ["server"]),
    ];

    [Fact]
    public void ProductionProjects_ShouldBeDiscovered()
    {
        RepositoryLayout.Projects.Should().NotBeEmpty();
        foreach (string layer in RepositoryLayout.Layers)
        {
            RepositoryLayout.Projects.Should().Contain(p => p.Layer == layer, "layer '{0}' should contain at least one project", layer);
        }
    }

    [Fact]
    public void ProductionProjects_ShouldAllBeReferencedByArchitectureTests()
    {
        string testProject = Path.Combine(RepositoryLayout.Root, "tests", "architecture", "Carbon.Architecture.Tests", "Carbon.Architecture.Tests.csproj");
        string content = File.ReadAllText(testProject).Replace('\\', '/');

        List<string> missing = [.. RepositoryLayout.Projects
            .Select(p => RepositoryLayout.Relative(p.ProjectPath))
            .Where(rel => !content.Contains("../../../" + rel, StringComparison.Ordinal))];

        Violations.ShouldBeEmpty(missing, "every production project must be referenced by Carbon.Architecture.Tests so its assembly is checked");
    }

    [Fact]
    public void ProjectReferences_ShouldRespectLayerBoundaries()
    {
        List<string> violations = [];
        foreach (ProductionProject project in RepositoryLayout.Projects)
        {
            string[] allowed = s_allowedLayerReferences[project.Layer];
            foreach (string reference in project.ProjectReferences)
            {
                string? targetLayer = RepositoryLayout.LayerOf(reference);
                if (targetLayer is null || !allowed.Contains(targetLayer, StringComparer.Ordinal))
                {
                    violations.Add($"{RepositoryLayout.Relative(project.ProjectPath)} ({project.Layer}) -> {RepositoryLayout.Relative(reference)} ({targetLayer ?? "outside layers"})");
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "AGENTS.md rule 3 defines which layers may reference each other");
    }

    [Fact]
    public void CompiledAssemblies_ShouldRespectLayerBoundaries()
    {
        Dictionary<string, string> layerByAssembly = RepositoryLayout.Projects.ToDictionary(p => p.AssemblyName, p => p.Layer, StringComparer.Ordinal);
        List<string> violations = [];

        foreach ((ProductionProject project, Assembly assembly) in RepositoryLayout.Assemblies)
        {
            string[] allowed = s_allowedLayerReferences[project.Layer];
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is { } name && layerByAssembly.TryGetValue(name, out string? targetLayer) &&
                    !allowed.Contains(targetLayer, StringComparer.Ordinal))
                {
                    violations.Add($"{project.AssemblyName} ({project.Layer}) -> {name} ({targetLayer})");
                }
            }
        }

        Violations.ShouldBeEmpty(violations, string.Empty);
    }

    [Fact]
    public void PackageReferences_ShouldStayInTheirLayer()
    {
        List<string> violations = [];
        foreach (ProductionProject project in RepositoryLayout.Projects)
        {
            foreach (string package in project.PackageReferences)
            {
                foreach ((string prefix, string[] allowedLayers) in s_restrictedPackages)
                {
                    if (package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !allowedLayers.Contains(project.Layer, StringComparer.Ordinal))
                    {
                        violations.Add($"{RepositoryLayout.Relative(project.ProjectPath)} ({project.Layer}) references {package}");
                    }
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "rendering, windowing, database, and hosting packages belong to specific layers");
    }
}
