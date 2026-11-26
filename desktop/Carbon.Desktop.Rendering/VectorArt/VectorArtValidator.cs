// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Summary returned after validating external <c>.cvg.json</c> vector assets.
/// </summary>
/// <param name="CharactersValidated">Count of character presets validated.</param>
/// <param name="MonstersValidated">Count of monster rigs validated.</param>
/// <param name="NpcsValidated">Count of NPC rigs validated.</param>
/// <param name="ItemsValidated">Count of item blueprints validated.</param>
/// <param name="PropsValidated">Count of natural prop/tree blueprints validated.</param>
/// <param name="BiomesValidated">Count of biome blueprints validated.</param>
/// <param name="StructuresValidated">Count of structure blueprints validated.</param>
/// <param name="RasterImagesFound">Count of unexpected raster image files (<c>.png</c>/<c>.jpg</c>) found inside the art folder.</param>
public readonly record struct VectorArtValidationSummary(
    int CharactersValidated,
    int MonstersValidated,
    int NpcsValidated,
    int ItemsValidated,
    int PropsValidated,
    int BiomesValidated,
    int StructuresValidated,
    int RasterImagesFound);

/// <summary>
/// Locates and validates external declarative vector blueprints under <c>resources/art/</c> without needing OpenGL.
/// </summary>
public static class VectorArtValidator
{
    /// <summary>Searches application output and repository ancestors for <c>resources/art/catalog.cvg.json</c>.</summary>
    public static string? TryFindArtRootDirectory(string? hintDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(hintDirectory) && File.Exists(Path.Combine(hintDirectory, "catalog.cvg.json")))
        {
            return Path.GetFullPath(hintDirectory);
        }

        string[] startDirs = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        for (int s = 0; s < startDirs.Length; s++)
        {
            DirectoryInfo? cursor = new(startDirs[s]);
            while (cursor is not null)
            {
                string direct = Path.Combine(cursor.FullName, "resources", "art");
                if (File.Exists(Path.Combine(direct, "catalog.cvg.json")))
                {
                    return direct;
                }

                cursor = cursor.Parent;
            }
        }

        return null;
    }

    /// <summary>Loads, compiles, and verifies all <c>.cvg.json</c> assets in <paramref name="artRoot"/>.</summary>
    public static VectorArtValidationSummary ValidateDirectory(string artRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artRoot);

        CarbonVectorArtLibrary lib = CarbonVectorArtLibrary.LoadFromDirectory(artRoot);
        int rasterCount = Directory.GetFiles(artRoot, "*.png", SearchOption.AllDirectories).Length +
            Directory.GetFiles(artRoot, "*.jpg", SearchOption.AllDirectories).Length;

        return new VectorArtValidationSummary(
            lib.CharacterCount,
            lib.MonsterCount,
            lib.NpcCount,
            lib.ItemCount,
            lib.PropCount,
            lib.BiomeCount,
            lib.StructureCount,
            rasterCount);
    }
}
