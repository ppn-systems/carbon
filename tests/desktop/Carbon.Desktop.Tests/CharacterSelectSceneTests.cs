// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using Carbon.Desktop.Tests.Fakes;
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
    // LayoutControls at 1280×720 (a 920 × 580 panel): ox = 180, oy = 80. In the creator the selector rows sit in the left
    // column, 36 px apart from y = 206 (arrows at x = 232 and 442): race, body type, then the race's options (human: hair,
    // hair colour, eyes, skin). With two bodies the body row is a toggle whose right half picks the second body. The
    // right column (rx = 772, ry = 148) holds the name, then Create at ry + 120.
    private static readonly Vector2 s_slot0 = new(337f, 186f);
    private static readonly Vector2 s_slot3 = new(337f, 186f + (3 * 84f));
    private static readonly Vector2 s_openCreator = new(337f, 578f);
    private static readonly Vector2 s_enterWorld = new(925f, 563f);
    private static readonly Vector2 s_logout = new(1028f, 111f);
    private static readonly Vector2 s_turnLeft = new(560f, 568f);
    private static readonly Vector2 s_turnRight = new(684f, 568f);
    private static readonly Vector2 s_raceNext = new(442f, 206f);
    private static readonly Vector2 s_bodyNext = new(442f, 242f);
    private static readonly Vector2 s_hairPrev = new(232f, 278f);
    private static readonly Vector2 s_hairNext = new(442f, 278f);
    private static readonly Vector2 s_hairColorNext = new(442f, 314f);
    private static readonly Vector2 s_faceNext = new(442f, 350f);
    private static readonly Vector2 s_skinPrev = new(232f, 386f);
    private static readonly Vector2 s_lastOptionRow = new(442f, 386f);
    private static readonly Vector2 s_createWithHeroes = new(879f, 290f);
    private static readonly Vector2 s_createFirstHero = new(925f, 290f);
    private static readonly Vector2 s_backToRoster = new(1020f, 290f);

    private static readonly CharacterRosterEntry s_ayla = new(Guid.NewGuid(), "Ayla", 3, 1, 0f, 0f);
    private static readonly CharacterRosterEntry s_bram = new(Guid.NewGuid(), "Bram", 7, 1, 0f, 0f);

    private static readonly string s_twoRaceJson = TwoRaceJson();

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
        _session.Races.Returns(TestRaces.Synced());
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
    public void CreatorOptionButtons_CycleEachRaceOptionAndWrap()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);

        this.Click(s_hairNext);
        this.Click(s_hairNext);
        _scene.Look.Should().Be(new CharacterLook(1, 0, [2, 0, 0, 0]), "hair style 0 → 2");

        this.Click(s_hairPrev);
        this.Click(s_hairPrev);
        this.Click(s_hairPrev);
        _scene.Look.Should().Be(new CharacterLook(1, 0, [3, 0, 0, 0]), "previous from 0 wraps to the last of 4 choices");

        this.Click(s_hairColorNext);
        this.Click(s_faceNext);
        this.Click(s_skinPrev);
        _scene.Look.Should().Be(new CharacterLook(1, 0, [3, 1, 1, 3]), "each row's buttons move only that option");
    }

    [Fact]
    public void CreatorBodyRow_NextBody_SendsTheFemaleHumanLook()
    {
        this.LoadRoster(s_ayla);
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<CharacterLook>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<(bool, string)>().Task);
        this.Click(s_openCreator);
        this.ClearName();
        this.Type("Mira");

        this.Click(s_bodyNext);
        this.Click(s_hairNext);
        this.Click(s_createWithHeroes);

        _scene.Look.Should().Be(new CharacterLook(1, 1, [1, 0, 0, 0]), "body 1 is the female human body in races.json, and options keep their row");
        _session.Received(1).CreateCharacterAsync("Mira", new CharacterLook(1, 1, [1, 0, 0, 0]), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreatorRaceRow_OnlyOneRace_DoesNotCycle()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);
        this.Click(s_bodyNext);

        this.Click(s_raceNext);

        _scene.Look.RaceId.Should().Be(1, "the shipped races.json has one race, so its row has nothing to cycle to");
        _scene.Look.BodyType.Should().Be(1, "a click on a row that cannot cycle leaves the rest of the look alone");
    }

    [Fact]
    public void CreatorRaceRow_ConfigOnlyRace_IsListedAndItsExtraOptionsScrollIntoView()
    {
        _session.Races.Returns(TestRaces.Synced(Encoding.UTF8.GetBytes(s_twoRaceJson)));
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);

        this.Click(s_raceNext);
        _scene.Look.Should().Be(new CharacterLook(4242, 5, [0, 0, 0, 0, 0, 0]), "the creator lists whatever races.json holds");

        this.Click(s_lastOptionRow);
        _scene.Look.GetOption(3).Should().Be(1, "the fourth option row starts on option 4");

        this.Scroll(at: s_lastOptionRow, wheel: -1f);
        this.Scroll(at: s_lastOptionRow, wheel: -1f);
        this.Scroll(at: s_lastOptionRow, wheel: -1f);
        _scene.OptionScroll.Should().Be(2, "six options under four rows scroll by at most two");
        _scene.RowKeys(0).Should().Be(("character_creator.race", "race.test_kin"));
        _scene.RowKeys(1).Should().Be(("character_creator.body", "race.body.slim"));
        _scene.RowKeys(2).LabelKey.Should().Be("test.option2", "the first option row now shows the third option");
        _scene.RowKeys(5).Should().Be(("test.option5", "appearance.hair.crop"), "the last row shows the sixth option and its current choice");

        this.Click(s_lastOptionRow);
        _scene.Look.Should().Be(new CharacterLook(4242, 5, [0, 0, 0, 1, 0, 1]), "after scrolling, the last row is the sixth option");

        this.Click(s_raceNext);
        _scene.Look.Should().Be(new CharacterLook(1, 0, [0, 0, 0, 0]), "back to the first race, its first body and choices");
        _scene.OptionScroll.Should().Be(0);
    }

    [Fact]
    public void CreateHero_SendsTheTypedNameAndLookThenSelectsTheNewHero()
    {
        this.LoadRoster(s_ayla);
        TaskCompletionSource<(bool, string)> create = new();
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<CharacterLook>(), Arg.Any<CancellationToken>()).Returns(create.Task);
        this.Click(s_openCreator);
        this.ClearName();
        this.Type("Cora");
        this.Click(s_hairNext);

        this.Click(s_createWithHeroes);

        _session.Received(1).CreateCharacterAsync("Cora", new CharacterLook(1, 0, [1, 0, 0, 0]), Arg.Any<CancellationToken>());
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
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<CharacterLook>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult((false, "Name taken")));

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
        _session.CreateCharacterAsync(Arg.Any<string>(), Arg.Any<CharacterLook>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<(bool, string)>().Task);
        this.Type("zz"); // roster mode: ignored
        this.Click(s_openCreator);
        this.ClearName();
        this.Type("Dex");

        this.Click(s_createWithHeroes);

        _session.Received(1).CreateCharacterAsync("Dex", Arg.Any<CharacterLook>(), Arg.Any<CancellationToken>());
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

    [Fact]
    public void CreatorName_RefusesASpaceOrAccentAndSaysWhy()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);
        this.ClearName();

        this.Type("H\u00F9 ng");
        this.Frame();

        _scene.HeroName.Text.Should().Be("Hng", "the server takes only ASCII letters, digits and _ in a hero name");
        _scene.HeroName.Feedback.Should().Be(FieldFeedback.Error);
        _scene.HeroName.FeedbackMessage.Should().Be(GameText.Get("character.name.chars"), "a refused key is explained where the player is looking");

        for (int i = 0; i < 200; i++)
        {
            this.Frame();
        }

        _scene.HeroName.Feedback.Should().Be(FieldFeedback.Success, "after a few seconds the line shows the name is fine");
    }

    [Fact]
    public void CreatorCreate_WithAOneLetterName_IsHeldBackWithTheRule()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);
        this.ClearName();
        this.Type("K");

        this.Click(s_createWithHeroes);

        _ = _session.DidNotReceive().CreateCharacterAsync(Arg.Any<string>(), Arg.Any<CharacterLook>(), Arg.Any<CancellationToken>());
        _scene.HeroName.Feedback.Should().Be(FieldFeedback.Error);
        _scene.HeroName.FeedbackMessage.Should().Be(GameText.Get("character.name.length"));
    }

    [Fact]
    public void ArrowKeys_TurnTheTurntableWhenNotTyping()
    {
        this.LoadRoster(s_ayla);

        this.Press(Key.Right);
        _scene.TurnIndex.Should().Be(1);
        this.Press(Key.Left);
        this.Press(Key.Left);
        _scene.TurnIndex.Should().Be(3, "left wraps from south to east");
    }

    [Fact]
    public void CreatorKeys_TabToASelectorRowThenRightStepsIt()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);

        this.Press(Key.Tab);
        this.Press(Key.Tab);
        this.Press(Key.Tab);
        this.Press(Key.Right);

        _scene.Look.Should().Be(new CharacterLook(1, 0, [1, 0, 0, 0]), "Tab from the name walks race, body, then hair; Right steps hair");
        _scene.TurnIndex.Should().Be(0, "a focused selector row takes Left/Right instead of the turntable");
        _scene.HeroName.IsFocused.Should().BeFalse("typing goes nowhere while a selector row has focus");
    }

    [Fact]
    public void CreatorBodyToggle_ClickEitherHalfPicksThatBody()
    {
        this.LoadRoster(s_ayla);
        this.Click(s_openCreator);
        Vector2 leftHalf = new(s_hairPrev.X + 20f, s_bodyNext.Y);

        this.Click(s_bodyNext);
        _scene.Look.BodyType.Should().Be(1, "the right half is the second body");
        this.Click(s_bodyNext);
        _scene.Look.BodyType.Should().Be(1, "clicking the chosen half again keeps it");
        this.Click(leftHalf);
        _scene.Look.BodyType.Should().Be(0, "the left half is the first body");
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

    private void Scroll(Vector2 at, float wheel)
    {
        _mouse = new MouseSnapshot(at, at, new Vector2(0f, wheel), new bool[8], new bool[8]);
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

    private static string TwoRaceJson()
    {
        static string Option(int n) =>
            $$"""{"code":"o{{n}}","nameKey":"test.option{{n}}","role":"hair","choices":[{"nameKey":"appearance.hair.crop","part":"a"},{"nameKey":"appearance.hair.long","part":"b"}]}""";
        string four = string.Join(',', Enumerable.Range(0, 4).Select(Option));
        string six = string.Join(',', Enumerable.Range(0, 6).Select(Option));
        return $$"""
            {"schemaVersion":1,"races":[
              {"id":1,"code":"human","nameKey":"race.human",
               "bodies":[{"id":0,"code":"male","nameKey":"race.body.male","rig":"characters/base_hero.cvg.json"}],"options":[{{four}}]},
              {"id":4242,"code":"test_kin","nameKey":"race.test_kin",
               "bodies":[{"id":5,"code":"slim","nameKey":"race.body.slim","rig":"characters/test_kin_slim.cvg.json"}],"options":[{{six}}]}]}
            """;
    }
}
