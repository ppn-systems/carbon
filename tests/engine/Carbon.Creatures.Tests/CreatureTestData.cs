// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Maps.Biomes;

namespace Carbon.Creatures.Tests;

/// <summary>Loads the shipped <c>resources/config</c> creature files once per test run.</summary>
internal static class CreatureTestData
{
    private static readonly Lazy<string> s_configDir = new(FindConfigDirectory);

    public static string MonstersJson => File.ReadAllText(Path.Combine(s_configDir.Value, "monsters.json"));

    public static string SpawningJson => File.ReadAllText(Path.Combine(s_configDir.Value, "spawning.json"));

    public static MonsterCatalog LoadCatalog() => MonsterCatalog.FromJson(MonstersJson);

    public static SpawningConfig LoadSpawning() => SpawningConfig.FromJson(SpawningJson);

    private static string FindConfigDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "resources", "config");
            if (File.Exists(Path.Combine(candidate, "monsters.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("resources/config/monsters.json not found above the test output directory.");
    }
}

/// <summary>Deterministic biome sampler returning one biome everywhere.</summary>
internal sealed class FixedBiomeSampler(BiomeType biome) : IBiomeSampler
{
    public BiomeType SampleBiome(Vector2 worldPosition) => biome;
}
