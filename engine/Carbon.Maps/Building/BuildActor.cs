// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Server-resolved identity and position of the player performing a building mutation. Never taken from the client.
/// </summary>
/// <param name="ActorId">Player id.</param>
/// <param name="TileX">World tile X the player stands on.</param>
/// <param name="TileY">World tile Y the player stands on.</param>
/// <param name="ToolTier">Tier of the tool the player holds (0 = bare hands).</param>
public readonly record struct BuildActor(ulong ActorId, int TileX, int TileY, byte ToolTier);
