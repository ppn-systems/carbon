// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Noise;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class NoiseTests
{
    [Fact]
    public void SimplexNoise2D_EvaluatesWithinBounds()
    {
        SimplexNoise2D noise = new(42UL);

        for (float y = 0f; y < 10f; y += 1.5f)
        {
            for (float x = 0f; x < 10f; x += 1.5f)
            {
                float val = noise.Evaluate(x, y);
                Assert.InRange(val, -1.0f, 1.0f);
            }
        }
    }

    [Fact]
    public void FractalNoise2D_EvaluatesWithinNormalizedRange()
    {
        FractalNoise2D noise = new(1337UL);

        for (float y = -5f; y < 5f; y += 1.0f)
        {
            for (float x = -5f; x < 5f; x += 1.0f)
            {
                float val = noise.Evaluate(x, y, octaves: 4);
                Assert.InRange(val, 0.0f, 1.0f);
            }
        }
    }

    [Fact]
    public void DomainWarp2D_SameSeed_ProducesExactMatch()
    {
        DomainWarp2D w1 = new(100UL);
        DomainWarp2D w2 = new(100UL);

        for (int i = 0; i < 20; i++)
        {
            float v1 = w1.Evaluate(i * 0.1f, i * 0.2f);
            float v2 = w2.Evaluate(i * 0.1f, i * 0.2f);
            Assert.Equal(v1, v2);
        }
    }
}
