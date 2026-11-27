// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Application.World.Players;
using Carbon.Application.World.Water;
using Carbon.Maps.Hydrology;

namespace Carbon.Application.World;

/// <summary>
/// Swimming, diving and oxygen (ADR-032 D10–D12): the read side for tests and tools. The rules live in
/// <see cref="OxygenService"/>.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    /// <summary>Gets the water movement and oxygen rules the world runs on (<c>water.json</c>).</summary>
    public WaterRules WaterRules => _chunkWorld.WaterRules;

    /// <summary>Gets a player's oxygen and swim state, for tests and tools.</summary>
    /// <param name="connectionId">The player's connection.</param>
    /// <param name="oxygenSeconds">Oxygen left in seconds.</param>
    /// <param name="swimState">The swim state as of the last tick.</param>
    /// <returns><c>false</c> when no such player is in the world.</returns>
    public bool TryGetOxygen(ulong connectionId, out float oxygenSeconds, out SwimState swimState)
    {
        lock (_gate)
        {
            if (_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                oxygenSeconds = _oxygen.OxygenOf(state);
                swimState = state.SwimState;
                return true;
            }

            oxygenSeconds = 0f;
            swimState = SwimState.Walking;
            return false;
        }
    }
}
