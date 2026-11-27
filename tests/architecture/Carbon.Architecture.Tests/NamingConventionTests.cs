// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Naming conventions from the <c>csharp-style</c> skill and the .NET Framework Design Guidelines, written as positive
/// rules that hold for every project. They have no allowlist: fix the name, never add an exception.
/// See <c>docs/audit/2026-11-naming.md</c>.
/// </summary>
public sealed partial class NamingConventionTests
{
    /// <summary>Top-level folders whose projects these rules cover: the product layers and their tests.</summary>
    private static readonly string[] s_projectFolders = [.. RepositoryLayout.Layers, "tests"];

    [Fact]
    public void Namespaces_EqualRootNamespacePlusFolderPath()
    {
        List<string> violations = [];
        foreach (ProjectIdentity project in AllProjects())
        {
            foreach (string file in SourceFiles(project.Directory))
            {
                Match match = NamespaceRegex().Match(File.ReadAllText(file));
                if (!match.Success)
                {
                    continue;
                }

                string relativeDir = Path.GetRelativePath(project.Directory, Path.GetDirectoryName(file)!).Replace('\\', '/');
                string expected = relativeDir == "." ? project.RootNamespace : project.RootNamespace + "." + relativeDir.Replace('/', '.');
                string actual = match.Groups["ns"].Value;
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    violations.Add($"{RepositoryLayout.Relative(file)}: declares '{actual}', folder implies '{expected}'");
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "a namespace must equal <RootNamespace>.<Folder>.<SubFolder>, so the folder tells you the namespace");
    }

    [Fact]
    public void Projects_NameAssemblyRootNamespaceAndFolderAlike()
    {
        List<string> violations = [];
        foreach (ProjectIdentity project in AllProjects())
        {
            string folder = Path.GetFileName(project.Directory);
            if (!string.Equals(project.AssemblyName, project.FileName, StringComparison.Ordinal) ||
                !string.Equals(project.RootNamespace, project.FileName, StringComparison.Ordinal) ||
                !string.Equals(folder, project.FileName, StringComparison.Ordinal))
            {
                violations.Add($"{RepositoryLayout.Relative(project.Path)}: folder '{folder}', AssemblyName '{project.AssemblyName}', RootNamespace '{project.RootNamespace}'");
            }
        }

        Violations.ShouldBeEmpty(violations, "a project's folder, file, assembly and root namespace must share one name");
    }

    [Fact]
    public void Interfaces_StartWithI()
    {
        List<string> violations = [.. ProductionTypes()
            .Where(type => type.IsInterface && !InterfaceNameRegex().IsMatch(type.Name))
            .Select(type => type.FullName!)];

        Violations.ShouldBeEmpty(violations, "interface names are 'I' + PascalCase (IPacketHandler)");
    }

    /// <summary>Every source-declared type of every production assembly, without compiler- or tool-generated types.</summary>
    internal static IEnumerable<Type> ProductionTypes() =>
        RepositoryLayout.Assemblies
            .SelectMany(a => RepositoryLayout.GetTypesSafe(a.Assembly))
            .Where(type => !CodeHygieneTests.IsCompilerOrToolGenerated(type));

    private static IEnumerable<ProjectIdentity> AllProjects()
    {
        foreach (string folder in s_projectFolders)
        {
            string root = Path.Combine(RepositoryLayout.Root, folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string csproj in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Where(p => !IsBuildOutput(p)).OrderBy(p => p, StringComparer.Ordinal))
            {
                XDocument doc = XDocument.Load(csproj);
                string fileName = Path.GetFileNameWithoutExtension(csproj);
                string assemblyName = Property(doc, "AssemblyName") ?? fileName;
                yield return new ProjectIdentity(csproj, Path.GetDirectoryName(csproj)!, fileName, assemblyName, Property(doc, "RootNamespace") ?? assemblyName);
            }
        }
    }

    private static IEnumerable<string> SourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p))
            .OrderBy(p => p, StringComparer.Ordinal);

    private static bool IsBuildOutput(string path)
    {
        string relative = RepositoryLayout.Relative(path);
        return relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal) ||
            relative.StartsWith("artifacts/", StringComparison.Ordinal);
    }

    private static string? Property(XDocument doc, string name) =>
        doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    [GeneratedRegex(@"^\s*namespace\s+(?<ns>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]", RegexOptions.Multiline)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"^I[A-Z]")]
    private static partial Regex InterfaceNameRegex();

    private sealed record ProjectIdentity(string Path, string Directory, string FileName, string AssemblyName, string RootNamespace);
}
