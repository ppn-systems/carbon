// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
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
using FluentAssertions;
using NSubstitute;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The Start and Server screens and screen changes (docs/art/menu-ui.md rules 16–17): keyboard focus order, server
/// cards with friendly status words, and panels that slide in without a fade to black.
/// </summary>
public sealed class MenuScreensTests : IDisposable
{
    private readonly IInputManager _input = Substitute.For<IInputManager>();
    private readonly ISceneManager _scenes = Substitute.For<ISceneManager>();
    private readonly List<(IScene Scene, float Duration)> _changes = [];
    private readonly SceneContext _context;
    private readonly FakeProbe _probe = new();
    private readonly MenuFlow _flow;
    private KeyboardSnapshot _keyboard = new(new bool[512], new bool[512]);

    public MenuScreensTests()
    {
        _input.Keyboard.Returns(_ => _keyboard);
        _input.Mouse.Returns(new MouseSnapshot(Vector2.Zero, Vector2.Zero, Vector2.Zero, new bool[8], new bool[8]));
        IGameWindow window = Substitute.For<IGameWindow>();
        window.Width.Returns(1280);
        window.Height.Returns(720);
        window.Input.Returns(_input);
        _scenes.When(m => m.ChangeScene(Arg.Any<IScene>(), Arg.Any<SceneTransitionData?>(), Arg.Any<float>()))
            .Do(call => _changes.Add((call.Arg<IScene>(), call.Arg<float>())));
        _context = new SceneContext(window, new Camera2D(1280f, 720f), _scenes);
        IniDocument servers = IniDocument.Parse("[server.home]\nname = Home Realm\nhost = 127.0.0.1\nport = 57207\n[server.far]\nname = Far Shore\nhost = 127.0.0.2\nport = 57207\n");
        _flow = new MenuFlow(Substitute.For<IWorldSession>(), new DesktopOptions(), new Camera2D(1280f, 720f), Substitute.For<IRememberedAccountStore>(), ServerDirectory.FromIni(servers, "127.0.0.1", 57207), _probe);
    }

    public void Dispose()
    {
        foreach ((IScene scene, _) in _changes)
        {
            scene.Dispose();
        }

        _scenes.Dispose();
    }

    [Theory]
    [InlineData(0.0f, 0f, 14f, 1f)]
    [InlineData(MenuTransition.Seconds, 1f, 0f, 0f)]
    public void MenuTransition_SlidesUpAndUnveilsOverItsDuration(float elapsed, float eased, float offset, float veil)
    {
        MenuTransition transition = new(animate: true);
        transition.Advance(elapsed);

        transition.Eased.Should().BeApproximately(eased, 1e-5f);
        transition.Offset.Should().Be(offset);
        transition.Veil.Should().BeApproximately(veil, 1e-5f);
    }

    [Fact]
    public void MenuTransition_EasesOutAndRestartsOnReEntry()
    {
        MenuTransition transition = new(animate: true);
        transition.Advance(MenuTransition.Seconds * 0.5f);

        transition.Eased.Should().BeApproximately(0.875f, 1e-5f, "a cubic ease-out is 7/8 done halfway: fast start, gentle landing");
        transition.IsDone.Should().BeFalse();

        transition.Advance(1f);
        transition.IsDone.Should().BeTrue();
        transition.Restart();
        transition.Offset.Should().Be(MenuTransition.SlidePixels, "entering again slides in again");
    }

    [Fact]
    public void MenuTransition_WithMotionOff_IsSettledAtOnce()
    {
        MenuTransition transition = new(animate: false);

        transition.IsDone.Should().BeTrue();
        transition.Offset.Should().Be(0f);
        transition.Veil.Should().Be(0f);
    }

    [Fact]
    public void ChangeTo_BetweenMenuScreens_SwapsWithoutAFadeToBlack()
    {
        using LoginScene login = new(_flow);
        login.Enter(_context);

        this.Press(Key.Escape);
        login.Update(_context, 0.016);

        _changes.Should().ContainSingle();
        _changes[0].Scene.Should().BeOfType<StartScene>();
        _changes[0].Duration.Should().Be(0f, "the shared backdrop and title stay; only the panel slides in");
    }

    [Fact]
    public void StartMenuModel_FocusWalksTheButtonsThenQuitAndWraps()
    {
        StartMenuModel signedOut = new(Substitute.For<IRememberedAccountStore>());
        signedOut.Focus.Should().Be(StartMenuAction.Login, "the first button starts focused");
        StartMenuAction[] seen = new StartMenuAction[5];
        for (int i = 0; i < seen.Length; i++)
        {
            seen[i] = signedOut.Focus;
            signedOut.MoveFocus(reverse: false);
        }

        seen.Should().Equal(StartMenuAction.Login, StartMenuAction.Register, StartMenuAction.Server, StartMenuAction.Quit, StartMenuAction.Login);
        signedOut.MoveFocus(reverse: true);
        signedOut.Focus.Should().Be(StartMenuAction.Login, "Up steps back one button");
        signedOut.MoveFocus(reverse: true);
        signedOut.Focus.Should().Be(StartMenuAction.Quit, "Up from the first button wraps to Quit");
    }

