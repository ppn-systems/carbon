// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Scenes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Carbon.Maps.Biomes;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The menu backdrop (docs/art/menu-ui.md rule 15): its clock moves only while a menu scene drives it, pauses when the
/// scene leaves, holds still with motion off, loops over a fixed set of chunks, allocates nothing per frame, and looks
/// at a scenic, deterministic spot.
/// </summary>
public sealed class MenuBackdropTests
{
    private static readonly Vector2 s_centre = new(5000f, -3000f);

    [Fact]
    public void ClockAdvance_MovesTheCameraAndPauseFreezesIt()
    {
        MenuBackdropClock clock = new(s_centre, animate: true);
        Vector2 start = clock.Camera;

        clock.Advance(1f / 60f);
        clock.Camera.Should().NotBe(start, "the backdrop pans while its scene runs");

        clock.Pause();
        Vector2 paused = clock.Camera;
        float pausedTime = clock.Time;
        for (int i = 0; i < 120; i++)
        {
            clock.Advance(1f / 60f);
        }

        clock.Time.Should().Be(pausedTime, "an inactive scene leaves the backdrop still");
        clock.Camera.Should().Be(paused);

        clock.Resume();
        clock.Advance(1f / 60f);
        clock.Time.Should().BeGreaterThan(pausedTime, "it picks up where it stopped");
    }

    [Fact]
    public void ClockAdvance_WithMotionOff_HoldsAStillFrame()
    {
        MenuBackdropClock clock = new(s_centre, animate: false);
        Vector2 start = clock.Camera;

        clock.Advance(5f);

        clock.Camera.Should().Be(start, "CARBON_MENU_MOTION=0 shows a still frame");
    }

    [Fact]
    public void ClockAdvance_ClampsALongHitchSoTheViewNeverJumps()
    {
        MenuBackdropClock clock = new(s_centre, animate: true);

        clock.Advance(3f);

        clock.Time.Should().BeApproximately(0.1f, 1e-6f, "a scene-load hitch advances at most 100 ms");
    }

    [Fact]
    public void ClockPath_IsASlowClosedLoopAroundTheCentre()
    {
        MenuBackdropClock clock = new(s_centre, animate: true);

        Vector2 a = clock.CameraAt(0f);
        Vector2 b = clock.CameraAt(MenuBackdropClock.LoopSeconds);
        Vector2.Distance(a, b).Should().BeLessThan(0.01f, "after one loop the camera is back where it started");

        float fastest = 0f;
        for (int i = 0; i < 3600; i++)
        {
            float t = i * 0.1f;
            fastest = MathF.Max(fastest, Vector2.Distance(clock.CameraAt(t), clock.CameraAt(t + 0.1f)) / 0.1f);
        }

        fastest.Should().BeLessThan(16f, "the pan drifts a few world pixels per second, never fast enough to distract");
    }

    [Fact]
    public void ClockChunkRange_CoversEveryViewAlongTheLoop()
    {
        const float chunk = 1024f;
        const float viewW = 1900f;
        const float viewH = 800f;
        MenuBackdropClock clock = new(s_centre, animate: true);
        (int minX, int minY, int maxX, int maxY) = clock.ChunkRange(viewW, viewH, chunk);

        for (int i = 0; i <= 720; i++)
        {
            Vector2 cam = clock.CameraAt(i * MenuBackdropClock.LoopSeconds / 720f);
            ((int)MathF.Floor((cam.X - (viewW / 2f)) / chunk)).Should().BeGreaterThanOrEqualTo(minX);
            ((int)MathF.Floor((cam.X + (viewW / 2f)) / chunk)).Should().BeLessThanOrEqualTo(maxX);
            ((int)MathF.Floor((cam.Y - (viewH / 2f)) / chunk)).Should().BeGreaterThanOrEqualTo(minY);
            ((int)MathF.Floor((cam.Y + (viewH / 2f)) / chunk)).Should().BeLessThanOrEqualTo(maxY);
        }

        ((maxX - minX + 1) * (maxY - minY + 1)).Should().BeLessThanOrEqualTo(20, "the loop needs only a handful of chunks, generated once");
    }

