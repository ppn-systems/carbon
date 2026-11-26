// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;

namespace Carbon.Creatures;

/// <summary>
/// Validated creature configuration documents (<c>monsters.json</c> + <c>spawning.json</c>) loaded server-side from
/// <c>resources/config</c>. Clients never load these files.
/// </summary>
/// <param name="Catalog">Monster catalog.</param>
/// <param name="Spawning">Spawning / scaling configuration.</param>
public sealed record CreatureContent(MonsterCatalog Catalog, SpawningConfig Spawning)
{
    /// <summary>Monster catalog document file name.</summary>
    public const string MonstersFileName = "monsters.json";

    /// <summary>Spawning configuration document file name.</summary>
    public const string SpawningFileName = "spawning.json";

    /// <summary>
    /// Parses both documents from JSON text.
    /// </summary>
    /// <param name="monstersJson">Content of <c>monsters.json</c>.</param>
    /// <param name="spawningJson">Content of <c>spawning.json</c>.</param>
    /// <returns>The validated content.</returns>
    public static CreatureContent FromJson(string monstersJson, string spawningJson) =>
        new(MonsterCatalog.FromJson(monstersJson), SpawningConfig.FromJson(spawningJson));

    /// <summary>
    /// Loads both documents from <paramref name="configDirectory"/>.
    /// </summary>
    /// <param name="configDirectory">Directory containing the config documents.</param>
    /// <returns>The validated content.</returns>
    public static CreatureContent LoadFromDirectory(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        return FromJson(
            File.ReadAllText(Path.Combine(configDirectory, MonstersFileName)),
            File.ReadAllText(Path.Combine(configDirectory, SpawningFileName)));
    }

    /// <summary>
    /// Loads from the first <c>resources/config</c> directory containing <c>monsters.json</c> found walking up from
    /// <see cref="AppContext.BaseDirectory"/> (published hosts copy it next to the binary; tests resolve the repo copy).
    /// </summary>
    /// <returns>The validated content.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no config directory is found.</exception>
    public static CreatureContent LoadDefault()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "resources", "config");
            if (File.Exists(Path.Combine(candidate, MonstersFileName)))
            {
                return LoadFromDirectory(candidate);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"resources/config/{MonstersFileName} not found above '{AppContext.BaseDirectory}'.");
    }
}
