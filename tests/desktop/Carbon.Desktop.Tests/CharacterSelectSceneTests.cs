// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
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
/// Characterization of the character screen's behaviour (roster selection, the creator panel and the roster requests),
/// written against the scene before those responsibilities moved out of it. The scene is driven frame by frame with
/// fake input in a 1280×720 window. Button centres come from <c>CharacterSelectScene.LayoutControls</c> at that size.
/// </summary>
public sealed class CharacterSelectSceneTests : IDisposable
{
    // LayoutControls at 1280×720: ox = 180, oy = 110, rx = 772, ry = 178.
    private static readonly Vector2 s_slot0 = new(337f, 216f);
    private static readonly Vector2 s_slot3 = new(337f, 216f + (3 * 84f));
    private static readonly Vector2 s_openCreator = new(337f, 540f);
    private static readonly Vector2 s_enterWorld = new(925f, 525f);
    private static readonly Vector2 s_logout = new(1028f, 141f);
    private static readonly Vector2 s_turnLeft = new(560f, 530f);
    private static readonly Vector2 s_turnRight = new(684f, 530f);
    private static readonly Vector2 s_hairPrev = new(802f, 235f);
    private static readonly Vector2 s_hairNext = new(1048f, 235f);
    private static readonly Vector2 s_hairColorNext = new(1048f, 269f);
    private static readonly Vector2 s_faceNext = new(1048f, 303f);
    private static readonly Vector2 s_skinPrev = new(802f, 337f);
    private static readonly Vector2 s_createWithHeroes = new(879f, 502f);
    private static readonly Vector2 s_createFirstHero = new(925f, 502f);
    private static readonly Vector2 s_backToRoster = new(1020f, 502f);

    private static readonly CharacterRosterEntry s_ayla = new(Guid.NewGuid(), "Ayla", 3, 1, 0f, 0f);
    private static readonly CharacterRosterEntry s_bram = new(Guid.NewGuid(), "Bram", 7, 1, 0f, 0f);

