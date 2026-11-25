// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Noise;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Continental elevation: a domain-warped fractal field plus ridged mountain chains raised only where the
/// land is already high. Also provides the smooth relief behind height levels and the routing terrain
/// rivers follow.
/// </summary>
public sealed class ElevationStage : ITileStage, IRoutingTerrain
{
    // Legacy salts: kept so worlds generated before the stage pipeline stay identical.
    private readonly DomainWarp2D _warp;
    private readonly FractalNoise2D _ridges;

    /// <summary>Initializes a new instance of the <see cref="ElevationStage"/> class.</summary>
    /// <param name="seed">World seed.</param>
    public ElevationStage(ulong seed)
    {
        _warp = new DomainWarp2D(seed ^ 0x1234567890ABCDEFUL);
        _ridges = new FractalNoise2D(seed ^ 0x52D6E1F0A3B49C87UL);
    }

    /// <inheritdoc/>
    public bool Apply(ref TileSample sample)
    {
        float elevation = _warp.Evaluate(sample.Fx, sample.Fy, warpStrength: 0.15f, octaves: 4);
        sample.Elevation = Math.Clamp(elevation + this.RidgeLift(sample.Fx, sample.Fy, elevation, octaves: 4), 0f, 1f);
        sample.CoastElevation = sample.Elevation;

        float relief = _warp.Evaluate(sample.Fx, sample.Fy, warpStrength: 0.15f, octaves: 2);
        sample.Relief = relief + this.RidgeLift(sample.Fx, sample.Fy, relief, octaves: 2);
        return true;
    }

    /// <inheritdoc/>
    public float SampleRoutingElevation(int worldX, int worldY)
    {
        float fx = worldX * ProceduralWorldGenerator.DefaultFrequency;
        float fy = worldY * ProceduralWorldGenerator.DefaultFrequency;
        float elevation = _warp.Evaluate(fx, fy, warpStrength: 0.15f, octaves: 4);
        return Math.Clamp(elevation + this.RidgeLift(fx, fy, elevation, octaves: 4), 0f, 1f);
    }

    // Ridged multifractal mountain chains, only raised where the continent is already high.
    private float RidgeLift(float fx, float fy, float baseElevation, int octaves)
    {
        float ridge = 1f - MathF.Abs((_ridges.Evaluate(fx * 2.2f, fy * 2.2f, octaves) * 2f) - 1f);
        float mask = Math.Clamp((baseElevation - 0.50f) / 0.14f, 0f, 1f);
        return ridge * ridge * mask * 0.10f;
    }
}
