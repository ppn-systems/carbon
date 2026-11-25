// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Noise;
using Carbon.Primitives.Math;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Temperature (a latitude band, turbulence and altitude cooling) and moisture (rainfall noise plus coastal
/// humidity).
/// </summary>
public sealed class ClimateStage : ITileStage
{
    private readonly ulong _seed;
    private readonly FractalNoise2D _temperature;
    private readonly FractalNoise2D _moisture;

    /// <summary>Initializes a new instance of the <see cref="ClimateStage"/> class.</summary>
    /// <param name="seed">World seed.</param>
    public ClimateStage(ulong seed)
    {
        _seed = seed;
        _temperature = new FractalNoise2D(seed ^ 0xFEDCBA0987654321UL);
        _moisture = new FractalNoise2D(seed ^ 0xCAFEBABE1337BEEFUL);
    }

    /// <inheritdoc/>
    public bool Apply(ref TileSample sample)
    {
        float starter = WorldProgression.GetStarterWeight(_seed, sample.WorldX, sample.WorldY);

        // Latitude band along Y (~4750-tile wavelength at default frequency), plus turbulence.
        float latGradient = 0.50f + (0.35f * DeterministicMath.Sin(sample.Fy * 1.65f));
        latGradient += (0.50f - latGradient) * (starter * 0.55f);

        float turbulence = (_temperature.Evaluate(sample.Fx * 0.45f, sample.Fy * 0.45f, octaves: 3) - 0.5f) * 0.22f;
        float temperature = Math.Clamp(latGradient + turbulence, 0f, 1f);

        // Adiabatic lapse rate: highlands and mountain peaks cool progressively above low plains.
        if (sample.Elevation > 0.48f)
        {
            temperature = Math.Clamp(temperature - ((sample.Elevation - 0.48f) * 0.62f), 0f, 1f);
        }

        float rawMoisture = _moisture.Evaluate(sample.Fx * 0.50f, sample.Fy * 0.50f, octaves: 3);
        if (sample.CoastElevation < 0.52f)
        {
            rawMoisture += (0.52f - sample.CoastElevation) * 0.45f; // coastal humidity
        }

        rawMoisture += (0.45f - rawMoisture) * (starter * 0.40f);

        sample.Temperature = temperature;
        sample.RawMoisture = rawMoisture;
        sample.Moisture = Math.Clamp(rawMoisture, 0f, 1f);
        return true;
    }
}
