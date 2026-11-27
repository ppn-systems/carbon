// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Capture;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.Fonts;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Shared frame of the account menus (start, login, register, server; ADR-037): animated backdrop, title, one
/// nine-slice window, typed-text routing, backspace repeat and a status line.
/// </summary>
internal abstract class MenuScene : GameScene
{
    /// <summary>
    /// <c>CARBON_UI_PREVIEW</c> value that opens the login or register form with sample input showing inline errors, the
    /// caps-lock warning and the strength meter (screenshots only; nothing is sent).
    /// </summary>
    public const string FormFeedbackPreview = "form-feedback";

    private readonly MenuSceneCapture _capture;
    private readonly MenuTransition _transition;
    private IInputManager? _boundInput;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private bool _shiftDown;
    private ScaledFontCache? _fonts;
    private float? _uiScaleChoice;

    protected MenuScene(string name, MenuFlow flow, string captureName)
        : base(name)
    {
        this.Flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _capture = new MenuSceneCapture(flow.Options, captureName);
        _transition = new MenuTransition(flow.Options.MenuMotion);
    }

    protected MenuFlow Flow { get; }

    protected BitmapFont? Font => _fonts?.Font;

    /// <summary>
    /// Gets the screen pixels per layout unit (docs/art/menu-ui.md rule 21): layouts work on a canvas of the window size
    /// divided by this, so the 1280×720 design fills a 1080p window at 1.5.
    /// </summary>
    internal float UiScale { get; private set; } = 1f;

    /// <summary>Gets the menu window rectangle from the last layout, in canvas units (tests check it fits).</summary>
    internal RectangleF PanelBounds { get; private set; }

    /// <summary>Gets the font cache (tests check a steady frame never re-bakes).</summary>
    internal ScaledFontCache? Fonts => _fonts;

    protected CarbonUiSkin? Skin { get; private set; }

    protected GeometryBatch? Geometry { get; private set; }

    protected float Elapsed { get; private set; }

    protected string StatusMessage { get; set; } = string.Empty;

    protected bool StatusIsError { get; set; }

    /// <summary>Gets the caps-lock state inferred from typed letters (password fields warn with it).</summary>
    protected CapsLockTracker CapsLock { get; } = new();

    /// <summary>Gets or sets whether this scene is handing over to the world (its <c>Leave</c> frees the backdrop's meshes).</summary>
    protected bool EnteringWorld { get; set; }

    /// <summary>Gets whether the form-feedback screenshot preview is on.</summary>
    protected bool IsFormFeedbackPreview => string.Equals(this.Flow.Options.UiPreview, FormFeedbackPreview, StringComparison.Ordinal);

    /// <summary>Gets the keyboard hint shown in the footer bar, or <see langword="null"/> for none.</summary>
    protected virtual string? FooterHint => null;

    /// <summary>Gets the panel's entrance (tests read its progress).</summary>
    internal MenuTransition Transition => _transition;

    /// <summary>Gets the panel heading drawn centred over a divider, or <see langword="null"/> for none.</summary>
    protected virtual string? Header => null;