    private readonly IWorldSession _session = Substitute.For<IWorldSession>();
    private readonly IInputManager _input = Substitute.For<IInputManager>();
    private readonly ISceneManager _scenes = Substitute.For<ISceneManager>();
    private readonly List<IScene> _changedTo = [];
    private readonly SceneContext _context;
    private readonly CharacterSelectScene _scene;
    private TaskCompletionSource<IReadOnlyList<CharacterRosterEntry>> _list = new();
    private KeyboardSnapshot _keyboard = new(new bool[512], new bool[512]);
    private MouseSnapshot _mouse = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, new bool[8], new bool[8]);

    public CharacterSelectSceneTests()
    {
        _session.ListCharactersAsync(Arg.Any<CancellationToken>()).Returns(_ => _list.Task);
        _input.Keyboard.Returns(_ => _keyboard);
        _input.Mouse.Returns(_ => _mouse);
        IGameWindow window = Substitute.For<IGameWindow>();
        window.Width.Returns(1280);
        window.Height.Returns(720);
        window.Input.Returns(_input);
        _scenes.When(m => m.ChangeScene(Arg.Any<IScene>(), Arg.Any<SceneTransitionData?>(), Arg.Any<float>()))
            .Do(call => _changedTo.Add(call.Arg<IScene>()));
        _context = new SceneContext(window, new Camera2D(1280f, 720f), _scenes);

        MenuFlow flow = new(_session, new DesktopOptions(), new Camera2D(1280f, 720f), Substitute.For<IRememberedAccountStore>(), ServerDirectory.FromIni(null, "127.0.0.1", 7777));
        _scene = new CharacterSelectScene(flow);
        _scene.Enter(_context);
    }

    public void Dispose()
    {
        foreach (IScene scene in _changedTo)
        {
            scene.Dispose();
        }

        _scene.Dispose();
        _scenes.Dispose();
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void Enter_RosterOfTwo_SelectsTheFirstAndReportsTheCount()
    {
        this.LoadRoster(s_ayla, s_bram);

        _scene.Characters.Should().Equal(s_ayla, s_bram);
        _scene.SelectedIndex.Should().Be(0);
        _scene.IsCreatorMode.Should().BeFalse();
        _scene.StatusMessage.Should().Be(GameText.Plural("character.status.found", 2));
        _scene.StatusIsError.Should().BeFalse();
    }

    [Fact]
    public void Enter_EmptyRoster_OpensTheCreatorWithTheWelcome()
    {
        this.LoadRoster();

        _scene.IsCreatorMode.Should().BeTrue("a new account has no hero to select");
        _scene.StatusMessage.Should().Be(GameText.Get("character.status.welcome"));
    }

    [Fact]
    public void Enter_RosterRequestFails_ShowsTheLoadError()
    {
        _list.SetException(new InvalidOperationException("socket closed"));
        this.Frame();

        _scene.StatusIsError.Should().BeTrue();
        _scene.StatusMessage.Should().Be(GameText.Get("character.status.load_failed"), "the player never sees the exception text");
    }

    [Fact]
    public void ArrowKeys_WrapAroundTheRoster()
    {
        this.LoadRoster(s_ayla, s_bram);

        this.Press(Key.Up);
        _scene.SelectedIndex.Should().Be(1, "up from the first row wraps to the last");
        this.Press(Key.Down);
        _scene.SelectedIndex.Should().Be(0, "down from the last row wraps to the first");
    }

    [Fact]
    public void EnterKey_EntersTheWorldWithTheSelectedHero()
    {
        this.LoadRoster(s_ayla, s_bram);
        TaskCompletionSource<(bool, string)> enter = new();
        _session.EnterWorldAsync(s_bram.CharacterId, Arg.Any<CancellationToken>()).Returns(enter.Task);

        this.Press(Key.Down);
        this.Press(Key.Enter);

        _session.Received(1).EnterWorldAsync(s_bram.CharacterId, Arg.Any<CancellationToken>());
        _scene.StatusMessage.Should().Be(GameText.Format("character.status.entering", new TextArg("name", "Bram")));

        enter.SetResult((true, string.Empty));
        this.Frame();
        _changedTo.Should().ContainSingle().Which.Should().BeOfType<WorldGameplayScene>();
    }

    [Fact]
    public void EnterWorld_Refused_ShowsTheServerMessageAndStays()
    {
        this.LoadRoster(s_ayla);
        _session.EnterWorldAsync(s_ayla.CharacterId, Arg.Any<CancellationToken>()).Returns(Task.FromResult((false, "Hero is busy")));

        this.Click(s_enterWorld);
        this.Frame();

        _scene.StatusIsError.Should().BeTrue();
        _scene.StatusMessage.Should().Be("Hero is busy");
        _changedTo.Should().BeEmpty();
    }

    [Fact]
    public void EnterWorld_RequestThrows_ShowsServerUnreachable()
    {
        this.LoadRoster(s_ayla);
        _session.EnterWorldAsync(s_ayla.CharacterId, Arg.Any<CancellationToken>()).Returns(Task.FromException<(bool, string)>(new TimeoutException("10.0.0.1:7777")));

        this.Click(s_enterWorld);
        this.Frame();

        _scene.StatusIsError.Should().BeTrue();
        _scene.StatusMessage.Should().Be(GameText.Get("auth.server_unreachable"));
    }

    [Fact]
    public void EnterKey_WhileTheRosterIsLoading_DoesNothing()
    {
        this.Press(Key.Enter);

        _session.DidNotReceive().EnterWorldAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClickEmptySlotOrNewHero_OpensTheCreator()
    {
        this.LoadRoster(s_ayla);

        this.Click(s_slot3);
        _scene.IsCreatorMode.Should().BeTrue("an empty slot opens the creator");

        this.Press(Key.Escape); // unfocus the name field
        this.Press(Key.Escape); // back to the roster (a hero exists)
        _scene.IsCreatorMode.Should().BeFalse();

        this.Click(s_openCreator);
        _scene.IsCreatorMode.Should().BeTrue("NEW HERO opens the creator");
    }

    [Fact]
    public void ClickHeroSlot_SelectsIt()
    {
        this.LoadRoster(s_ayla, s_bram);
        this.Press(Key.Down);

        this.Click(s_slot0);

        _scene.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void CreatorTraitButtons_CycleEachTraitAndWrap()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);

        this.Click(s_hairNext);
        this.Click(s_hairNext);
        _scene.AppearancePreset.Should().Be(CharacterAppearance.Pack(2, 0, 0, 0), "hair style 0 → 2");

        this.Click(s_hairPrev);
        this.Click(s_hairPrev);
        this.Click(s_hairPrev);
        _scene.AppearancePreset.Should().Be(CharacterAppearance.Pack(3, 0, 0, 0), "previous from 0 wraps to 3");

        this.Click(s_hairColorNext);
        this.Click(s_faceNext);
        this.Click(s_skinPrev);
        _scene.AppearancePreset.Should().Be(CharacterAppearance.Pack(3, 1, 1, 3), "each button moves only its own trait");
    }

    [Fact]
    public void CreateHero_SendsTheTypedNameAndPresetThenSelectsTheNewHero()
    {
        this.LoadRoster(s_ayla);
        TaskCompletionSource<(bool, string)> create = new();
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<byte>(), Arg.Any<CancellationToken>()).Returns(create.Task);
        this.Click(s_openCreator);
        this.ClearName();
        this.Type("Cora");
        this.Click(s_hairNext);

        this.Click(s_createWithHeroes);

        _session.Received(1).CreateCharacterAsync("Cora", CharacterAppearance.Pack(1, 0, 0, 0), Arg.Any<CancellationToken>());
        _scene.StatusMessage.Should().Be(GameText.Format("character.status.creating", new TextArg("name", "Cora")));

        CharacterRosterEntry cora = new(Guid.NewGuid(), "cora", 1, 1, 0f, 0f);
        _list = new TaskCompletionSource<IReadOnlyList<CharacterRosterEntry>>();
        create.SetResult((true, "Created"));
        this.Frame();
        _scene.IsCreatorMode.Should().BeFalse("a created hero returns to the roster");
        _scene.StatusMessage.Should().Be("Created");

        _list.SetResult([s_ayla, cora]);
        this.Frame();
        _scene.SelectedIndex.Should().Be(1, "the new hero is selected, matched by name ignoring case");
        _scene.StatusMessage.Should().Be(GameText.Plural("character.status.found", 2));
    }

    [Fact]
    public void CreateFirstHero_Refused_ShowsTheMessageAndStaysInTheCreator()
    {
        this.LoadRoster();
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<byte>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult((false, "Name taken")));

        this.Click(s_createFirstHero);
        this.Frame();

        _scene.IsCreatorMode.Should().BeTrue();
        _scene.StatusIsError.Should().BeTrue();
        _scene.StatusMessage.Should().Be("Name taken");
    }

    [Fact]
    public void BackButton_ReturnsToTheRoster()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);

        this.Click(s_backToRoster);

        _scene.IsCreatorMode.Should().BeFalse();
    }

    [Fact]
    public void TypedCharacters_ReachTheNameOnlyInTheCreator()
    {
        this.LoadRoster(s_ayla);
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<byte>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<(bool, string)>().Task);
        this.Type("zz"); // roster mode: ignored
        this.Click(s_openCreator);
        this.ClearName();
        this.Type("Dex");

        this.Click(s_createWithHeroes);

        _session.Received(1).CreateCharacterAsync("Dex", Arg.Any<byte>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void LogoutButton_SignsOutThenReturnsToTheStartScreen()
    {
        this.LoadRoster(s_ayla);
        _session.LogoutAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask); // completes synchronously

        this.Click(s_logout);

        _session.Received(1).LogoutAsync(Arg.Any<CancellationToken>());
        _scene.StatusMessage.Should().Be(GameText.Get("character.status.signing_out"));
        _changedTo.Should().BeEmpty("the scene changes on the frame after the sign-out completes");

        this.Frame();
        _changedTo.Should().ContainSingle().Which.Should().BeOfType<StartScene>();
    }

    [Fact]
    public void Escape_InTheRoster_SignsOut()
    {
        this.LoadRoster(s_ayla);
        _session.LogoutAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        this.Press(Key.Escape);

        _session.Received(1).LogoutAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TurnButtons_RotateThePreviewAndWrap()
    {
        this.LoadRoster(s_ayla);

        this.Click(s_turnLeft);
        _scene.TurnIndex.Should().Be(3, "left from south wraps to the last direction");
        this.Click(s_turnRight);
        this.Click(s_turnRight);
        _scene.TurnIndex.Should().Be(1);
    }

    private void LoadRoster(params CharacterRosterEntry[] heroes)
    {
        _list.SetResult(heroes);
        this.Frame();
    }

    private void Frame()
    {
        _scene.Update(_context, 1.0 / 60.0);
        _keyboard = new KeyboardSnapshot(new bool[512], new bool[512]);
        _mouse = new MouseSnapshot(_mouse.Position, _mouse.Position, Vector2.Zero, new bool[8], new bool[8]);
    }

    private void Press(Key key)
    {
        bool[] down = new bool[512];
        down[(int)key] = true;
        _keyboard = new KeyboardSnapshot(down, new bool[512]);
        this.Frame();
    }

    private void Click(Vector2 at)
    {
        bool[] down = new bool[8];
        down[(int)MouseButton.Left] = true;
        _mouse = new MouseSnapshot(at, at, Vector2.Zero, down, new bool[8]);
        this.Frame();
    }

    private void Type(string text)
    {
        foreach (char ch in text)
        {
            _input.CharacterReceived += Raise.Event<Action<char>>(ch);
        }
    }

    private void ClearName()
    {
        for (int i = 0; i < 24; i++)
        {
            this.Press(Key.Backspace);
        }
    }
}
