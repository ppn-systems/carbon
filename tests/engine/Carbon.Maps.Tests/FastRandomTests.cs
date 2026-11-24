// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Random;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class FastRandomTests
{
    [Fact]
    public void FastRandom_SameSeed_ProducesExactSameSequence()
    {
        FastRandom r1 = new(1337UL);
        FastRandom r2 = new(1337UL);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(r1.NextULong(), r2.NextULong());
            Assert.Equal(r1.NextFloat(), r2.NextFloat());
        }
    }

    [Fact]
    public void FastRandom_NextFloat_AlwaysWithinUnitRange()
    {
        FastRandom rng = new(42UL);

        for (int i = 0; i < 1000; i++)
        {
            float val = rng.NextFloat();
            Assert.InRange(val, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void FastRandom_NextInt_AlwaysWithinRange()
    {
        FastRandom rng = new(999UL);

        for (int i = 0; i < 500; i++)
        {
            int val = rng.NextInt(10, 25);
            Assert.InRange(val, 10, 24);
        }
    }
}
