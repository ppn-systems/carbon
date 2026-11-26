// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Result of <see cref="MonsterScaling.Compute"/>: the effective level and stats of one monster instance.
/// </summary>
/// <param name="Level">Integer monster level.</param>
/// <param name="BandIndex">Index of the level band containing the distance (band count when beyond the last band).</param>
/// <param name="MaxHealth">Scaled maximum health.</param>
/// <param name="Damage">Scaled base damage per hit.</param>
/// <param name="Armor">Scaled armor.</param>
/// <param name="MoveSpeed">Scaled movement speed.</param>
/// <param name="IsNight">Whether night empowerment was applied.</param>
public readonly record struct ScaledMonsterStats(
    int Level,
    int BandIndex,
    float MaxHealth,
    float Damage,
    float Armor,
    float MoveSpeed,
    bool IsNight);
