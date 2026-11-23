// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Sprites;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Batching.Tests;

public sealed class SpriteSortKeyTests
{
    [Fact]
    public void Calculate_YSorted_ShouldOrderBasedOnLayerAndFootY()
    {
        float key1 = SpriteSortKey.Calculate(DepthSortMode.YSorted, footY: 100f, layer: 0, depth: 0f, textureHandle: 1);
        float key2 = SpriteSortKey.Calculate(DepthSortMode.YSorted, footY: 200f, layer: 0, depth: 0f, textureHandle: 1);
        float keyLayer1 = SpriteSortKey.Calculate(DepthSortMode.YSorted, footY: 100f, layer: 1, depth: 0f, textureHandle: 1);

        key1.Should().BeLessThan(key2);
        key2.Should().BeLessThan(keyLayer1);
    }

    [Fact]
    public void Calculate_BackToFront_ShouldOrderBasedOnLayerAndDepth()
    {
        float keyBack = SpriteSortKey.Calculate(DepthSortMode.BackToFront, footY: 0f, layer: 0, depth: 10f, textureHandle: 1);
        float keyFront = SpriteSortKey.Calculate(DepthSortMode.BackToFront, footY: 0f, layer: 0, depth: 1f, textureHandle: 1);

        keyFront.Should().BeLessThan(keyBack);
    }

    [Fact]
    public void Calculate_Immediate_ShouldAlwaysReturnZero()
    {
        float key = SpriteSortKey.Calculate(DepthSortMode.Immediate, footY: 150f, layer: 3, depth: 0.8f, textureHandle: 99);
        key.Should().Be(0f);
    }

    [Fact]
    public void Calculate_UnknownMode_ShouldReturnTextureHandle()
    {
        float key = SpriteSortKey.Calculate((DepthSortMode)99, footY: 150f, layer: 3, depth: 0.8f, textureHandle: 42);
        key.Should().Be(42f);
    }
}
