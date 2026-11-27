// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Primitives.Random;

namespace Carbon.Items.Progression;

/// <summary>
/// Deterministic chance rolls for combat (critical hits, evasion). A roll is a keyed PRF (ADR-026) of the server's secret
/// roll key, the tick, the actor and a per-character nonce, so a replay with the same key produces the same outcome, tests
/// need no random source, and a client that knows the world seed, the tick and its own nonce still cannot predict it.
/// </summary>
public static class CombatRoll
{
    /// <summary>Domain of critical hit rolls.</summary>
    public const ulong CriticalDomain = GameplayDomain.Critical;

    /// <summary>Domain of evasion rolls (keeps them independent from crit rolls on the same nonce).</summary>
    public const ulong EvasionDomain = GameplayDomain.Evasion;

    /// <summary>Returns whether one roll succeeds with probability <paramref name="chance"/>.</summary>
    /// <param name="chance">Success probability (<c>&lt;= 0</c> never, <c>&gt;= 1</c> always).</param>
    /// <param name="key">Secret roll key of the tick's epoch (server-only, never the world seed).</param>
    /// <param name="domain">Roll kind (<see cref="CriticalDomain"/> or <see cref="EvasionDomain"/>).</param>
    /// <param name="tick">Simulation tick of the roll.</param>
    /// <param name="actorId">Rolling character (network id).</param>
    /// <param name="nonce">Monotonic per-character counter.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool Roll(float chance, in GameplayKey key, ulong domain, ulong tick, ulong actorId, uint nonce)
    {
        if (!(chance > 0f))
        {
            return false;
        }

        return chance >= 1f || key.Sample(domain, tick, actorId, nonce) < chance;
    }

    /// <summary>Applies the critical multiplier to <paramref name="damage"/> when <paramref name="isCritical"/>.</summary>
    /// <param name="damage">Damage before the crit.</param>
    /// <param name="isCritical">Whether the hit crit.</param>
    /// <param name="critDamage">Critical damage multiplier (values below 1 are treated as 1).</param>
    /// <returns>Final damage.</returns>
    public static float ApplyCritical(float damage, bool isCritical, float critDamage) =>
        isCritical ? damage * (critDamage < 1f ? 1f : critDamage) : damage;
}