    /// <summary>Gets a value indicating whether the screen shows its final content (for screenshot capture).</summary>
    protected virtual bool ReadyForCapture => true;

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);
        this.Geometry = new GeometryBatch(context.Device);
        this.Skin = CarbonUiSkin.Load(context.Device);
        IGraphicsDevice device = context.Device;
        this.UseFonts(new ScaledFontCache(scale => GameFontLoader.LoadFont(device, locale: null, scale)), context.WindowWidth, context.WindowHeight);
    }

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Enter(context);
        this.DetachInput();
        _boundInput = context.Input;
        _boundInput.CharacterReceived += this.ReceiveCharacter;
        _transition.Restart();
        this.Flow.Backdrop.Resume();
    }

    /// <inheritdoc/>
    public override void Leave(SceneContext context)
    {
        this.DetachInput();
        this.Flow.Backdrop.Pause();
        if (this.EnteringWorld)
        {
            // No menu draws the backdrop again until the player comes back; free its meshes for the world.
            this.Flow.Backdrop.ReleaseGpu();
        }

        base.Leave(context);
    }

    /// <inheritdoc/>
    public override void Update(SceneContext context, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Update(context, dt);
        this.Elapsed += (float)dt;
        _transition.Advance((float)dt);
        this.Flow.Backdrop.Advance((float)dt);
        this.ApplyScale(context.WindowWidth, context.WindowHeight);
        this.PanelBounds = this.Layout(this.CanvasWidth, this.CanvasHeight);
        KeyboardSnapshot kb = context.Input.Keyboard;
        _shiftDown = kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight);
        if (kb.IsKeyPressed(Key.CapsLock))
        {
            this.CapsLock.ObserveCapsLockKey();
        }

        this.HandleBackspaceRepeat(kb, (float)dt);
        this.UpdateMenu(context, (float)dt);
    }

    /// <inheritdoc/>
    public override void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spriteBatch);
        if (this.Geometry is not { } geometry || this.Font is not { } font || this.Skin is not { } skin)
        {
            return;
        }

        int width = context.WindowWidth;
        int height = context.WindowHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        context.Device.SetClearColor(new Vector4(0.04f, 0.06f, 0.11f, 1f));
        context.Device.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        this.ApplyScale(width, height);
        RectangleF card = this.Layout(this.CanvasWidth, this.CanvasHeight);
        this.PanelBounds = card;
        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, width / this.UiScale, height / this.UiScale, 0f, -1f, 1f);
        int canvasWidth = this.CanvasWidth;
        int canvasHeight = this.CanvasHeight;

        // The panel slides in while the backdrop, title and footer stay put (docs/art/menu-ui.md rule 16).
        Matrix4x4 panel = Matrix4x4.CreateTranslation(0f, _transition.Offset, 0f) * proj;
        RectangleF statusPill = new(card.X + 28f, card.Bottom - 46f, card.Width - 56f, 30f);
        bool showStatus = this.StatusMessage.Length > 0;

        this.Flow.Backdrop.Render(context.Device, geometry, width, height);

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: panel);
        skin.DrawWindow(spriteBatch, in card, CarbonUiPalette.BorderOuter, bodyAlpha: 242);
        this.DrawSkins(spriteBatch, skin);
        if (showStatus)
        {
            skin.DrawInset(spriteBatch, in statusPill, CarbonUiPalette.SlotFill, this.StatusIsError ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderSubtle);
        }

        spriteBatch.End();

        geometry.Begin(panel);
        if (this.Header is not null)
        {
            float cx = card.X + (card.Width * 0.5f);
            float dividerY = card.Y + 54f;
            geometry.FillRectangle(new RectangleF(card.X + 28f, dividerY, card.Width - 56f, 1f), MenuPalette.Divider);
            MenuIcons.DrawDiamond(geometry, new Vector2(cx, dividerY + 0.5f), 4f, CarbonUiPalette.BorderActive);
        }

        this.DrawGeometry(geometry, font);
        geometry.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        ColorRgba black = CarbonUiPalette.TextOutline;
        DrawTitle(spriteBatch, font, canvasWidth, TitleTop(canvasHeight));
        this.DrawFooter(spriteBatch, font, canvasWidth, canvasHeight);
        spriteBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: panel);
        if (this.Header is { } header)
        {
            TextMeasureResult headerMeasure = font.MeasureString(header, TextLayoutOptions.Default, 1f);
            spriteBatch.DrawOutlinedString(font, header, new Vector2(MathF.Floor(card.X + ((card.Width - headerMeasure.Width) * 0.5f)), card.Y + 26f), CarbonUiPalette.TextWarm, black, 1, 1f);
        }

        this.DrawTexts(spriteBatch, font);
        if (showStatus)
        {
            ColorRgba statusColor = this.StatusIsError ? MenuPalette.ErrorText : CarbonUiPalette.TextPrimary;
            string msg = CarbonUiSkin.TruncateToWidth(font, this.StatusMessage, statusPill.Width - 20f, 1f);
            spriteBatch.DrawOutlinedString(font, msg, new Vector2(statusPill.X + 10f, statusPill.Y + 7f), statusColor, black, 1, 1f);
        }

        spriteBatch.End();

        if (_transition.Veil > 0.01f)
        {
            // The contents rise out of the panel body: a body-coloured veil over them fades away.
            geometry.Begin(panel);
            RectangleF inner = new(card.X + 8f, card.Y + 8f, card.Width - 16f, card.Height - 16f);
            geometry.FillRectangle(in inner, MenuPalette.WithAlpha(CarbonUiPalette.WindowFill with { A = 255 }, _transition.Veil));
            geometry.End();
        }

        base.Render(context, spriteBatch, dt);
        _capture.AfterRender(context, this.ReadyForCapture && _transition.IsDone && this.Flow.Backdrop.IsSettled(expectWorld: this.Flow.Session.GameConfig is not null));
    }

    /// <summary>Gets the canvas width in layout units (the window width divided by <see cref="UiScale"/>).</summary>
    protected int CanvasWidth { get; private set; }

    /// <summary>Gets the canvas height in layout units (the window height divided by <see cref="UiScale"/>).</summary>
    protected int CanvasHeight { get; private set; }

    /// <summary>
    /// Takes over <paramref name="fonts"/> and reads the player's UI scale choice: once per screen, never per frame. Load
    /// calls it with the real font loader; tests hand in a fake one.
    /// </summary>
    internal void UseFonts(ScaledFontCache fonts, int windowWidth, int windowHeight)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts?.Dispose();
        _fonts = fonts;
        _uiScaleChoice = this.Flow.ReadUiScaleChoice();
        this.ApplyScale(windowWidth, windowHeight);
    }

    /// <summary>Maps the mouse from window pixels to canvas units, where every hit rectangle lives.</summary>
    protected Vector2 PointerPosition(in MouseSnapshot mouse) => mouse.Position / this.UiScale;

    /// <summary>Places the controls for the canvas size and returns the menu window rectangle.</summary>
    protected abstract RectangleF Layout(int width, int height);

    /// <summary>Handles input and pending work for one frame (layout is already done).</summary>
    protected abstract void UpdateMenu(SceneContext context, float dt);

    /// <summary>Draws nine-slice skins (fields, buttons).</summary>
    protected abstract void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin);

    /// <summary>Draws vector geometry (cursors, check marks).</summary>
    protected virtual void DrawGeometry(GeometryBatch geometry, BitmapFont font)
    {
    }

    /// <summary>Draws text.</summary>
    protected abstract void DrawTexts(SpriteBatch spriteBatch, BitmapFont font);

    /// <summary>Receives a typed character.</summary>
    protected virtual void OnCharacter(char ch)
    {
    }

    /// <summary>Deletes one character from the focused field.</summary>
    protected virtual void OnBackspace()
    {
    }

    /// <summary>Switches to the scene <paramref name="create"/> builds, disposing it if the switch throws.</summary>
    protected static void ChangeTo(SceneContext context, Func<GameScene> create)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(create);
        GameScene? pending = null;
        try
        {
            pending = create();

            // Menu to menu: no fade to black, the shared backdrop and title stay and the new panel slides in.
            context.SceneManager.ChangeScene(pending, transitionDuration: pending is MenuScene ? 0f : 0.15f);
            pending = null;
        }
        finally
        {
            pending?.Dispose();
        }
    }

    /// <summary>Shows a status line; <paramref name="isError"/> frames it red.</summary>
    protected void SetStatus(string message, bool isError)
    {
        this.StatusMessage = message;
        this.StatusIsError = isError;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.DetachInput();
            this.Skin?.Dispose();
            this.Skin = null;
            this.Geometry?.Dispose();
            this.Geometry = null;
            _fonts?.Dispose();
            _fonts = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Resolves the UI scale for the window (re-baking the font only when it changes) and the canvas it leaves.
    /// </summary>
    private void ApplyScale(int windowWidth, int windowHeight)
    {
        float scale = Rendering.UiSkin.UiScale.Resolve(_uiScaleChoice, windowWidth, windowHeight);
        this.UiScale = scale;
        this.CanvasWidth = (int)MathF.Floor(windowWidth / scale);
        this.CanvasHeight = (int)MathF.Floor(windowHeight / scale);
        _ = _fonts?.Get(scale);
        if (this.Skin is { } skin)
        {
            skin.PixelScale = scale;
        }
    }

    private void HandleBackspaceRepeat(in KeyboardSnapshot kb, float dt)
    {
        if (kb.IsKeyPressed(Key.Backspace))
        {
            this.OnBackspace();
            _backspaceHoldTime = 0f;
            _backspaceRepeatTime = 0f;
            return;
        }

        if (!kb.IsKeyDown(Key.Backspace))
        {
            _backspaceHoldTime = 0f;
            _backspaceRepeatTime = 0f;
            return;
        }

        _backspaceHoldTime += dt;
        if (_backspaceHoldTime >= 0.38f)
        {
            _backspaceRepeatTime += dt;
            if (_backspaceRepeatTime >= 0.055f)
            {
                _backspaceRepeatTime = 0f;
                this.OnBackspace();
            }
        }
    }

    /// <summary>Top of the title block: the same on every menu screen, so it never jumps when the screen changes.</summary>
    protected static float TitleTop(int height) => MathF.Max(12f, MathF.Floor((height - 600f) * 0.5f));

    /// <summary>Top of a panel <paramref name="panelHeight"/> tall: centred a little low, never over the title block.</summary>
    protected static float PanelTop(int height, float panelHeight) =>
        MathF.Max(TitleTop(height) + 92f, MathF.Floor((height - panelHeight) * 0.5f) + 40f);

    private static void DrawTitle(SpriteBatch spriteBatch, BitmapFont font, int width, float top)
    {
        ColorRgba black = CarbonUiPalette.TextOutline;
        string title = GameText.Get("login.title");
        TextMeasureResult titleMeasure = font.MeasureString(title, TextLayoutOptions.Default, 3f);
        Vector2 titlePos = new(MathF.Floor((width - titleMeasure.Width) * 0.5f), top);
        spriteBatch.DrawOutlinedString(font, title, titlePos + new Vector2(0f, 3f), new ColorRgba(60, 40, 18, 200), black, 2, 3f);
        spriteBatch.DrawOutlinedString(font, title, titlePos, CarbonUiPalette.TextWarm, black, 2, 3f);
        string subtitle = GameText.Get("login.subtitle");
        TextMeasureResult subMeasure = font.MeasureString(subtitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, subtitle, new Vector2(MathF.Floor((width - subMeasure.Width) * 0.5f), top + 58f), CarbonUiPalette.TextMuted, black, 1, 1f);
    }

    private void DrawFooter(SpriteBatch spriteBatch, BitmapFont font, int width, int height)
    {
        if (this.FooterHint is not { Length: > 0 } hint)
        {
            return;
        }

        string fitted = CarbonUiSkin.TruncateToWidth(font, hint, width - 32f, 1f);
        TextMeasureResult measure = font.MeasureString(fitted, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, fitted, new Vector2(MathF.Floor((width - measure.Width) * 0.5f), height - 30f), CarbonUiPalette.TextMuted, CarbonUiPalette.TextOutline, 1, 1f);
    }

    private void ReceiveCharacter(char ch)
    {
        this.CapsLock.ObserveCharacter(ch, _shiftDown);
        this.OnCharacter(ch);
    }

    private void DetachInput()
    {
        if (_boundInput is { } bound)
        {
            bound.CharacterReceived -= this.ReceiveCharacter;
            _boundInput = null;
        }
    }
}
