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
using Carbon.Graphics.OpenGL.Textures;
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
    private IInputManager? _boundInput;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private bool _shiftDown;
    private Texture2D? _whitePixel;

    protected MenuScene(string name, MenuFlow flow, string captureName)
        : base(name)
    {
        this.Flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _capture = new MenuSceneCapture(flow.Options, captureName);
    }

    protected MenuFlow Flow { get; }

    protected BitmapFont? Font { get; private set; }

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
        (BitmapFont font, Texture2D whitePixel) = GameFontLoader.Load(context.Device);
        this.Font = font;
        _whitePixel = whitePixel;
        this.Skin = CarbonUiSkin.Load(context.Device);
    }

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Enter(context);
        this.DetachInput();
        _boundInput = context.Input;
        _boundInput.CharacterReceived += this.ReceiveCharacter;
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
        this.Flow.Backdrop.Advance((float)dt);
        _ = this.Layout(context.WindowWidth, context.WindowHeight);
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

        RectangleF card = this.Layout(width, height);
        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);
        RectangleF statusPill = new(card.X + 28f, card.Bottom - 46f, card.Width - 56f, 30f);
        bool showStatus = this.StatusMessage.Length > 0;

        this.Flow.Backdrop.Render(context.Device, geometry, width, height);

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        skin.DrawWindow(spriteBatch, in card, CarbonUiPalette.BorderOuter, bodyAlpha: 242);
        this.DrawSkins(spriteBatch, skin);
        if (showStatus)
        {
            skin.DrawInset(spriteBatch, in statusPill, CarbonUiPalette.SlotFill, this.StatusIsError ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderSubtle);
        }

        spriteBatch.End();

        geometry.Begin(proj);
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
        string title = GameText.Get("login.title");
        TextMeasureResult titleMeasure = font.MeasureString(title, TextLayoutOptions.Default, 3f);
        Vector2 titlePos = new(MathF.Floor((width - titleMeasure.Width) * 0.5f), card.Y - 96f);
        spriteBatch.DrawOutlinedString(font, title, titlePos + new Vector2(0f, 3f), new ColorRgba(60, 40, 18, 200), black, 2, 3f);
        spriteBatch.DrawOutlinedString(font, title, titlePos, CarbonUiPalette.TextWarm, black, 2, 3f);
        string subtitle = GameText.Get("login.subtitle");
        TextMeasureResult subMeasure = font.MeasureString(subtitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, subtitle, new Vector2(MathF.Floor((width - subMeasure.Width) * 0.5f), card.Y - 30f), CarbonUiPalette.TextMuted, black, 1, 1f);
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

        base.Render(context, spriteBatch, dt);
        _capture.AfterRender(context, this.ReadyForCapture && this.Flow.Backdrop.IsSettled(expectWorld: this.Flow.Session.GameConfig is not null));
    }

    /// <summary>Places the controls for the window size and returns the menu window rectangle.</summary>
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
            context.SceneManager.ChangeScene(pending, transitionDuration: 0.15f);
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
            this.Font?.Dispose();
            this.Font = null;
            _whitePixel?.Dispose();
            _whitePixel = null;
        }

        base.Dispose(disposing);
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
