// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Economy.Trading;

/// <summary>
/// Restock epochs from server wall-clock time (ADR-025 D13): <c>epoch = ⌊(unix + jitter(key)) / period⌋</c>. Wall-clock
/// time survives restarts (the tick counter does not), and the per-trader jitter keeps villages from restocking together.
/// </summary>
public static class TraderClock
{
    /// <summary>Gets the restock offset of a trader, in [0, jitter].</summary>
    public static long JitterOf(ulong traderKey, TraderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.RestockJitterSeconds == 0 ? 0 : (long)(Mix(traderKey) % (ulong)(profile.RestockJitterSeconds + 1));
    }

    /// <summary>Gets the restock epoch at <paramref name="unixSeconds"/>.</summary>
    public static long EpochAt(ulong traderKey, TraderProfile profile, long unixSeconds)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Math.DivRem(unixSeconds + JitterOf(traderKey, profile), profile.RestockSeconds, out long rem) - (rem < 0 ? 1 : 0);
    }

    /// <summary>Gets the whole seconds until the next restock (1..period).</summary>
    public static int SecondsUntilRestock(ulong traderKey, TraderProfile profile, long unixSeconds)
    {
        long epoch = EpochAt(traderKey, profile, unixSeconds);
        long next = ((epoch + 1) * profile.RestockSeconds) - JitterOf(traderKey, profile);
        return (int)Math.Clamp(next - unixSeconds, 1, profile.RestockSeconds);
    }

    internal static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
