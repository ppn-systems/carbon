// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Carbon.Creatures.Serialization;

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Root model of <c>resources/config/spawning.json</c>: distance-based level curve, per-level stat curves,
/// night empowerment, spawner density / timer settings, and biome spawn tables.
/// </summary>
public sealed class SpawningConfig
{
    /// <summary>Gets the schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Gets the radius around the world origin (player spawn) where monsters never spawn.</summary>
    public float SafeZoneRadius { get; set; }

    /// <summary>Gets the spawn / AoI density cell size in world units.</summary>
    public float CellSize { get; set; } = 512f;

    /// <summary>Gets the radius, in cells, around each spawn anchor that the spawner keeps populated.</summary>
    public int ActivationRadiusCells { get; set; } = 2;

    /// <summary>Gets the minimum distance from any anchor at which a monster may appear.</summary>
    public float MinSpawnDistanceFromAnchor { get; set; }

    /// <summary>
    /// Gets how many candidate positions a cell samples per spawn opportunity before giving up until its next
    /// interval (rejections: safe zone, anchor distance, blocked tile, no monster eligible at the level).
    /// </summary>
    public int PlacementAttempts { get; set; } = 6;

    /// <summary>Gets the daytime maximum number of live spawned monsters per cell.</summary>
    public int BaseCellCap { get; set; } = 3;

    /// <summary>Gets the minimum ticks between two spawn attempts in the same cell.</summary>
    public uint SpawnIntervalTicks { get; set; } = 100;

    /// <summary>Gets the ticks a cell waits after one of its monsters dies before it may spawn again.</summary>
    public uint RespawnDelayTicks { get; set; } = 600;

    /// <summary>Gets how often (in ticks) the spawner evaluates cells.</summary>
    public uint ScanIntervalTicks { get; set; } = 10;

    /// <summary>Gets the collider layer assigned to spawned monsters.</summary>
    public uint MonsterLayer { get; set; } = 4;

    /// <summary>Gets the collider layer mask monsters target.</summary>
    public uint TargetLayerMask { get; set; } = 2;

    /// <summary>Gets the daytime chance that a spawn is upgraded to its elite variant.</summary>
    public float DayEliteChance { get; set; }

    /// <summary>Gets the distance-to-level curve.</summary>
    public LevelCurveConfig Level { get; set; } = new();

    /// <summary>Gets the per-level stat curves.</summary>
    public StatCurvesConfig StatCurves { get; set; } = new();

    /// <summary>Gets the night empowerment settings.</summary>
    public NightConfig Night { get; set; } = new();

    /// <summary>Gets the biome spawn tables.</summary>
    public IReadOnlyList<SpawnTable> SpawnTables { get; set; } = [];

    /// <summary>
    /// Parses and validates a <c>spawning.json</c> document.
    /// </summary>
    /// <param name="json">JSON document text.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="JsonException">Thrown for malformed JSON, unknown members, or invalid enum names.</exception>
    public static SpawningConfig FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SpawningConfig config = JsonSerializer.Deserialize(json, CreaturesJsonContext.Default.SpawningConfig)
            ?? throw new JsonException("spawning.json is empty.");
        config.Validate();
        return config;
    }

    /// <summary>
    /// Validates internal consistency (contiguous monotonic bands, probabilities, positive timers).
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a value is inconsistent.</exception>
    public void Validate()
    {
        if (this.SafeZoneRadius < 0f || this.CellSize <= 0f || this.ActivationRadiusCells < 0 || this.BaseCellCap < 0 ||
            this.PlacementAttempts < 1)
        {
            throw new ArgumentException("Spawner safe zone, cell size, activation radius, and cap must be non-negative; placement attempts must be positive.");
        }

        if (this.ScanIntervalTicks == 0 || this.SpawnIntervalTicks == 0)
        {
            throw new ArgumentException("Spawner scan and spawn intervals must be positive.");
        }

        if (!IsProbability(this.DayEliteChance) || !IsProbability(this.Night.EliteChance))
        {
            throw new ArgumentException("Elite chances must lie in [0, 1].");
        }

        if (this.Night.StatMultiplier < 1f || this.Night.DensityMultiplier < 1f)
        {
            throw new ArgumentException("Night stat and density multipliers must be >= 1 (night is never weaker).");
        }

        IReadOnlyList<LevelBand> bands = this.Level.Bands;
        if (bands.Count == 0 || bands[0].StartDistance != 0f || this.Level.MaxLevel < 1 || this.Level.LevelsPerUnitBeyond < 0f)
        {
            throw new ArgumentException("Level curve needs at least one band starting at distance 0 and a positive max level.");
        }

        for (int i = 0; i < bands.Count; i++)
        {
            LevelBand band = bands[i];
            if (band.EndDistance <= band.StartDistance || band.EndLevel < band.StartLevel || band.StartLevel < 1f)
            {
                throw new ArgumentException($"Level band '{band.Name}' must have increasing distance and non-decreasing level >= 1.");
            }

            if (i > 0 && (band.StartDistance != bands[i - 1].EndDistance || band.StartLevel != bands[i - 1].EndLevel))
            {
                throw new ArgumentException($"Level band '{band.Name}' must start where '{bands[i - 1].Name}' ends (distance and level).");
            }
        }

        ValidateCurve(this.StatCurves.Hp, "hp");
        ValidateCurve(this.StatCurves.Damage, "damage");
        ValidateCurve(this.StatCurves.Armor, "armor");
        ValidateCurve(this.StatCurves.Speed, "speed");

        for (int t = 0; t < this.SpawnTables.Count; t++)
        {
            SpawnTable table = this.SpawnTables[t];
            if (!Enum.IsDefined(table.Biome))
            {
                throw new ArgumentException($"Spawn table #{t} references unknown biome '{table.Biome}'.");
            }

            for (int e = 0; e < table.Entries.Count; e++)
            {
                SpawnTableEntry entry = table.Entries[e];
                if (string.IsNullOrWhiteSpace(entry.Monster) || entry.Weight < 0f || entry.NightWeight < 0f)
                {
                    throw new ArgumentException($"Spawn table '{table.Biome}' entry #{e} is invalid.");
                }
            }
        }
    }

    private static bool IsProbability(float value) => value is >= 0f and <= 1f;

    private static void ValidateCurve(StatCurve curve, string name)
    {
        if (curve is null || curve.PerLevel < 0f || curve.Exponent <= 0f || curve.MaxMultiplier < 1f)
        {
            throw new ArgumentException($"Stat curve '{name}' must have perLevel >= 0, exponent > 0, maxMultiplier >= 1.");
        }
    }
}
