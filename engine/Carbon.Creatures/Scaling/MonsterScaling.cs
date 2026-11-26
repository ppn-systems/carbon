// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Creatures.Definitions;

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Deterministic difficulty scaling: level from distance to the world origin (first player spawn point), stats from
/// per-level curves, plus night empowerment. Pure and allocation-free.
/// </summary>
public sealed class MonsterScaling
{
    private readonly SpawningConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonsterScaling"/> class.
    /// </summary>
    /// <param name="config">Validated spawning configuration.</param>
    public MonsterScaling(SpawningConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;
    }

    /// <summary>Gets the configuration.</summary>
    public SpawningConfig Config => _config;

    /// <summary>
    /// Evaluates the continuous (fractional) level at <paramref name="distance"/> world units from the origin.
    /// Non-decreasing in distance and continuous across band boundaries.
    /// </summary>
    /// <param name="distance">Distance from the world origin.</param>
    /// <param name="bandIndex">Index of the containing band (band count beyond the last band).</param>
    /// <returns>Continuous level clamped to <c>[1, MaxLevel]</c>.</returns>
    public float EvaluateLevel(float distance, out int bandIndex)
    {
        float d = float.IsNaN(distance) ? 0f : MathF.Max(0f, distance);
        IReadOnlyList<LevelBand> bands = _config.Level.Bands;
        float level;
        bandIndex = bands.Count;

        LevelBand last = bands[^1];
        if (d >= last.EndDistance)
        {
            level = last.EndLevel + ((d - last.EndDistance) * _config.Level.LevelsPerUnitBeyond);
        }
        else
        {
            level = last.EndLevel;
            for (int i = 0; i < bands.Count; i++)
            {
                LevelBand band = bands[i];
                if (d < band.EndDistance)
                {
                    float t = (d - band.StartDistance) / (band.EndDistance - band.StartDistance);
                    float eased = t * t * (3f - (2f * t));
                    level = band.StartLevel + ((band.EndLevel - band.StartLevel) * eased);
                    bandIndex = i;
                    break;
                }
            }
        }

        return Math.Clamp(level, 1f, _config.Level.MaxLevel);
    }

    /// <summary>
    /// Computes the level and scaled stats of <paramref name="def"/> spawned <paramref name="distance"/> world units
    /// from the world origin, optionally empowered by night.
    /// </summary>
    /// <param name="distance">Distance from the world origin.</param>
    /// <param name="isNight">Whether the spawn happens at night (from the simulation time-of-day input).</param>
    /// <param name="def">Monster definition.</param>
    /// <returns>Scaled stats.</returns>
    public ScaledMonsterStats Compute(float distance, bool isNight, MonsterDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);

        int level = (int)MathF.Floor(this.EvaluateLevel(distance, out int bandIndex));
        StatCurvesConfig curves = _config.StatCurves;
        float night = isNight ? _config.Night.StatMultiplier : 1f;
        MonsterBaseStats stats = def.Stats;

        return new ScaledMonsterStats(
            level,
            bandIndex,
            MaxHealth: stats.Hp * curves.Hp.Evaluate(level) * night,
            Damage: stats.Damage * curves.Damage.Evaluate(level) * night,
            Armor: stats.Armor * curves.Armor.Evaluate(level) * night,
            MoveSpeed: stats.Speed * curves.Speed.Evaluate(level),
            isNight);
    }
}
