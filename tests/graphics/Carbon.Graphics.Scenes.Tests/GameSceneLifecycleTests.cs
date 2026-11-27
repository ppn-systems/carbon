// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Scenes.Tests;

/// <summary>
/// The base-scene contract the desktop scenes rely on (they call <c>base.Load/Update/Render/Dispose</c>): naming, the
/// loaded flag and argument checks. Written against the scene before its unused object roster was removed.
/// </summary>
public sealed class GameSceneLifecycleTests
{
    private readonly SceneContext _context;

    public GameSceneLifecycleTests()
    {
        IGameWindow fakeWindow = Substitute.For<IGameWindow>();
        Camera2D camera = new(800f, 600f);
        ISceneManager fakeManager = Substitute.For<ISceneManager>();
        _context = new SceneContext(fakeWindow, camera, fakeManager);
    }

    [Fact]
    public void Constructor_WithoutName_UsesTheTypeName()
    {
        using UnnamedScene scene = new();

        scene.Name.Should().Be(nameof(UnnamedScene), "a scene without a name is known by its type");
    }

    [Fact]
    public void Constructor_WithName_KeepsIt()
    {
        using NamedScene scene = new("World");

        scene.Name.Should().Be("World");
    }

    [Fact]
    public void LoadThenUnload_TogglesIsLoaded()
    {
        using NamedScene scene = new("World");
        scene.IsLoaded.Should().BeFalse("a new scene is not loaded");

        scene.Load(_context);
        scene.IsLoaded.Should().BeTrue("Load marks the scene loaded");

        scene.Unload(_context);
        scene.IsLoaded.Should().BeFalse("Unload marks the scene unloaded");
    }

    [Fact]
    public void Update_PlainScene_DoesNotThrowAndKeepsTheSceneLoaded()
    {
        using NamedScene scene = new("World");
        scene.Load(_context);

        scene.Update(_context, dt: 0.016);

        scene.IsLoaded.Should().BeTrue("updating a scene does not change its lifecycle state");
    }

    [Fact]
    public void Render_NullArguments_AreRejected()
    {
        using NamedScene scene = new("World");

        FluentActions.Invoking(() => scene.Render(null!, null!, 0.016)).Should().Throw<ArgumentNullException>().WithParameterName("context");
        FluentActions.Invoking(() => scene.Render(_context, null!, 0.016)).Should().Throw<ArgumentNullException>().WithParameterName("spriteBatch");
    }

    [Fact]
    public void Dispose_Twice_IsSafe()
    {
        NamedScene scene = new("World");

        scene.Dispose();

        FluentActions.Invoking(scene.Dispose).Should().NotThrow("disposing twice is a no-op");
    }

    private sealed class UnnamedScene : GameScene;

    private sealed class NamedScene(string name) : GameScene(name);
}
