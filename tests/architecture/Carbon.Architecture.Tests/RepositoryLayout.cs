// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Carbon.Architecture.Tests;

/// <summary>A production project discovered on disk under one of the layer folders.</summary>
/// <param name="Layer">Top-level folder (contracts, engine, graphics, server, desktop).</param>
/// <param name="ProjectPath">Absolute path of the <c>.csproj</c> file.</param>
/// <param name="AssemblyName">Assembly name (falls back to the project file name).</param>
/// <param name="RootNamespace">Root namespace (falls back to the assembly name).</param>
/// <param name="ProjectReferences">Absolute paths of referenced projects.</param>
/// <param name="PackageReferences">Ids of referenced NuGet packages.</param>
internal sealed record ProductionProject(
    string Layer,
    string ProjectPath,
    string AssemblyName,
    string RootNamespace,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences)
{
    public string ProjectDirectory => Path.GetDirectoryName(this.ProjectPath)!;
}

/// <summary>Discovers the repository root, the production projects, and their compiled assemblies.</summary>
internal static class RepositoryLayout
{
    /// <summary>Layer folders that hold production code, as defined in AGENTS.md.</summary>
    public static readonly IReadOnlyList<string> Layers = ["contracts", "engine", "graphics", "server", "desktop"];

    private static readonly Lazy<string> s_root = new(FindRoot);
    private static readonly Lazy<IReadOnlyList<ProductionProject>> s_projects = new(DiscoverProjects);
    private static readonly Lazy<IReadOnlyList<(ProductionProject Project, Assembly Assembly)>> s_assemblies = new(LoadAssemblies);

    public static string Root => s_root.Value;

    public static IReadOnlyList<ProductionProject> Projects => s_projects.Value;

    public static IReadOnlyList<(ProductionProject Project, Assembly Assembly)> Assemblies => s_assemblies.Value;

    public static IEnumerable<(ProductionProject Project, Assembly Assembly)> AssembliesIn(params string[] layers) =>
        Assemblies.Where(a => layers.Contains(a.Project.Layer, StringComparer.Ordinal));

    /// <summary>Returns all types of an assembly, tolerating partially loadable assemblies.</summary>
    public static Type[] GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).ToArray()!;
        }
    }

    /// <summary>
    /// Repo-relative paths (forward slashes) of files committed under <paramref name="relativeDirectory"/>.
    /// Uses <c>git ls-files</c>, so git-ignored local files (e.g. imported reference art) are not reported.
    /// Falls back to the filesystem minus directories git-ignored in the root <c>.gitignore</c> when git is unavailable.
    /// </summary>
    public static IReadOnlyList<string> TrackedFiles(string relativeDirectory)
    {
        if (TryGitLsFiles(relativeDirectory, out List<string>? tracked))
        {
            return tracked;
        }

        string[] ignoredPrefixes = ReadIgnoredDirectoryPrefixes();
        string absolute = Path.Combine(Root, relativeDirectory);
        if (!Directory.Exists(absolute))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories)
            .Select(Relative)
            .Where(rel => !ignoredPrefixes.Any(prefix => rel.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(rel => rel, StringComparer.Ordinal)];
    }

    private static bool TryGitLsFiles(string relativeDirectory, out List<string> files)
    {
        files = [];
        try
        {
            ProcessStartInfo startInfo = new("git")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("-z");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(relativeDirectory);

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return false;
            }

            files = ParseLsFilesOutput(output);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses NUL-separated <c>git ls-files -z</c> output into sorted, distinct paths. During an unresolved merge git
    /// lists a conflicted path once per index stage (base, ours, theirs). Without the dedupe, file-based guards read
    /// the same file up to three times, and a class looked about 3x its size to the size caps.
    /// </summary>
    internal static List<string> ParseLsFilesOutput(string output) =>
        [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal)];

    private static string[] ReadIgnoredDirectoryPrefixes()
    {
        string gitignore = Path.Combine(Root, ".gitignore");
        if (!File.Exists(gitignore))
        {
            return [];
        }

        return [.. File.ReadAllLines(gitignore)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith('!') && line.EndsWith('/') && !line.Contains('*', StringComparison.Ordinal))
            .Select(line => line.TrimStart('/'))];
    }

    /// <summary>Returns the repo-relative path with forward slashes.</summary>
    public static string Relative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');

    /// <summary>Enumerates C# source files of a project, excluding build output.</summary>
    public static IEnumerable<string> SourceFiles(ProductionProject project) =>
        Directory.EnumerateFiles(project.ProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(project.ProjectDirectory, p))
            .OrderBy(p => p, StringComparer.Ordinal);

    /// <summary>Returns the layer (top-level folder) a path belongs to, or <c>null</c> when outside the layers.</summary>
    public static string? LayerOf(string absolutePath)
    {
        string relative = Relative(absolutePath);
        int slash = relative.IndexOf('/', StringComparison.Ordinal);
        string top = slash < 0 ? relative : relative[..slash];
        return Layers.Contains(top, StringComparer.Ordinal) ? top : null;
    }

    private static bool IsBuildOutput(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path).Replace('\\', '/');
        return relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "AGENTS.md")) && File.Exists(Path.Combine(cursor.FullName, "Carbon.slnx")))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Carbon repository root (AGENTS.md + Carbon.slnx) above " + AppContext.BaseDirectory);
    }

    private static IReadOnlyList<ProductionProject> DiscoverProjects()
    {
        List<ProductionProject> projects = [];
        foreach (string layer in Layers)
        {
            string layerDir = Path.Combine(Root, layer);
            if (!Directory.Exists(layerDir))
            {
                continue;
            }

            foreach (string csproj in Directory.EnumerateFiles(layerDir, "*.csproj", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                XDocument doc = XDocument.Load(csproj);
                string fileName = Path.GetFileNameWithoutExtension(csproj);
                string assemblyName = Property(doc, "AssemblyName") ?? fileName;
                string rootNamespace = Property(doc, "RootNamespace") ?? assemblyName;
                string dir = Path.GetDirectoryName(csproj)!;

                List<string> projectRefs = [.. doc.Descendants()
                    .Where(e => e.Name.LocalName == "ProjectReference")
                    .Select(e => (string?)e.Attribute("Include"))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => Path.GetFullPath(Path.Combine(dir, v!.Replace('\\', Path.DirectorySeparatorChar))))];

                List<string> packageRefs = [.. doc.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference")
                    .Select(e => (string?)e.Attribute("Include"))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)];

                projects.Add(new ProductionProject(layer, csproj, assemblyName, rootNamespace, projectRefs, packageRefs));
            }
        }

        return projects;
    }

    private static string? Property(XDocument doc, string name) =>
        doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static IReadOnlyList<(ProductionProject Project, Assembly Assembly)> LoadAssemblies()
    {
        List<(ProductionProject, Assembly)> loaded = [];
        foreach (ProductionProject project in Projects)
        {
            loaded.Add((project, Assembly.Load(new AssemblyName(project.AssemblyName))));
        }

        return loaded;
    }
}