    [Fact]
    public void ClockFrame_AllocatesNothing()
    {
        MenuBackdropClock clock = new(s_centre, animate: true);
        clock.Advance(0.016f);
        _ = clock.Camera;
        _ = clock.NightAmount;

        long before = GC.GetAllocatedBytesForCurrentThread();
        float sink = 0f;
        for (int i = 0; i < 10_000; i++)
        {
            clock.Advance(0.016f);
            sink += clock.Camera.X + clock.NightAmount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0, "the backdrop runs every menu frame (zero per-frame allocation)");
        sink.Should().NotBe(0f);
    }

    [Fact]
    public void BackdropAdvanceAndPause_BeforeAnyDraw_AllocateNothingAndStartNoWork()
    {
        using MenuBackdrop backdrop = new(1337UL, animate: true, static _ => null);
        backdrop.Advance(0.016f);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            backdrop.Advance(0.016f);
            backdrop.Pause();
            backdrop.Resume();
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        backdrop.IsSettled(expectWorld: false).Should().BeFalse("nothing is generated until the first draw asks for it");
    }

    [Fact]
    public void MenuSceneLeave_PausesTheSharedBackdropAndEnterResumesIt()
    {
        IInputManager input = Substitute.For<IInputManager>();
        input.Keyboard.Returns(new KeyboardSnapshot(new bool[512], new bool[512]));
        input.Mouse.Returns(new MouseSnapshot(Vector2.Zero, Vector2.Zero, Vector2.Zero, new bool[8], new bool[8]));
        IGameWindow window = Substitute.For<IGameWindow>();
        window.Width.Returns(1280);
        window.Height.Returns(720);
        window.Input.Returns(input);
        using ISceneManager scenes = Substitute.For<ISceneManager>();
        SceneContext context = new(window, new Camera2D(1280f, 720f), scenes);
        MenuFlow flow = new(Substitute.For<IWorldSession>(), new DesktopOptions(), new Camera2D(1280f, 720f), Substitute.For<IRememberedAccountStore>(), ServerDirectory.FromIni(null, "127.0.0.1", 7777));
        using LoginScene scene = new(flow);

        scene.Enter(context);
        scene.Update(context, 0.5f);
        float running = flow.Backdrop.Clock.Time;
        running.Should().BeGreaterThan(0f, "the active menu scene drives the backdrop");

        scene.Leave(context);
        flow.Backdrop.Clock.IsPaused.Should().BeTrue();
        flow.Backdrop.Advance(1f);
        flow.Backdrop.Clock.Time.Should().Be(running, "nothing moves while no menu scene is active");

        scene.Enter(context);
        flow.Backdrop.Clock.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void SiteChoose_PrefersAShoreOverOpenPlainsAndIsDeterministic()
    {
        // Plains everywhere except a sea to the east of x = 160: the best view straddles the shore.
        static BiomeType World(int x, int y) => x > 160 ? BiomeType.ShallowWater : x > 140 ? BiomeType.BeachSand : BiomeType.LushPlains;

        (int X, int Y) site = MenuBackdropSite.Choose(World, (0, 0));

        MenuBackdropSite.Score(World, site.X, site.Y).Should().BeGreaterThan(MenuBackdropSite.Score(World, 0, 0), "a shore beats flat plains");
        site.X.Should().BeInRange(100, 224, "the chosen view shows some water but is not open sea");
        MenuBackdropSite.Choose(World, (0, 0)).Should().Be(site, "the same seed always gives the same backdrop");
    }

    [Fact]
    public void SiteScore_RewardsAShoreButNotOpenSea()
    {
        // The scored view spans 88 tiles across the centre (x -44..44 around 0).
        int plains = MenuBackdropSite.Score(static (_, _) => BiomeType.LushPlains, 0, 0);
        int shore = MenuBackdropSite.Score(static (x, _) => x > 20 ? BiomeType.ShallowWater : BiomeType.LushPlains, 0, 0);
        int sea = MenuBackdropSite.Score(static (x, _) => x > -40 ? BiomeType.ShallowWater : BiomeType.LushPlains, 0, 0);

        plains.Should().Be(1, "one biome, no water");
        shore.Should().Be(2 + 3, "two biomes plus the bonus for a view that is about a quarter water");
        sea.Should().Be(2, "a view that is almost all water gets no bonus");
    }

    [Fact]
    public void SiteChoose_OnUniformLand_StaysAtTheSpawn() =>
        MenuBackdropSite.Choose(static (_, _) => BiomeType.LushPlains, (40, -12)).Should().Be((40, -12), "ties keep the spot nearest the spawn");
}
