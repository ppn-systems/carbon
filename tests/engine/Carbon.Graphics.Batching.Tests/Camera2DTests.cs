// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Batching.Tests;

public sealed class Camera2DTests
{
    [Fact]
    public void GetVisibleBounds_WhenRotationIsZero_ReturnsAccurateAabbDirectly()
    {
        // Arrange
        Camera2D camera = new(800f, 600f)
        {
            Position = new Vector2(100f, 200f),
            Zoom = 2.0f
        };

        // Act
        RectangleF bounds = camera.GetVisibleBounds();

        // Assert
        // Viewport 800x600 at 2x zoom is 400x300 in world units.
        // Center at (100, 200) means Left: -100, Top: 50, Width: 400, Height: 300
        bounds.Width.Should().BeApproximately(400f, 0.001f);
        bounds.Height.Should().BeApproximately(300f, 0.001f);
        bounds.Left.Should().BeApproximately(-100f, 0.001f);
        bounds.Top.Should().BeApproximately(50f, 0.001f);
    }

    [Fact]
    public void ScreenToWorld_And_WorldToScreen_RoundTrip_MatchesOriginalCoordinate()
    {
        // Arrange
        Camera2D camera = new(1920f, 1080f)
        {
            Position = new Vector2(500f, 500f),
            Zoom = 1.5f
        };
        Vector2 screenPoint = new(960f, 540f); // Screen center

        // Act
        Vector2 worldPoint = camera.ScreenToWorld(screenPoint);
        Vector2 backToScreen = camera.WorldToScreen(worldPoint);

        // Assert
        worldPoint.X.Should().BeApproximately(500f, 0.01f);
        worldPoint.Y.Should().BeApproximately(500f, 0.01f);
        backToScreen.X.Should().BeApproximately(screenPoint.X, 0.01f);
        backToScreen.Y.Should().BeApproximately(screenPoint.Y, 0.01f);
    }

    [Fact]
    public void ClampToBounds_WhenPositionExceedsBounds_ConstrainsVisibleArea()
    {
        // Arrange
        Camera2D camera = new(800f, 600f)
        {
            Position = new Vector2(5000f, 5000f)
        };
        RectangleF worldBounds = new(0f, 0f, 2000f, 2000f);

        // Act
        camera.ClampToBounds(worldBounds);

        // Assert
        // Camera half size is 400 x 300. Max center within [0, 2000] is (1600, 1700).
        camera.Position.X.Should().BeApproximately(1600f, 0.01f);
        camera.Position.Y.Should().BeApproximately(1700f, 0.01f);
    }

    [Fact]
    public void LerpTo_WhenSmoothingApplied_ApproachesTargetWithoutOvershoot()
    {
        // Arrange
        Camera2D camera = new(800f, 600f)
        {
            Position = Vector2.Zero
        };
        Vector2 target = new(100f, 100f);

        // Act
        camera.LerpTo(target, smoothing: 5.0f, dt: 0.1);

        // Assert
        camera.Position.X.Should().BeGreaterThan(0f).And.BeLessThan(100f);
        camera.Position.Y.Should().BeGreaterThan(0f).And.BeLessThan(100f);
    }
}
