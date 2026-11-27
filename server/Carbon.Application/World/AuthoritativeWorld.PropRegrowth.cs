// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Players;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Props;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    /// <summary>
    /// Ticks until a destroyed prop of <paramref name="type"/> on a world tile comes back (ADR-040): starter forage in the
    /// spawn clearing after its short delay, flora after its <c>props.json</c> delay, stone and ore never (0).
    /// </summary>
    private ulong PropRegrowDelayTicksLocked(MapPropType type, int tileX, int tileY) =>
        _propRegrowth.DelayTicks(type, SpawnClearingStage.IsInClearing(_chunkWorld.SpawnTile, tileX, tileY), this.TickRate);

    /// <summary>Whether a living player stands on the world tile, so a prop due there waits instead of growing into them.</summary>
    private bool IsPropTileBlockedLocked(int tileX, int tileY)
    {
        foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
        {
            PlayerState state = kvp.Value;
            if (!_simulation.Entities.IsAlive(state.Entity) || !_simulation.Entities.HasTransform(state.Entity))
            {
                continue;
            }

            Vector2 position = _simulation.Entities.GetTransform(state.Entity).Position;
            if ((int)MathF.Floor(position.X / this.TileSize) == tileX && (int)MathF.Floor(position.Y / this.TileSize) == tileY)
            {
                return true;
            }
        }

        return false;
    }
}
