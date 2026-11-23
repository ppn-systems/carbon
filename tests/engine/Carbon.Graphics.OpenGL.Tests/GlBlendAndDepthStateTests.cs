// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.OpenGL.States;
using FluentAssertions;
using Silk.NET.OpenGL;
using Xunit;

namespace Carbon.Graphics.OpenGL.Tests;

public sealed class GlBlendAndDepthStateTests
{
    [Fact]
    public void BlendState_PredefinedPresets_ShouldHaveExpectedFactors()
    {
        // AlphaBlend
        BlendState.AlphaBlend.ColorSource.Should().Be(BlendingFactor.One);
        BlendState.AlphaBlend.ColorDestination.Should().Be(BlendingFactor.OneMinusSrcAlpha);

        // NonPremultiplied
        BlendState.NonPremultiplied.ColorSource.Should().Be(BlendingFactor.SrcAlpha);
        BlendState.NonPremultiplied.ColorDestination.Should().Be(BlendingFactor.OneMinusSrcAlpha);

        // Additive
        BlendState.Additive.ColorSource.Should().Be(BlendingFactor.SrcAlpha);
        BlendState.Additive.ColorDestination.Should().Be(BlendingFactor.One);

        // Opaque
        BlendState.Opaque.ColorSource.Should().Be(BlendingFactor.One);
        BlendState.Opaque.ColorDestination.Should().Be(BlendingFactor.Zero);
    }

    [Fact]
    public void DepthStencilState_PredefinedPresets_ShouldHaveExpectedConfiguration()
    {
        // None
        DepthStencilState.None.DepthTestEnabled.Should().BeFalse();
        DepthStencilState.None.DepthWriteEnabled.Should().BeFalse();
        DepthStencilState.None.DepthFunction.Should().Be(DepthFunction.Always);

        // DepthRead
        DepthStencilState.DepthRead.DepthTestEnabled.Should().BeTrue();
        DepthStencilState.DepthRead.DepthWriteEnabled.Should().BeFalse();
        DepthStencilState.DepthRead.DepthFunction.Should().Be(DepthFunction.Lequal);

        // DepthReadWrite
        DepthStencilState.DepthReadWrite.DepthTestEnabled.Should().BeTrue();
        DepthStencilState.DepthReadWrite.DepthWriteEnabled.Should().BeTrue();
        DepthStencilState.DepthReadWrite.DepthFunction.Should().Be(DepthFunction.Lequal);
    }

    [Fact]
    public void StateRecords_ValueEquality_ShouldWorkAsExpected()
    {
        BlendState custom1 = new(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero);
        BlendState custom2 = new(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero);

        custom1.Should().Be(custom2);
        (custom1 == custom2).Should().BeTrue();

        DepthStencilState ds1 = new(true, true, DepthFunction.Less);
        DepthStencilState ds2 = new(true, true, DepthFunction.Less);

        ds1.Should().Be(ds2);
        (ds1 == ds2).Should().BeTrue();
    }
}