    [Fact]
    public void StartMenuModelLogOut_MovesFocusToAButtonStillOffered()
    {
        IRememberedAccountStore store = Substitute.For<IRememberedAccountStore>();
        store.Load().Returns(new RememberedAccount("ayla", "127.0.0.1", 57207, default, DateTimeOffset.MaxValue));
        StartMenuModel model = new(store);
        model.Focus.Should().Be(StartMenuAction.Continue);
        model.MoveFocus(reverse: false);
        model.Focus.Should().Be(StartMenuAction.LogOut);

        _ = model.LogOut();

        model.FocusOrder.Should().Equal(StartMenuAction.Login, StartMenuAction.Register, StartMenuAction.Server, StartMenuAction.Quit);
        model.Focus.Should().Be(StartMenuAction.Login, "focus never rests on a button that is gone");
    }

    [Fact]
    public void StartSceneKeys_DownThenEnter_OpensTheFocusedScreen()
    {
        using StartScene start = new(_flow);
        start.Enter(_context);

        this.Press(Key.Down);
        start.Update(_context, 0.016);
        start.Focus.Should().Be(StartMenuAction.Register);

        this.Press(Key.Enter);
        start.Update(_context, 0.016);

        _changes.Should().ContainSingle().Which.Scene.Should().BeOfType<RegisterScene>("Enter opens the focused button, not always the first");
    }

    [Theory]
    [InlineData(null, "Unreachable", "Unknown", 0)]
    [InlineData(20, "Online", "Excellent", 4)]
    [InlineData(90, "Online", "Good", 3)]
    [InlineData(200, "Online", "Fair", 2)]
    [InlineData(900, "Online", "Poor", 1)]
    public void ServerStatusFromProbe_GivesFriendlyWordsAndBars(int? ms, string health, string quality, int bars)
    {
        ServerStatus status = ServerStatus.FromProbe(ms is { } value ? TimeSpan.FromMilliseconds(value) : null);

        status.Health.ToString().Should().Be(health);
        status.Quality.ToString().Should().Be(quality);
        status.Bars.Should().Be(bars);
    }

    [Fact]
    public void ServerSelectScene_ChecksEachServerOnceAndShowsTheResults()
    {
        using ServerSelectScene screen = new(_flow);
        screen.Enter(_context);

        _probe.Calls.Should().Equal("Home Realm", "Far Shore");
        screen.StatusOf(0).Should().Be(ServerStatus.Checking);

        _probe.Complete("Home Realm", TimeSpan.FromMilliseconds(40));
        _probe.Complete("Far Shore", null);
        screen.Update(_context, 0.016);

        screen.StatusOf(0).Should().Be(new ServerStatus(ServerHealth.Online, ConnectionQuality.Excellent));
        screen.StatusOf(1).Health.Should().Be(ServerHealth.Unreachable);
        screen.Update(_context, 0.016);
        _probe.Calls.Should().HaveCount(2, "a server is checked once per visit, never on a timer");
    }

    [Fact]
    public void ServerSelectSceneKeys_DownWrapsOverCardsAndBack_EnterPicksTheFocusedServer()
    {
        using ServerSelectScene screen = new(_flow);
        screen.Enter(_context);
        screen.FocusIndex.Should().Be(0, "focus starts on the selected server");

        this.Press(Key.Down);
        screen.Update(_context, 0.016);
        screen.FocusIndex.Should().Be(1);
        this.Press(Key.Down);
        screen.Update(_context, 0.016);
        screen.FocusIndex.Should().Be(2, "after the cards comes Back");
        this.Press(Key.Down);
        screen.Update(_context, 0.016);
        screen.FocusIndex.Should().Be(0, "and it wraps");

        this.Press(Key.Up);
        screen.Update(_context, 0.016);
        this.Press(Key.Up);
        screen.Update(_context, 0.016);
        this.Press(Key.Enter);
        screen.Update(_context, 0.016);

        _flow.SelectedServer.Name.Should().Be("Far Shore");
        _changes.Should().ContainSingle().Which.Scene.Should().BeOfType<StartScene>();
    }

    private void Press(Key key)
    {
        bool[] current = new bool[512];
        current[(int)key] = true;
        _keyboard = new KeyboardSnapshot(current, new bool[512]);
    }

    private sealed class FakeProbe : IServerProbe
    {
        private readonly Dictionary<string, TaskCompletionSource<TimeSpan?>> _pending = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];

        public Task<TimeSpan?> ProbeAsync(GameServer server, CancellationToken ct)
        {
            this.Calls.Add(server.Name);
            TaskCompletionSource<TimeSpan?> source = new();
            _pending[server.Name] = source;
            return source.Task;
        }

        public void Complete(string name, TimeSpan? result) => _pending[name].SetResult(result);
    }
}
