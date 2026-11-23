// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Windowing.Configuration;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Windowing.Tests;

public sealed class WindowSettingsTests
{
    [Fact]
    public void Default_ShouldHaveStandardConfiguredProperties()
    {
        WindowSettings settings = WindowSettings.Default;

        settings.Title.Should().Be("Carbon Engine");
        settings.Width.Should().Be(1280);
        settings.Height.Should().Be(720);
        settings.VSync.Should().BeTrue();
        settings.DisplayMode.Should().Be(WindowDisplayMode.Windowed);
        settings.IsResizable.Should().BeTrue();
        settings.TargetFps.Should().Be(0.0);
        settings.Samples.Should().Be(0);
    }

    [Fact]
    public void Properties_ShouldAllowCustomConfiguration()
    {
        WindowSettings settings = new()
        {
            Title = "My Custom MMORPG",
            Width = 1920,
            Height = 1080,
            VSync = false,
            DisplayMode = WindowDisplayMode.Borderless,
            IsResizable = false,
            TargetFps = 144.0,
            Samples = 4
        };

        settings.Title.Should().Be("My Custom MMORPG");
        settings.Width.Should().Be(1920);
        settings.Height.Should().Be(1080);
        settings.VSync.Should().BeFalse();
        settings.DisplayMode.Should().Be(WindowDisplayMode.Borderless);
        settings.IsResizable.Should().BeFalse();
        settings.TargetFps.Should().Be(144.0);
        settings.Samples.Should().Be(4);
    }
}
