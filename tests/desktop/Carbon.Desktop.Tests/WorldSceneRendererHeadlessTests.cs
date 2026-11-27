// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The world renderer creates its GPU resources (geometry batch, terrain renderer) on the first draw, so the world scene
/// can be loaded and its input characterized without a GL context. Terrain cache invalidations before that draw have
/// nothing to invalidate.
/// </summary>
public sealed class WorldSceneRendererHeadlessTests
{
    [Fact]
    public void Constructor_WithoutGlContext_TouchesNoGpuApi()
    {
        IGraphicsDevice device = Substitute.For<IGraphicsDevice>();

        using WorldSceneRenderer renderer = new(device, new DesktopOptions(), Substitute.For<IVectorArtRepository>());

        device.ReceivedCalls().Should().BeEmpty("nothing is created on the GPU until the first draw");
    }

    [Fact]
    public void TerrainInvalidation_BeforeAnyDraw_IsANoOp()
    {
        IGraphicsDevice device = Substitute.For<IGraphicsDevice>();
        using WorldSceneRenderer renderer = new(device, new DesktopOptions(), Substitute.For<IVectorArtRepository>());

        Action invalidate = () =>
        {
            renderer.InvalidateChunk(3, -2);
            renderer.InvalidateAll();
            renderer.EvictDistant(0, 0, 4);
        };

        invalidate.Should().NotThrow();
        device.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_BeforeAnyDraw_DisposesCleanlyAndOnlyOnce()
    {
        IGraphicsDevice device = Substitute.For<IGraphicsDevice>();
        using WorldSceneRenderer renderer = new(device, new DesktopOptions(), Substitute.For<IVectorArtRepository>());

        Action dispose = () =>
        {
            renderer.Dispose();
            renderer.Dispose();
        };

        dispose.Should().NotThrow();
        device.ReceivedCalls().Should().BeEmpty();
    }
}
