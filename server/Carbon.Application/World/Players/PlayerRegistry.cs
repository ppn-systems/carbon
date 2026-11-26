// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Players;

/// <summary>
/// The players in the world, by connection and by network id (audit SOLID-1). Only touched under the world lock.
/// </summary>
internal sealed class PlayerRegistry
{
    /// <summary>Gets the players keyed by connection id.</summary>
    public Dictionary<ulong, PlayerState> ByConnection { get; } = new();

    /// <summary>Gets the connection id of each player's network id.</summary>
    public Dictionary<ulong, ulong> ConnectionByNetworkId { get; } = new();

    /// <summary>Gets the player whose entity has <paramref name="networkId"/>.</summary>
    public bool TryGetByNetworkId(ulong networkId, [NotNullWhen(true)] out PlayerState? state)
    {
        if (this.ConnectionByNetworkId.TryGetValue(networkId, out ulong connectionId) &&
            this.ByConnection.TryGetValue(connectionId, out state))
        {
            return true;
        }

        state = null;
        return false;
    }

    /// <summary>Gets the player controlling <paramref name="entity"/>.</summary>
    public bool TryGetByEntity(WorldSimulation simulation, Entity entity, [NotNullWhen(true)] out PlayerState? state)
    {
        if (simulation.TryGetNetworkIdentity(entity, out NetworkIdentity identity) &&
            this.TryGetByNetworkId(identity.NetworkId, out state) &&
            state.Entity == entity)
        {
            return true;
        }

        state = null;
        return false;
    }
}
