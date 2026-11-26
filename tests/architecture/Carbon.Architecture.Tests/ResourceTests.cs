// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Carbon.Desktop.Rendering.VectorArt;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Guards <c>resources/</c>: JSON must parse, vector art must load through the real loader, and raster images are restricted.
/// </summary>
public sealed class ResourceTests
{
    /// <summary>Raster image extensions that are not allowed outside the allowed folders.</summary>
    private static readonly string[] s_rasterExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tga", ".webp"];

    /// <summary>
    /// Folders (repo-relative, with trailing slash) where raster images were already committed.
    /// resources/ui holds nine-slice panel textures (ADR-008); resources/fonts holds font atlases if any.
    /// </summary>
    private static readonly string[] s_rasterAllowedFolders = ["resources/ui/", "resources/fonts/"];

    private static string ResourcesRoot => Path.Combine(RepositoryLayout.Root, "resources");

    private static string ArtRoot => Path.Combine(ResourcesRoot, "art");

    [Fact]
    public void AllResourceJsonFiles_ShouldParse()
    {
        List<string> files = [.. Directory.EnumerateFiles(ResourcesRoot, "*.json", SearchOption.AllDirectories)];
        files.Should().NotBeEmpty();

        List<string> failures = [];
        foreach (string file in files)
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(file));
            }
            catch (JsonException ex)
            {
                failures.Add($"{RepositoryLayout.Relative(file)}: {ex.Message}");
            }
        }

        Violations.ShouldBeEmpty(failures, "every JSON resource must be strictly valid JSON (no comments, no trailing commas)");
    }

    [Fact]
    public void VectorArtCatalog_ShouldLoadThroughTheRealLoader()
    {
        // LoadFromDirectory deserializes and compiles every blueprint the catalog references, and throws on schema errors.
        Action load = () => CarbonVectorArtLibrary.LoadFromDirectory(ArtRoot);
        load.Should().NotThrow();

        CarbonVectorArtLibrary library = CarbonVectorArtLibrary.LoadFromDirectory(ArtRoot);
        library.CharacterCount.Should().BePositive();
        library.MonsterCount.Should().BePositive();
        library.ItemCount.Should().BePositive();
        library.PropCount.Should().BePositive();
        library.BiomeCount.Should().BePositive();
        library.StructureCount.Should().BePositive();
    }

    [Fact]
    public void EveryCvgFile_ShouldBeReferencedByTheCatalog()
    {
        // A .cvg.json that the catalog does not reference is never loaded, so it is never validated either.
        HashSet<string> referenced = CatalogReferencedFiles();
        List<string> orphans = [.. Directory.EnumerateFiles(ArtRoot, "*.cvg.json", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(p => !string.Equals(Path.GetFileName(p), "catalog.cvg.json", StringComparison.OrdinalIgnoreCase))
            .Where(p => !referenced.Contains(p))
            .Select(RepositoryLayout.Relative)];

        Violations.ShouldBeEmpty(orphans, "every vector blueprint must be declared in resources/art/catalog.cvg.json so the loader validates it");
    }

    [Fact]
    public void EveryCatalogReference_ShouldExist()
    {
        List<string> missing = [.. CatalogReferencedFiles().Where(p => !File.Exists(p)).Select(RepositoryLayout.Relative)];
        Violations.ShouldBeEmpty(missing, string.Empty);
    }

    [Fact]
    public void RasterImages_ShouldOnlyExistInAllowedResourceFolders()
    {
        List<string> violations = [.. Directory.EnumerateFiles(ResourcesRoot, "*", SearchOption.AllDirectories)
            .Where(p => s_rasterExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Select(RepositoryLayout.Relative)
            .Where(rel => !s_rasterAllowedFolders.Any(f => rel.StartsWith(f, StringComparison.Ordinal)))];

        Violations.ShouldBeEmpty(violations, "art is declarative vector (.cvg.json); raster images are only allowed in resources/ui and resources/fonts");
    }

    [Fact]
    public void ThirdPartyResources_ShouldNotBeCommitted()
    {
        string thirdParty = Path.Combine(ResourcesRoot, "thirdparty");
        bool hasFiles = Directory.Exists(thirdParty) && Directory.EnumerateFiles(thirdParty, "*", SearchOption.AllDirectories).Any();
        hasFiles.Should().BeFalse("third-party assets must not be committed under resources/thirdparty");
    }

    private static HashSet<string> CatalogReferencedFiles()
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ArtRoot, "catalog.cvg.json")));
        HashSet<string> files = new(StringComparer.Ordinal);
        Collect(catalog.RootElement, files);
        return files;
    }

    private static void Collect(JsonElement element, HashSet<string> files)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals("file") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        files.Add(Path.GetFullPath(Path.Combine(ArtRoot, property.Value.GetString()!)));
                    }
                    else
                    {
                        Collect(property.Value, files);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, files);
                }

                break;
            case JsonValueKind.Undefined:
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:
                break;
        }
    }
}
