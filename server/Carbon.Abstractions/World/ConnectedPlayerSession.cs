// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Abstractions.World;

/// <summary>
/// Represents an active player session bound to an entity in the authoritative world simulation.
/// </summary>
/// <param name="SessionId">Unique server session ID.</param>
/// <param name="ConnectionId">Underlying transport connection identifier.</param>
/// <param name="AccountId">Authenticated account ID.</param>
/// <param name="CharacterId">Selected character ID.</param>
/// <param name="CharacterName">Character display name.</param>
/// <param name="PlayerNetworkId">Authoritative entity network ID in the simulation.</param>
/// <param name="SelectedHotbarSlot">Active hotbar slot index.</param>
/// <param name="SelectedHarvestTier">Tool harvest tier granted by the selected hotbar tool.</param>
public sealed record ConnectedPlayerSession(
    ulong SessionId,
    ulong ConnectionId,
    Guid AccountId,
    Guid CharacterId,
    string CharacterName,
    ulong PlayerNetworkId,
    byte SelectedHotbarSlot,
    byte SelectedHarvestTier);
