// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
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
