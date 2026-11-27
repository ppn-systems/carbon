// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Hydrology;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// The local player in the water (ADR-032): the swim state, tier and depth predicted from the terrain under the
/// predicted position (so the underwater view follows the player at once), and the oxygen the server last sent.
/// </summary>
/// <param name="SwimState">Walking, wading, swimming or diving.</param>
/// <param name="Tier">The depth tier under the player.</param>
/// <param name="Depth">The water depth under the player (see <see cref="WaterDepth"/>); 0 on land.</param>
/// <param name="OxygenSeconds">Oxygen left, in seconds of diving.</param>
/// <param name="MaxOxygenSeconds">The full oxygen pool in seconds; 0 before the server has sent it.</param>
public readonly record struct WaterView(SwimState SwimState, WaterDepthTier Tier, float Depth, float OxygenSeconds, float MaxOxygenSeconds)
{
    /// <summary>Gets the oxygen left as a fraction of the full pool (1 before the server has sent it).</summary>
    public float OxygenFraction => this.MaxOxygenSeconds > 0f ? System.Math.Clamp(this.OxygenSeconds / this.MaxOxygenSeconds, 0f, 1f) : 1f;

    /// <summary>Gets whether the oxygen bar is shown: while diving, or until the breath has refilled.</summary>
    public bool ShowsOxygen => this.MaxOxygenSeconds > 0f && (this.SwimState == SwimState.Diving || this.OxygenFraction < 0.999f);
}
