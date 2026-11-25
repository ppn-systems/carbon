// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Noise;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Continental elevation built from domain-warped continentalness, erosion dampening, Musgrave ridged mountains,
/// and a gentle Starter Heartland modulation near the origin. Also supplies smooth coarse relief and world-frequency routing elevation.
/// </summary>
public sealed class ElevationStage : ITileStage, IRoutingTerrain
{
    private readonly ulong _seed;
    private readonly float _frequency;
    private readonly DomainWarp2D _warp;
    private readonly FractalNoise2D _erosion;
    private readonly FractalNoise2D _ridges;

    /// <summary>Initializes a new instance of the <see cref="ElevationStage"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="frequency">Macro spatial noise frequency configured for the world.</param>
    public ElevationStage(ulong seed, float frequency = ProceduralWorldGenerator.DefaultFrequency)
    {
        _seed = seed;
        _frequency = frequency;
        _warp = new DomainWarp2D(seed ^ 0x1234567890ABCDEFUL);
        _erosion = new FractalNoise2D(seed ^ 0x3A9C7E51B204D86FUL);
        _ridges = new FractalNoise2D(seed ^ 0x52D6E1F0A3B49C87UL);
    }

    /// <inheritdoc/>
    public bool Apply(ref TileSample sample)
    {
        this.SynthesizeElevation(sample.WorldX, sample.WorldY, sample.Fx, sample.Fy, out float elevation, out float relief);
        sample.Elevation = elevation;
        sample.CoastElevation = elevation;
        sample.Relief = relief;
        return true;
    }

    /// <inheritdoc/>
    public float SampleRoutingElevation(int worldX, int worldY)
    {
        float fx = worldX * _frequency;
        float fy = worldY * _frequency;
        this.SynthesizeElevation(worldX, worldY, fx, fy, out float elevation, out _);
        return elevation;
    }

    private void SynthesizeElevation(int worldX, int worldY, float fx, float fy, out float elevation, out float relief)
    {
        float cont = _warp.EvaluateWithCoarse(fx, fy, warpStrength: 0.16f, fullOctaves: 4, coarseOctaves: 2, out float coarseCont);

        // Nudge the starter heartland gently toward fertile lowlands without erasing rivers or scenic ridges.
        float starter = WorldProgression.GetStarterWeight(_seed, worldX, worldY) * 0.55f;
        cont += (0.46f - cont) * starter;
        coarseCont += (0.46f - coarseCont) * starter;

        float fineLift = 0f;
        float coarseLift = 0f;
        if (cont > 0.44f || coarseCont > 0.44f)
        {
            float erosion = _erosion.Evaluate(fx * 0.65f, fy * 0.65f, octaves: 2);
            float ridge = _ridges.EvaluateRidged(fx * 2.1f, fy * 2.1f, fullOctaves: 4, coarseOctaves: 2, out float coarseRidge);
            float mountainFactor = 1f - (erosion * 0.50f);

            float fineMask = Math.Clamp((cont - 0.44f) / 0.16f, 0f, 1f);
            fineLift = ridge * fineMask * mountainFactor * 0.24f;

            float coarseMask = Math.Clamp((coarseCont - 0.44f) / 0.16f, 0f, 1f);
            coarseLift = coarseRidge * coarseMask * mountainFactor * 0.20f;
        }

        elevation = Math.Clamp(cont + fineLift, 0f, 1f);
        relief = Math.Clamp(coarseCont + coarseLift, 0f, 1f);
    }
}
