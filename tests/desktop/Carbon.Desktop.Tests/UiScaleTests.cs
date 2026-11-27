// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.Fonts;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Scenes;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Fonts.TrueType;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.UI.NineSlice;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Carbon.Testing;
using FluentAssertions;
using NSubstitute;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The UI scale for 1080p and up (docs/art/menu-ui.md rule 21): the scale picked from the window, the font re-baked only
/// when it changes, clicks mapped from window pixels to canvas units, every menu fitting its canvas, and text laying out
/// the same at every scale.
/// </summary>
public sealed class UiScaleTests : IDisposable
{
    private readonly IInputManager _input = Substitute.For<IInputManager>();
    private readonly ISceneManager _scenes = Substitute.For<ISceneManager>();
    private readonly IGameWindow _window = Substitute.For<IGameWindow>();
    private readonly IWorldSession _session = Substitute.For<IWorldSession>();
    private readonly List<IScene> _changes = [];
    private readonly List<IDisposable> _owned = [];
    private readonly SceneContext _context;
    private MouseSnapshot _mouse = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, new bool[8], new bool[8]);

    public UiScaleTests()
    {
        _input.Keyboard.Returns(new KeyboardSnapshot(new bool[512], new bool[512]));
        _input.Mouse.Returns(_ => _mouse);
        _window.Input.Returns(_input);
        this.Resize(1920, 1080);
        _session.ListCharactersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<CharacterRosterEntry>>([]));
        _session.Races.Returns(TestRaces.Synced());
        _scenes.When(m => m.ChangeScene(Arg.Any<IScene>(), Arg.Any<SceneTransitionData?>(), Arg.Any<float>()))
            .Do(call => _changes.Add(call.Arg<IScene>()));
        _context = new SceneContext(_window, new Camera2D(1920f, 1080f), _scenes);
    }

    public void Dispose()
    {
        foreach (IScene scene in _changes)
        {
            scene.Dispose();
        }

        foreach (IDisposable owned in _owned)
        {
            owned.Dispose();
        }

        _scenes.Dispose();
        _window.Dispose();
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData(1280, 720, 1f)]
    [InlineData(1366, 768, 1f)]
    [InlineData(1600, 900, 1.25f)]
    [InlineData(1920, 1080, 1.5f)]
    [InlineData(1920, 1200, 1.5f)]
    [InlineData(2560, 1440, 2f)]
    [InlineData(3840, 2160, 2f)]
    [InlineData(1024, 600, 1f)]
    [InlineData(1440, 1080, 1f)]
    public void UiScaleFit_PicksTheLargestPresetWhoseCanvasFitsTheWindow(int width, int height, float expected) =>
        UiScale.Fit(width, height).Should().Be(expected);

    [Theory]
    [InlineData(null, 1.5f)]
    [InlineData(1f, 1f)]
    [InlineData(1.25f, 1.25f)]
    [InlineData(2f, 1.5f)]
    public void UiScaleResolve_AtFullHd_HonoursAChoiceThatFitsAndCapsOneThatDoesNot(float? choice, float expected) =>
        UiScale.Resolve(choice, 1920, 1080).Should().Be(expected, "a choice the window cannot fit would cut the menus off");

    [Theory]
    [InlineData("auto", null)]
    [InlineData("1.25", 1.25f)]
    [InlineData("2", 2f)]
    [InlineData("1.3", null)]
    [InlineData("huge", null)]
    public void UiSettings_UiScale_RoundTripsAndFallsBackToAuto(string written, float? expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-ui-scale-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, $"[display]\nui_scale = {written}\n");
            UiSettings loaded = UiSettings.LoadOrDefault(path);
            loaded.UiScaleChoice.Should().Be(expected);

            loaded.UiScaleChoice = 1.5f;
            loaded.Save(path);
            UiSettings.LoadOrDefault(path).UiScaleChoice.Should().Be(1.5f);
            File.ReadAllText(path).Should().Contain("ui_scale = 1.5");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StartScene_AtFullHd_MapsAClickFromWindowPixelsToTheButtonUnderIt()
    {
        StartScene start = this.Own(new StartScene(this.Flow()));
        start.Enter(_context);
        start.Update(_context, 0.016);
        start.UiScale.Should().Be(1.5f);
        RectangleF register = start.ButtonBounds(StartMenuAction.Register);
        Vector2 registerCentre = new(register.X + (register.Width * 0.5f), register.Y + (register.Height * 0.5f));

        // Where the Register button would be without the scale lies over nothing (or another control) on screen.
        this.Click(registerCentre);
        start.Update(_context, 0.016);
        _changes.Should().NotContain(scene => scene is RegisterScene, "window pixels are not canvas units");

        this.Click(registerCentre * 1.5f);
        start.Update(_context, 0.016);
        _changes.Should().ContainSingle(scene => scene is RegisterScene, "the click lands on Register as drawn at 1.5x");
    }

    [Fact]
    public void CharacterSelectScene_AtFullHd_MapsAClickToTheTurnButtonAsDrawn()
    {
        // Turn right sits at (684, 568) on the 1280x720 canvas (CharacterSelectSceneTests); at 1.5x it is drawn at 1.5 times that.
        CharacterSelectScene scene = this.Own(new CharacterSelectScene(this.Flow()));
        scene.Enter(_context);
        scene.Update(_context, 0.016);
        scene.UiScale.Should().Be(1.5f);
        int before = scene.TurnIndex;

        this.Click(new Vector2(684f, 568f) * 1.5f);
        scene.Update(_context, 0.016);

        scene.TurnIndex.Should().NotBe(before, "the click lands on the turn button as drawn at 1.5x");
    }

    public static TheoryData<string, int, int, float> Screens()
    {
        TheoryData<string, int, int, float> data = [];
        foreach (string screen in new[] { "start", "login", "register", "server", "characters" })
        {
            data.Add(screen, 1280, 720, 1f);
            data.Add(screen, 1920, 1080, 1.5f);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryMenu_FitsItsCanvas(string screen, int width, int height, float scale)
    {
        this.Resize(width, height);
        MenuFlow flow = this.Flow();
        (float uiScale, RectangleF panel) = screen switch
        {
            "characters" => this.Settle(new CharacterSelectScene(flow), s => (s.UiScale, s.PanelBounds)),
            "start" => this.Settle(new StartScene(flow), s => (s.UiScale, s.PanelBounds)),
            "login" => this.Settle(new LoginScene(flow), s => (s.UiScale, s.PanelBounds)),
            "register" => this.Settle(new RegisterScene(flow), s => (s.UiScale, s.PanelBounds)),
            _ => this.Settle(new ServerSelectScene(flow), s => (s.UiScale, s.PanelBounds)),
        };

        uiScale.Should().Be(scale);
        float canvasWidth = width / scale;
        float canvasHeight = height / scale;
        canvasWidth.Should().Be(1280f, "both windows leave the 1280x720 canvas the menus are designed for");
        canvasHeight.Should().Be(720f);
        panel.Width.Should().BePositive();
        panel.X.Should().BeGreaterThanOrEqualTo(0f);
        panel.Y.Should().BeGreaterThanOrEqualTo(0f);
        panel.Right.Should().BeLessThanOrEqualTo(canvasWidth);
        panel.Bottom.Should().BeLessThanOrEqualTo(canvasHeight - 36f, "the footer hint line sits under the panel");
    }

    [Fact]
    public void MenuScene_SteadyFrames_NeverRebakeTheFont_AndAResizeRebakesOnce()
    {
        LoginScene login = this.Own(new LoginScene(this.Flow()));
        ScaledFontCache fonts = this.Own(new ScaledFontCache(FakeFont));
        login.UseFonts(fonts, 1920, 1080);
        login.Enter(_context);
        for (int frame = 0; frame < 30; frame++)
        {
            login.Update(_context, 0.016);
        }

        fonts.BakeCount.Should().Be(1, "the font is baked when the screen loads, never per frame");
        fonts.Scale.Should().Be(1.5f);

        this.Resize(2560, 1440);
        login.Update(_context, 0.016);
        login.Update(_context, 0.016);
        fonts.BakeCount.Should().Be(2, "a new scale re-bakes once");
        fonts.Scale.Should().Be(2f);
    }

    [Fact]
    public void ScaledFontCache_SteadyFrame_AllocatesNothing()
    {
        using ScaledFontCache fonts = new(FakeFont);
        _ = fonts.Get(1.5f);

        AllocationResult window = AllocationProbe.Measure(1000, warmupIterations: 1000, i =>
        {
            float scale = UiScale.Resolve(null, 1920, 1080);
            _ = fonts.Get(scale);
        });

        window.Bytes.Should().Be(0, window.ToString());
        fonts.BakeCount.Should().Be(1);
    }

    [Fact]
    public void MenuScene_ReadsTheChoiceOncePerScreen()
    {
        int reads = 0;
        MenuFlow flow = this.Flow(() =>
        {
            reads++;
            return 1.25f;
        });
        LoginScene login = this.Own(new LoginScene(flow));
        login.UseFonts(this.Own(new ScaledFontCache(FakeFont)), 1920, 1080);
        login.Enter(_context);
        login.Update(_context, 0.016);
        login.Update(_context, 0.016);

        login.UiScale.Should().Be(1.25f, "the player's choice beats Auto");
        reads.Should().Be(1, "settings.ini is read when the screen loads, not every frame");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    public void EveryString_LaysOutAtEveryScaleAsItDoesAtOne(string code)
    {
        LocaleInfo locale = FindLocale(code);
        string fontsDirectory = GameFontLoader.TryFindFontsDirectory() ?? throw new InvalidOperationException("resources/fonts not found");
        List<FontFaceSource> faces = GameFontLoader.LoadFaces(locale, fontsDirectory);
        faces.Should().NotBeEmpty();
        using BitmapFont baseline = Bake(faces, 1f);
        IReadOnlyList<IniEntry> strings = IniDocument.Load(Path.Combine(Path.GetDirectoryName(fontsDirectory)!, "localization", code + ".ini")).Entries;
        strings.Count.Should().BeGreaterThan(500);

        foreach (float scale in UiScale.Presets)
        {
            using BitmapFont scaled = Bake(faces, scale);
            scaled.TexelsPerUnit.Should().Be(scale);
            scaled.LineHeight.Should().BeApproximately(baseline.LineHeight, 1f);
            foreach (IniEntry entry in strings)
            {
                float expected = baseline.MeasureString(entry.Value, TextLayoutOptions.Default, 1f).Width;
                float actual = scaled.MeasureString(entry.Value, TextLayoutOptions.Default, 1f).Width;
                actual.Should().BeApproximately(expected, 0.05f, $"'{entry.FullKey}' must fit wherever it fits at 1x (scale {scale})");
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    public void SettingsUiScaleButtons_FitTheirLabels(string code)
    {
        LocaleInfo locale = FindLocale(code);
        string fontsDirectory = GameFontLoader.TryFindFontsDirectory()!;
        using BitmapFont font = Bake(GameFontLoader.LoadFaces(locale, fontsDirectory), 1f);
        IniDocument strings = IniDocument.Load(Path.Combine(Path.GetDirectoryName(fontsDirectory)!, "localization", code + ".ini"));
        string[] keys = ["settings.ui_scale.auto", "settings.ui_scale.x1", "settings.ui_scale.x1_25", "settings.ui_scale.x1_5", "settings.ui_scale.x2"];
        for (int i = 0; i < keys.Length; i++)
        {
            strings.TryGetValue(keys[i], out string label).Should().BeTrue(keys[i]);
            RectangleF button = SettingsModalWidget.GetUiScaleButtonRect(0f, 0f, i);
            font.MeasureString(label, TextLayoutOptions.Default, 1f).Width.Should().BeLessThanOrEqualTo(button.Width - 8f, $"'{label}' fits its button");
            SettingsModalWidget.UiScaleChoiceAt(i).Should().Be(i == 0 ? null : UiScale.Presets[i - 1]);
        }

        RectangleF last = SettingsModalWidget.GetUiScaleButtonRect(0f, 0f, keys.Length - 1);
        RectangleF quality = SettingsModalWidget.GetQualityButtonRect(0f, 0f, 2);
        last.Right.Should().Be(quality.Right, "the row lines up with the rows above it");
        last.Y.Should().BeGreaterThanOrEqualTo(quality.Bottom + 4f);
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(1.25f, 0.8f)]
    [InlineData(1.5f, 1f / 1.5f)]
    [InlineData(2f, 1f)]
    public void CarbonUiSkinCorners_DrawAWholeNumberOfScreenPixelsPerTexel(float scale, float cornerScale)
    {
        CarbonUiSkin.CornerScale(scale).Should().BeApproximately(cornerScale, 1e-6f);
        (CarbonUiSkin.CornerScale(scale) * scale % 1f).Should().BeApproximately(0f, 1e-5f);

        RectangleF bounds = new(100.3f, 50.45f, 200.2f, 40.1f);
        RectangleF snapped = CarbonUiSkin.SnapToPixels(in bounds, scale);
        if (scale == 1f)
        {
            snapped.Should().Be(bounds, "at 1x panels draw exactly where they always did");
        }
        else
        {
            foreach (float edge in new[] { snapped.X, snapped.Y, snapped.Right, snapped.Bottom })
            {
                float pixels = edge * scale;
                pixels.Should().BeApproximately(MathF.Round(pixels), 1e-3f, "every panel edge falls on a screen pixel");
            }
        }

        NineSliceGrid grid = NineSliceGrid.Create(new RectangleF(0f, 0f, 30f, 30f), new NineSliceMargins(10f), snapped, CarbonUiSkin.CornerScale(scale));
        (grid.DestTopLeft.Width * scale).Should().BeApproximately(10f * MathF.Max(1f, MathF.Floor(scale)), 1e-3f);
    }

    private static BitmapFont FakeFont(float scale) =>
        new("fake", new BitmapFontAtlas(Substitute.For<ITexture>(), scale), new FontMetrics(20f, 20f, 16f, 8f));

    private static BitmapFont Bake(IReadOnlyList<FontFaceSource> faces, float scale) =>
        TrueTypeFontLoader.CreateFont(TrueTypeFontLoader.RasterizeAtScale("test", faces, UnicodeGlyphRanges.LatinFull, scale), Substitute.For<ITexture>(), scale);

    private static LocaleInfo FindLocale(string code)
    {
        foreach (LocaleInfo locale in GameText.Current.Locales)
        {
            if (string.Equals(locale.Code, code, StringComparison.Ordinal))
            {
                return locale;
            }
        }

        throw new InvalidOperationException($"locale {code} missing");
    }

    private MenuFlow Flow(Func<float?>? readChoice = null) =>
        new(_session, new DesktopOptions(), new Camera2D(1280f, 720f), Substitute.For<IRememberedAccountStore>(), ServerDirectory.FromIni(null, "127.0.0.1", 7777), new SilentProbe(), readChoice);

    private (float Scale, RectangleF Panel) Settle<TScene>(TScene scene, Func<TScene, (float, RectangleF)> read)
        where TScene : GameScene
    {
        this.Own(scene);
        scene.Enter(_context);
        scene.Update(_context, 0.016);
        return read(scene);
    }

    private TScene Own<TScene>(TScene scene)
        where TScene : IDisposable
    {
        _owned.Add(scene);
        return scene;
    }

    private void Resize(int width, int height)
    {
        _window.Width.Returns(width);
        _window.Height.Returns(height);
    }

    private sealed class SilentProbe : IServerProbe
    {
        public Task<TimeSpan?> ProbeAsync(GameServer server, CancellationToken ct) => new TaskCompletionSource<TimeSpan?>().Task;
    }

    private void Click(Vector2 at)
    {
        bool[] down = new bool[8];
        down[(int)MouseButton.Left] = true;
        _mouse = new MouseSnapshot(at, at, Vector2.Zero, down, new bool[8]);
    }
}
