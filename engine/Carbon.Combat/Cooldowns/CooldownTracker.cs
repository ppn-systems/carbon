// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Combat.Cooldowns;

/// <summary>
/// Zero-allocation tick-based cooldown tracker for an actor's abilities or actions.
/// </summary>
public sealed class CooldownTracker
{
    private struct CooldownEntry
    {
        public uint AbilityId;
        public ulong ReadyAtTick;
    }

    private CooldownEntry[] _entries = new CooldownEntry[16];
    private int _count;

    /// <summary>
    /// Starts or resets a cooldown for <paramref name="abilityId"/> lasting <paramref name="durationTicks"/> ticks.
    /// </summary>
    /// <param name="abilityId">Ability identifier.</param>
    /// <param name="currentTick">Current simulation tick index.</param>
    /// <param name="durationTicks">Cooldown length in ticks.</param>
    public void StartCooldown(uint abilityId, ulong currentTick, uint durationTicks)
    {
        ulong readyAt = currentTick + durationTicks;
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].AbilityId == abilityId)
            {
                _entries[i].ReadyAtTick = readyAt;
                return;
            }
        }

        if (_count >= _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        _entries[_count++] = new CooldownEntry
        {
            AbilityId = abilityId,
            ReadyAtTick = readyAt
        };
    }

    /// <summary>
    /// Determines whether <paramref name="abilityId"/> is off cooldown at <paramref name="currentTick"/>.
    /// </summary>
    /// <param name="abilityId">Ability identifier.</param>
    /// <param name="currentTick">Current simulation tick index.</param>
    /// <returns><see langword="true"/> if ready; otherwise, <see langword="false"/>.</returns>
    public bool IsReady(uint abilityId, ulong currentTick) =>
        this.GetRemainingTicks(abilityId, currentTick) == 0;

    /// <summary>
    /// Gets the remaining cooldown ticks for <paramref name="abilityId"/> at <paramref name="currentTick"/>.
    /// </summary>
    /// <param name="abilityId">Ability identifier.</param>
    /// <param name="currentTick">Current simulation tick index.</param>
    /// <returns>Remaining ticks (0 when ready).</returns>
    public uint GetRemainingTicks(uint abilityId, ulong currentTick)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].AbilityId == abilityId)
            {
                ulong readyAt = _entries[i].ReadyAtTick;
                return readyAt > currentTick ? (uint)(readyAt - currentTick) : 0U;
            }
        }

        return 0U;
    }

    /// <summary>
    /// Clears all tracked cooldowns.
    /// </summary>
    public void ResetAll() => _count = 0;
}
