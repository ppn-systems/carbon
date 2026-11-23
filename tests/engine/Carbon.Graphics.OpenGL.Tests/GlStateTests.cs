// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.OpenGL.States;
using FluentAssertions;
using Silk.NET.OpenGL;
using Xunit;

namespace Carbon.Graphics.OpenGL.Tests;

public sealed class GlStateTests
{
    [Fact]
    public void BlendState_NonPremultiplied_HasExpectedSourceAndDestinationFactors()
    {
        // Act
        BlendState state = BlendState.NonPremultiplied;

        // Assert
        state.ColorSource.Should().Be(BlendingFactor.SrcAlpha);
        state.ColorDestination.Should().Be(BlendingFactor.OneMinusSrcAlpha);
        state.AlphaSource.Should().Be(BlendingFactor.SrcAlpha);
        state.AlphaDestination.Should().Be(BlendingFactor.OneMinusSrcAlpha);
    }

    [Fact]
    public void BlendState_Additive_HasOneDestinationFactor()
    {
        // Act
        BlendState state = BlendState.Additive;

        // Assert
        state.ColorSource.Should().Be(BlendingFactor.SrcAlpha);
        state.ColorDestination.Should().Be(BlendingFactor.One);
    }

    [Fact]
    public void DepthStencilState_Presets_ConfigureProperDepthFlags()
    {
        // Assert
        DepthStencilState.None.DepthTestEnabled.Should().BeFalse();
        DepthStencilState.None.DepthWriteEnabled.Should().BeFalse();

        DepthStencilState.DepthRead.DepthTestEnabled.Should().BeTrue();
        DepthStencilState.DepthRead.DepthWriteEnabled.Should().BeFalse();

        DepthStencilState.DepthReadWrite.DepthTestEnabled.Should().BeTrue();
        DepthStencilState.DepthReadWrite.DepthWriteEnabled.Should().BeTrue();
    }
}
