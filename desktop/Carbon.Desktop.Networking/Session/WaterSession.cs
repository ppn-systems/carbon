// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// The local player in the water (ADR-032): the oxygen the server last sent (<see cref="PlayerOxygenSync"/>, owner
/// only) and the swim state and depth predicted from the client-generated terrain under the predicted position.
/// </summary>
internal sealed class WaterSession
{
    private readonly Lock _gate = new();
    private float _oxygenSeconds;
    private float _maxOxygenSeconds;

    /// <summary>Takes the server's oxygen; sent only when it changes.</summary>
    /// <param name="packet">The sync.</param>
    public void OnPlayerOxygenSync(PlayerOxygenSync packet)
    {
        System.ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            _oxygenSeconds = packet.OxygenMs / 1000f;
            _maxOxygenSeconds = packet.MaxOxygenMs / 1000f;
        }
    }

    /// <summary>Gets the water view at the local player's predicted position.</summary>
    /// <param name="world">The streaming world, or <see langword="null"/> before entering it.</param>
    /// <param name="position">The predicted position.</param>
    /// <returns>The view; dry land without a world, but always with the last oxygen the server sent.</returns>
    public WaterView ViewAt(ChunkWorld2D? world, Vector2 position)
    {
        float oxygen;
        float max;
        lock (_gate)
        {
            oxygen = _oxygenSeconds;
            max = _maxOxygenSeconds;
        }

        if (world is null)
        {
            return new WaterView(SwimState.Walking, WaterDepthTier.Dry, 0f, oxygen, max);
        }

        WaterDepthTier tier = world.GetWaterTierAt(position);
        return new WaterView(WaterRules.SwimStateOf(tier), tier, world.GetWaterDepthAt(position), oxygen, max);
    }
}
