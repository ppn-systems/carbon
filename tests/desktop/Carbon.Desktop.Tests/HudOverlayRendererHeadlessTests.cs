// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The HUD renderer creates its GPU resources (geometry batch, fonts, UI skin) on first draw, so its UI state can be built
/// and exercised without a GL context, and disposing it before any draw frees nothing it never created.
/// </summary>
public sealed class HudOverlayRendererHeadlessTests
{
    [Fact]
    public void Constructor_WithoutGlContext_TouchesNoGpuApi()
    {
        IGraphicsDevice device = Substitute.For<IGraphicsDevice>();

        using HudOverlayRenderer hud = new(device, Substitute.For<IVectorArtRepository>());

        device.ReceivedCalls().Should().BeEmpty("nothing is created on the GPU until the first draw");
        hud.HasActiveModal.Should().BeFalse();
    }

    [Fact]
    public void Dispose_BeforeAnyDraw_DisposesCleanlyAndOnlyOnce()
    {
        IGraphicsDevice device = Substitute.For<IGraphicsDevice>();
        using HudOverlayRenderer hud = new(device, Substitute.For<IVectorArtRepository>());

        Action dispose = () =>
        {
            hud.Dispose();
            hud.Dispose();
        };

        dispose.Should().NotThrow();
        device.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Font_AfterDispose_IsNotCreated()
    {
        using HudOverlayRenderer hud = new(Substitute.For<IGraphicsDevice>(), Substitute.For<IVectorArtRepository>());
        hud.Dispose();

        Action font = () => _ = hud.Font;

        font.Should().Throw<ObjectDisposedException>("a disposed HUD must not create GPU resources");
    }
}
