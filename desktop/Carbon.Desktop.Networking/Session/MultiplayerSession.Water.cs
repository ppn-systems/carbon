// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
using Carbon.Simulation.ECS.Components;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Swimming and diving on the client (ADR-032). The predicted local player collides like the server's player, so it
/// can enter deep water instead of being stopped at the shore and snapped forward by reconciliation. The underwater
/// view follows the predicted position; the oxygen comes from the server (<see cref="WaterSession"/>).
/// </summary>
public sealed partial class MultiplayerSession
{
    private readonly SyncedWaterRules _waterRules = new();
    private readonly WaterSession _water = new();

    /// <inheritdoc/>
    public WaterView LocalWater => _water.ViewAt(_chunkWorld, this.LocalPlayerInterpolatedPosition);

    private void MakeLocalPlayerSwimmer()
    {
        if (_simulation is not null && _simulation.Entities.HasCollider(_localPlayer))
        {
            Collider2D collider = _simulation.Entities.GetCollider(_localPlayer);
            _simulation.Entities.SetCollider(_localPlayer, SwimmingColliders.AsSwimmer(collider));
        }
    }
}
