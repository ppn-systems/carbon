// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Scenes.Objects;
using Carbon.Graphics.Windowing.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Scenes.Tests;

public sealed class GameSceneLifecycleTests
{
    private sealed class TestGameScene : GameScene
    {
        public TestGameScene() : base("TestScene") { }
    }

    private sealed class TestRenderObject : RenderObject
    {
        public override RectangleF Bounds => new(this.Position.X, this.Position.Y, 10f, 10f);
        public override void Draw(SpriteBatch spriteBatch) { }
    }

    private readonly SceneContext _context;

    public GameSceneLifecycleTests()
    {
        IGameWindow fakeWindow = Substitute.For<IGameWindow>();
        Camera2D camera = new(800f, 600f);
        ISceneManager fakeManager = Substitute.For<ISceneManager>();
        _context = new SceneContext(fakeWindow, camera, fakeManager);
    }

    [Fact]
    public void Add_EnqueuesObject_AndMountsOnNextUpdate()
    {
        // Arrange
        using TestGameScene scene = new();
        TestRenderObject obj = new() { Tag = "player" };

        // Act
        scene.Add(obj);
        scene.Objects.Should().BeEmpty("object is pending in double-buffered queue until Update");

        scene.Update(_context, dt: 0.016);

        // Assert
        scene.Objects.Should().ContainSingle().Which.Should().Be(obj);
        scene.FindFirstByTag<TestRenderObject>("player").Should().Be(obj);
    }

    [Fact]
    public void Remove_EnqueuesObject_AndUnmountsOnNextUpdate()
    {
        // Arrange
        using TestGameScene scene = new();
        TestRenderObject obj = new() { Tag = "enemy" };
        scene.Add(obj);
        scene.Update(_context, dt: 0.016);

        // Act
        scene.Remove(obj);
        scene.Objects.Should().ContainSingle("still mounted until next tick");

        scene.Update(_context, dt: 0.016);

        // Assert
        scene.Objects.Should().BeEmpty();
        scene.FindFirstByTag<TestRenderObject>("enemy").Should().BeNull();
    }
}
