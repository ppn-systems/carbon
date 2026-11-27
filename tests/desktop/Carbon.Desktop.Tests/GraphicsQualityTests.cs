// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.PostProcessing;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Graphics quality presets (ADR-041): parsing, the GPU default, persistence, and which passes each preset runs. All
/// headless: the world renderer resolves its preset without a GL context.
/// </summary>
public sealed class GraphicsQualityTests
{
    [Theory]
    [InlineData("low", GraphicsQuality.Low)]
    [InlineData("MEDIUM", GraphicsQuality.Medium)]
    [InlineData(" High ", GraphicsQuality.High)]
    public void TryParse_KnownPreset_ReturnsIt(string text, GraphicsQuality expected)
    {
        GraphicsQualityPolicy.TryParse(text, out GraphicsQuality quality).Should().BeTrue();
        quality.Should().Be(expected);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ultra")]
    public void TryParse_AutoOrUnknown_IsNotAPreset(string? text) =>
        GraphicsQualityPolicy.TryParse(text, out _).Should().BeFalse("auto and unknown text fall back to the GPU default");

    [Theory]
    [InlineData("llvmpipe (LLVM 19.1.1, 256 bits)", GraphicsQuality.Low)]
    [InlineData("SwiftShader Device (Subzero)", GraphicsQuality.Low)]
    [InlineData("Mesa Intel(R) UHD Graphics 620 (KBL GT2)", GraphicsQuality.Medium)]
    [InlineData("AMD Radeon(TM) Graphics (radeonsi, renoir)", GraphicsQuality.Medium)]
    [InlineData("NVIDIA GeForce RTX 3060/PCIe/SSE2", GraphicsQuality.High)]
    [InlineData("AMD Radeon RX 6700 XT (navi22)", GraphicsQuality.High)]
    public void DefaultFor_Renderer_PicksPresetByGpuClass(string renderer, GraphicsQuality expected) =>
        GraphicsQualityPolicy.DefaultFor(renderer).Should().Be(expected);

    [Fact]
    public void Resolve_OverrideWinsOverSavedChoiceAndGpu()
    {
        GraphicsQualityPolicy.Resolve("low", GraphicsQuality.High, "NVIDIA GeForce RTX 3060").Should().Be(GraphicsQuality.Low);
        GraphicsQualityPolicy.Resolve(null, GraphicsQuality.High, "llvmpipe").Should().Be(GraphicsQuality.High, "the player's choice beats the GPU default");
        GraphicsQualityPolicy.Resolve("auto", null, "llvmpipe").Should().Be(GraphicsQuality.Low, "auto follows the GPU");
    }

    [Fact]
    public void PostPassPlan_Low_RunsNoOffscreenPass()
    {
        PostPassPlan plan = PostPassPlan.For(GraphicsQuality.Low);

        plan.UsesOffscreenScene.Should().BeFalse();
        plan.UsesLightMap.Should().BeFalse();
        plan.ColorGrading.Should().BeFalse();
        plan.Vignette.Should().BeFalse();
        plan.Bloom.Should().BeFalse();
        plan.LightMapSize(1280, 720).Should().Be((0u, 0u));
    }

    [Fact]
    public void PostPassPlan_Medium_LightsAndGradesWithoutBloom()
    {
        PostPassPlan plan = PostPassPlan.For(GraphicsQuality.Medium);

        plan.UsesOffscreenScene.Should().BeTrue();
        plan.ColorGrading.Should().BeTrue();
        plan.Vignette.Should().BeTrue();
        plan.Bloom.Should().BeFalse("bloom taps are High only");
        plan.LightMapSize(1280, 720).Should().Be((640u, 360u), "the light map is half resolution");
    }

    [Fact]
    public void PostPassPlan_High_AddsBloom()
    {
        PostPassPlan plan = PostPassPlan.For(GraphicsQuality.High);

        plan.Bloom.Should().BeTrue();
        plan.UsesLightMap.Should().BeTrue();
        plan.LightMapSize(1, 1).Should().Be((1u, 1u), "a tiny viewport still gets a 1x1 light map");
    }

    [Fact]
    public void QualityChoice_TurnedDown_SwitchesThePassesOff()
    {
        using WorldSceneRenderer renderer = new(Substitute.For<IGraphicsDevice>(), new DesktopOptions(), Substitute.For<IVectorArtRepository>());

        renderer.QualityChoice = GraphicsQuality.High;
        renderer.Plan.Bloom.Should().BeTrue();

        renderer.QualityChoice = GraphicsQuality.Low;

        renderer.Quality.Should().Be(GraphicsQuality.Low);
        renderer.Plan.UsesOffscreenScene.Should().BeFalse("Low must not render the world offscreen");
        renderer.Plan.UsesLightMap.Should().BeFalse();
        renderer.Plan.Bloom.Should().BeFalse();
    }

    [Fact]
    public void QualityChoice_WithOverride_KeepsTheOverride()
    {
        DesktopOptions options = new() { GraphicsQualityOverride = "medium" };
        using WorldSceneRenderer renderer = new(Substitute.For<IGraphicsDevice>(), options, Substitute.For<IVectorArtRepository>());

        renderer.QualityChoice = GraphicsQuality.High;

        renderer.Quality.Should().Be(GraphicsQuality.Medium, "CARBON_GRAPHICS_QUALITY wins over the saved choice");
    }

    [Fact]
    public void QualityChoice_SetEveryFrameToTheSameValue_AllocatesNothing()
    {
        using WorldSceneRenderer renderer = new(Substitute.For<IGraphicsDevice>(), new DesktopOptions(), Substitute.For<IVectorArtRepository>());
        renderer.QualityChoice = GraphicsQuality.High;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            renderer.QualityChoice = GraphicsQuality.High;
            _ = renderer.Plan;
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the scene sets the choice every frame");
    }

    [Theory]
    [InlineData(GraphicsQuality.Low, "low")]
    [InlineData(GraphicsQuality.High, "high")]
    [InlineData(null, "auto")]
    public void UiSettings_SaveAndLoad_RoundTripsGraphicsQuality(GraphicsQuality? quality, string written)
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-settings-{Guid.NewGuid():N}.ini");
        try
        {
            new UiSettings { GraphicsQuality = quality }.Save(path);

            File.ReadAllText(path).Should().Contain($"graphics_quality = {written}");
            UiSettings.LoadOrDefault(path).GraphicsQuality.Should().Be(quality);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UiSettings_UnknownGraphicsQuality_LoadsAsAuto()
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-settings-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[display]\ngraphics_quality = ultra\n");

            UiSettings.LoadOrDefault(path).GraphicsQuality.Should().BeNull("an unknown preset falls back to the GPU default");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DesktopOptions_GraphicsQualityVariable_IsReadAsOverride()
    {
        DesktopOptions options = DesktopOptions.FromEnvironment(name => name == "CARBON_GRAPHICS_QUALITY" ? "high" : null);

        options.GraphicsQualityOverride.Should().Be("high");
    }
}
