// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Primitives.Random;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Monster hit status effect proc roll (ADR-029 D7) on the secret gameplay key (ADR-026), never the world seed.
/// </summary>
public static class StatusProcRoll
{
    /// <summary>Samples the proc roll in <c>[0, 1)</c>; the proc fires when the sample is below its chance.</summary>
    /// <param name="key">Gameplay key of the tick's epoch.</param>
    /// <param name="tick">Server tick of the hit.</param>
    /// <param name="victimNetworkId">Victim network id.</param>
    /// <param name="attackerNetworkId">Attacker network id.</param>
    /// <param name="procIndex">Index of the proc in the attacker's proc list.</param>
    /// <returns>The sample.</returns>
    public static double Sample(in GameplayKey key, ulong tick, ulong victimNetworkId, ulong attackerNetworkId, int procIndex) =>
        key.Sample(GameplayDomain.StatusProc, tick, victimNetworkId, (attackerNetworkId << 8) ^ (uint)procIndex);
}
