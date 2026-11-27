// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>
/// Deterministic chance rolls for combat (critical hits, evasion). A roll is a pure function of a per-character nonce
/// and a salt, so a replayed tick produces the same outcome and tests need no random source.
/// </summary>
public static class CombatRoll
{
    /// <summary>Salt for critical hit rolls.</summary>
    public const uint CriticalSalt = 0U;

    /// <summary>Salt for evasion rolls (keeps them independent from crit rolls on the same nonce).</summary>
    public const uint EvasionSalt = 0x5BD1E995U;

    /// <summary>Returns whether a roll of <paramref name="nonce"/> succeeds with probability <paramref name="chance"/>.</summary>
    /// <param name="chance">Success probability (<c>&lt;= 0</c> never, <c>&gt;= 1</c> always).</param>
    /// <param name="nonce">Monotonic per-character counter.</param>
    /// <param name="salt">Roll kind salt.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool Roll(float chance, uint nonce, uint salt = CriticalSalt)
    {
        if (!(chance > 0f))
        {
            return false;
        }

        uint h = (nonce ^ salt) * 0x9E3779B1U;
        h ^= h >> 15;
        h *= 0x85EBCA6BU;
        h ^= h >> 13;
        float roll = (h & 0xFFFFU) / 65536f;
        return roll < chance;
    }

    /// <summary>Applies the critical multiplier to <paramref name="damage"/> when <paramref name="isCritical"/>.</summary>
    /// <param name="damage">Damage before the crit.</param>
    /// <param name="isCritical">Whether the hit crit.</param>
    /// <param name="critDamage">Critical damage multiplier (values below 1 are treated as 1).</param>
    /// <returns>Final damage.</returns>
    public static float ApplyCritical(float damage, bool isCritical, float critDamage) =>
        isCritical ? damage * (critDamage < 1f ? 1f : critDamage) : damage;
}
