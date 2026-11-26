// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Camera;
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
/// Online authentication scene with standardized 9-slice skinning, explicit Sign-In / Create-Account tabs,
/// password confirmation &amp; reveal toggle, gateway endpoint editor, and fast AutoLogin path.
/// </summary>
public sealed class LoginScene : GameScene
{
    private readonly IClientWorldSession _session;
    private readonly DesktopClientOptions _options;
    private readonly Camera2D _camera;

    private readonly MenuTextField _usernameField;
    private readonly MenuTextField _passwordField;
    private readonly MenuTextField _confirmPasswordField;
    private readonly MenuTextField _serverField;

    private readonly MenuButton _signInTab = new("SIGN IN", MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _registerTab = new("CREATE ACCOUNT", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _toggleMaskBtn = new("SHOW", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _submitButton = new("SIGN IN TO REALM", MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _editGatewayBtn = new("EDIT", MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _quitButton = new("QUIT", MenuButtonStyle.DangerRed);

    private GeometryBatch? _geometryBatch;
    private BitmapFont? _font;
    private Texture2D? _whitePixel;
    private CarbonUiSkin? _uiSkin;
    private IInputManager? _boundInput;

    private Task<ClientAuthResult>? _pendingAuthTask;
    private Task? _pendingAutoEnterTask;
    private bool _registerMode;
    private bool _showGatewayEditor;
    private bool _autoStarted;
    private float _elapsed;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private string _statusMessage = "Ready. Sign in or create an online account.";
    private bool _statusIsError;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginScene"/> class.
    /// </summary>
    public LoginScene(IClientWorldSession session, DesktopClientOptions options, Camera2D camera)
        : base("Login")
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));

        _usernameField = new MenuTextField("ACCOUNT USERNAME", "3-24 letters or digits", options.Username, isPassword: false, maxLength: 24)
        {
            IsFocused = true,
            CharacterFilter = static ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.',
        };
        _passwordField = new MenuTextField("PASSWORD", "min 6 chars", options.Password, isPassword: true, maxLength: 64);
        _confirmPasswordField = new MenuTextField("CONFIRM PASSWORD", "repeat password", string.Empty, isPassword: true, maxLength: 64);
        _serverField = new MenuTextField("SERVER GATEWAY (HOST:PORT)", "127.0.0.1:57207", $"{options.ServerHost}:{options.ServerTcpPort}", isPassword: false, maxLength: 96);
    }

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);

        _geometryBatch = new GeometryBatch(context.Device);
        (_font, _whitePixel) = GameFontLoader.Load(context.Device);
        _uiSkin = CarbonUiSkin.Load(context.Device);
    }

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Enter(context);

        this.AttachInput(context.Input);

        if (_options.AutoLogin && !_autoStarted)
        {
            _autoStarted = true;
            _statusIsError = false;
            _statusMessage = $"Auto-signing in to {_options.ServerHost}:{_options.ServerTcpPort}...";
            _pendingAutoEnterTask = _session.InitializeAsync();
        }
    }

    /// <inheritdoc/>
    public override void Leave(SceneContext context)
    {
        this.DetachInput();
        base.Leave(context);
    }

    /// <inheritdoc/>
    public override void Update(SceneContext context, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Update(context, dt);

        _elapsed += (float)dt;

        if (_pendingAutoEnterTask is not null && _pendingAutoEnterTask.IsCompleted)
        {
            Task completed = _pendingAutoEnterTask;
            _pendingAutoEnterTask = null;
            try
            {
                completed.GetAwaiter().GetResult();
                WorldGameplayScene? next = null;
                try
                {
                    next = new WorldGameplayScene(_session, _options, _camera);
                    context.SceneManager.ChangeScene(next, transitionDuration: 0.15f);
                    next = null;
                }
                finally
                {
                    next?.Dispose();
                }

                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _statusIsError = true;
                _statusMessage = ex.Message;
            }
        }

        if (_pendingAuthTask is not null && _pendingAuthTask.IsCompleted)
        {
            Task<ClientAuthResult> completedAuth = _pendingAuthTask;
            _pendingAuthTask = null;
            try
            {
                ClientAuthResult authResult = completedAuth.GetAwaiter().GetResult();
                if (authResult.Succeeded)
                {
                    CharacterSelectScene? next = null;
                    try
                    {
                        next = new CharacterSelectScene(_session, _options, _camera);
                        context.SceneManager.ChangeScene(next, transitionDuration: 0.16f);
                        next = null;
                    }
                    finally
                    {
                        next?.Dispose();
                    }

                    return;
                }

                _statusIsError = true;
                _statusMessage = authResult.Message;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _statusIsError = true;
                _statusMessage = $"Authentication error: {ex.Message}";
            }
        }

        this.LayoutControls(context.WindowWidth, context.WindowHeight);

        KeyboardSnapshot kb = context.Input.Keyboard;
        MouseSnapshot mouse = context.Input.Mouse;
        bool busy = _pendingAuthTask is not null || _pendingAutoEnterTask is not null;

        if (kb.IsKeyPressed(Key.Escape))
        {
            if (this.UnfocusAll())
            {
                // First Escape clears active text focus cleanly.
            }
            else if (_showGatewayEditor)
            {
                _showGatewayEditor = false;
            }
            else
            {
                context.Window.Close();
                return;
            }
        }

        if (kb.IsKeyPressed(Key.Tab))
        {
            bool reverse = kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight);
            this.CycleFocus(reverse);
        }

        this.HandleBackspaceRepeat(kb, (float)dt);

        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position;

        _signInTab.IsEnabled = !busy;
        _registerTab.IsEnabled = !busy;
        _toggleMaskBtn.IsEnabled = !busy;
        _submitButton.IsEnabled = !busy;
        _editGatewayBtn.IsEnabled = !busy;
        _quitButton.IsEnabled = true;

        _signInTab.Style = _registerMode ? MenuButtonStyle.NeutralSlate : MenuButtonStyle.PrimaryGold;
        _registerTab.Style = _registerMode ? MenuButtonStyle.PrimaryGold : MenuButtonStyle.NeutralSlate;
        _submitButton.Text = _registerMode ? "CREATE ACCOUNT" : "SIGN IN TO REALM";
        _submitButton.Style = _registerMode ? MenuButtonStyle.PrimaryGold : MenuButtonStyle.SecondaryEmerald;
        _editGatewayBtn.Text = _showGatewayEditor ? "DONE" : "EDIT";

        if (_signInTab.Update(mousePos, leftDown, leftPressed) && _registerMode)
        {
            _registerMode = false;
            _confirmPasswordField.IsFocused = false;
            _statusIsError = false;
            _statusMessage = "Enter credentials to connect via TCP + Authenticated UDP.";
        }
        else if (_registerTab.Update(mousePos, leftDown, leftPressed) && !_registerMode)
        {
            _registerMode = true;
            _statusIsError = false;
            _statusMessage = "Choose a unique username (3-24 chars) and password.";
        }

        if (_toggleMaskBtn.Update(mousePos, leftDown, leftPressed))
        {
            bool showPlain = _passwordField.IsPassword;
            _passwordField.IsPassword = !showPlain;
            _confirmPasswordField.IsPassword = !showPlain;
            _toggleMaskBtn.Text = showPlain ? "HIDE" : "SHOW";
        }

        if (_editGatewayBtn.Update(mousePos, leftDown, leftPressed))
        {
            _showGatewayEditor = !_showGatewayEditor;
            if (_showGatewayEditor)
            {
                _ = this.UnfocusAll();
                _serverField.IsFocused = true;
            }
            else if (_serverField.IsFocused)
            {
                _serverField.IsFocused = false;
                _usernameField.IsFocused = true;
            }
        }

        if (_quitButton.Update(mousePos, leftDown, leftPressed))
        {
            context.Window.Close();
            return;
        }

        this.UpdateActiveFields((float)dt, mousePos, leftPressed && !busy);

        bool enterSubmit = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if (_submitButton.Update(mousePos, leftDown, leftPressed) || enterSubmit)
        {
            this.BeginAuth();
        }

        if (busy)
        {
            _statusIsError = false;
            _statusMessage = _pendingAutoEnterTask is not null
                ? $"Connecting & entering world ({_options.ServerHost}:{_options.ServerTcpPort})..."
                : (_registerMode ? "Creating account via TCP X25519..." : "Signing in via TCP X25519...");
        }
    }

    /// <inheritdoc/>
    public override void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_geometryBatch is null || _font is null || _uiSkin is null)
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

        this.LayoutControls(width, height);
        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);

        (float px, float py, float panelW, float panelH) = this.GetPanelRect(width, height);
        RectangleF card = new(px, py, panelW, panelH);
        RectangleF gatewayPill = new(px + 28f, py + panelH - 90f, 344f, 34f);
        RectangleF statusPill = new(px + 28f, py + panelH - 46f, panelW - 56f, 30f);

        // Pass 1: Animated twilight horizon backdrop
        _geometryBatch.Begin(proj);
        MenuBackdropRenderer.Draw(_geometryBatch, width, height, _elapsed);
        _geometryBatch.End();

        // Pass 2: Standardized 9-Slice Ornate Frame, Tabs, Fields & Buttons
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        _uiSkin.DrawWindow(spriteBatch, in card, CarbonUiPalette.BorderOuter, bodyAlpha: 242);
        _signInTab.DrawSkin(spriteBatch, _uiSkin);
        _registerTab.DrawSkin(spriteBatch, _uiSkin);
        _usernameField.DrawSkin(spriteBatch, _uiSkin);
        _passwordField.DrawSkin(spriteBatch, _uiSkin);
        _toggleMaskBtn.DrawSkin(spriteBatch, _uiSkin);
        if (_registerMode)
        {
            _confirmPasswordField.DrawSkin(spriteBatch, _uiSkin);
        }

        _submitButton.DrawSkin(spriteBatch, _uiSkin);
        if (_showGatewayEditor)
        {
            _serverField.DrawSkin(spriteBatch, _uiSkin);
        }
        else
        {
            _uiSkin.DrawInset(spriteBatch, in gatewayPill, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
        }

        _editGatewayBtn.DrawSkin(spriteBatch, _uiSkin);
        _quitButton.DrawSkin(spriteBatch, _uiSkin);

        ColorRgba statusFrame = _statusIsError
            ? CarbonUiPalette.DangerBorder
            : CarbonUiPalette.BorderSubtle;
        _uiSkin.DrawInset(spriteBatch, in statusPill, CarbonUiPalette.SlotFill, statusFrame);
        spriteBatch.End();

        // Pass 3: Focus cursors & server badge accent
        _geometryBatch.Begin(proj);
        _usernameField.DrawCursorGeometry(_geometryBatch, _font);
        _passwordField.DrawCursorGeometry(_geometryBatch, _font);
        if (_registerMode)
        {
            _confirmPasswordField.DrawCursorGeometry(_geometryBatch, _font);
        }

        if (_showGatewayEditor)
        {
            _serverField.DrawCursorGeometry(_geometryBatch, _font);
        }
        else
        {
            ColorRgba dotCol = _session.IsConnected ? CarbonUiPalette.StaminaFill : CarbonUiPalette.TextMuted;
            _geometryBatch.FillCircle(new Vector2(gatewayPill.X + 14f, gatewayPill.Y + 17f), 4f, dotCol, 10);
        }

        _geometryBatch.End();

        // Pass 4: Crisp Bitmap Typography
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        ColorRgba black = CarbonUiPalette.TextOutline;

        const string title = "CARBON REALMS";
        TextMeasureResult titleMeasure = _font.MeasureString(title, TextLayoutOptions.Default, 2f);
        spriteBatch.DrawOutlinedString(_font, title, new Vector2(MathF.Floor((width - titleMeasure.Width) * 0.5f), py - 66f), CarbonUiPalette.TextWarm, black, 2, 2f);

        const string subtitle = "ONLINE MMORPG  //  TCP + AUTH UDP";
        TextMeasureResult subMeasure = _font.MeasureString(subtitle, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(_font, subtitle, new Vector2(MathF.Floor((width - subMeasure.Width) * 0.5f), py - 28f), CarbonUiPalette.TextMuted, black, 1, 1f);

        _signInTab.DrawText(spriteBatch, _font);
        _registerTab.DrawText(spriteBatch, _font);
        _usernameField.DrawText(spriteBatch, _font);
        _passwordField.DrawText(spriteBatch, _font);
        _toggleMaskBtn.DrawText(spriteBatch, _font);
        if (_registerMode)
        {
            _confirmPasswordField.DrawText(spriteBatch, _font);
        }

        _submitButton.DrawText(spriteBatch, _font);
        if (_showGatewayEditor)
        {
            _serverField.DrawText(spriteBatch, _font);
        }
        else
        {
            string gwText = CarbonUiSkin.TruncateToWidth(_font, $"GATEWAY: {_serverField.Text}", gatewayPill.Width - 36f, 1f);
            spriteBatch.DrawOutlinedString(_font, gwText, new Vector2(gatewayPill.X + 26f, gatewayPill.Y + 9f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        _editGatewayBtn.DrawText(spriteBatch, _font);
        _quitButton.DrawText(spriteBatch, _font);

        ColorRgba statusColor = _statusIsError
            ? new ColorRgba(235, 130, 130, 255)
            : CarbonUiPalette.TextPrimary;
        string msg = CarbonUiSkin.TruncateToWidth(_font, _statusMessage, statusPill.Width - 20f, 1f);
        spriteBatch.DrawOutlinedString(_font, msg, new Vector2(statusPill.X + 10f, statusPill.Y + 7f), statusColor, black, 1, 1f);
        spriteBatch.End();

        base.Render(context, spriteBatch, dt);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.DetachInput();
            _uiSkin?.Dispose();
            _uiSkin = null;
            _geometryBatch?.Dispose();
            _geometryBatch = null;
            _font?.Dispose();
            _font = null;
            _whitePixel?.Dispose();
            _whitePixel = null;
        }

        base.Dispose(disposing);
    }

    private (float Px, float Py, float PanelW, float PanelH) GetPanelRect(int width, int height)
    {
        const float panelW = 540f;
        float panelH = _registerMode ? 496f : 434f;
        if (_showGatewayEditor)
        {
            panelH += 34f;
        }

        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;
        return (px, py, panelW, panelH);
    }

    private void LayoutControls(int width, int height)
    {
        (float px, float py, float panelW, float panelH) = this.GetPanelRect(width, height);
        float fieldX = px + 28f;
        float fieldW = panelW - 56f;

        _signInTab.Bounds = new RectangleF(fieldX, py + 22f, 236f, 38f);
        _registerTab.Bounds = new RectangleF(fieldX + 248f, py + 22f, 236f, 38f);

        _usernameField.Bounds = new RectangleF(fieldX, py + 98f, fieldW, 38f);
        _passwordField.Bounds = new RectangleF(fieldX, py + 172f, fieldW - 76f, 38f);
        _toggleMaskBtn.Bounds = new RectangleF(fieldX + fieldW - 68f, py + 172f, 68f, 38f);

        float nextY = py + 230f;
        if (_registerMode)
        {
            _confirmPasswordField.Bounds = new RectangleF(fieldX, py + 246f, fieldW, 38f);
            nextY = py + 302f;
        }

        _submitButton.Bounds = new RectangleF(fieldX, nextY, fieldW, 44f);

        if (_showGatewayEditor)
        {
            _serverField.Bounds = new RectangleF(fieldX, py + panelH - 96f, 344f, 36f);
            _editGatewayBtn.Bounds = new RectangleF(fieldX + 352f, py + panelH - 96f, 68f, 36f);
            _quitButton.Bounds = new RectangleF(fieldX + 426f, py + panelH - 96f, 58f, 36f);
        }
        else
        {
            _editGatewayBtn.Bounds = new RectangleF(fieldX + 352f, py + panelH - 90f, 68f, 34f);
            _quitButton.Bounds = new RectangleF(fieldX + 426f, py + panelH - 90f, 58f, 34f);
        }
    }

    private void BeginAuth()
    {
        if (!TryParseEndpoint(_serverField.Text, _options.ServerTcpPort, out string host, out ushort port))
        {
            _statusIsError = true;
            _statusMessage = "Invalid server gateway. Use host:port.";
            return;
        }

        string user = _usernameField.Text.Trim();
        string pass = _passwordField.Text;
        if (user.Length < 3 || user.Length > 24)
        {
            _statusIsError = true;
            _statusMessage = "Username must be between 3 and 24 characters.";
            return;
        }

        if (pass.Length < 6)
        {
            _statusIsError = true;
            _statusMessage = "Password must be at least 6 characters.";
            return;
        }

        if (_registerMode && !string.Equals(pass, _confirmPasswordField.Text, StringComparison.Ordinal))
        {
            _statusIsError = true;
            _statusMessage = "Password confirmation does not match.";
            return;
        }

        _pendingAuthTask = _registerMode
            ? _session.RegisterAsync(host, port, user, pass)
            : _session.LoginAsync(host, port, user, pass);
    }

    private void UpdateActiveFields(float dt, Vector2 mousePos, bool allowFocusClick)
    {
        _usernameField.Update(dt, mousePos, allowFocusClick);
        _passwordField.Update(dt, mousePos, allowFocusClick);
        if (_registerMode)
        {
            _confirmPasswordField.Update(dt, mousePos, allowFocusClick);
        }
        else
        {
            _confirmPasswordField.IsFocused = false;
        }

        if (_showGatewayEditor)
        {
            _serverField.Update(dt, mousePos, allowFocusClick);
        }
        else
        {
            _serverField.IsFocused = false;
        }
    }

    private bool UnfocusAll()
    {
        bool any = _usernameField.IsFocused || _passwordField.IsFocused || _confirmPasswordField.IsFocused || _serverField.IsFocused;
        _usernameField.IsFocused = false;
        _passwordField.IsFocused = false;
        _confirmPasswordField.IsFocused = false;
        _serverField.IsFocused = false;
        return any;
    }

    private void CycleFocus(bool reverse)
    {
        MenuTextField[] active = (_registerMode, _showGatewayEditor) switch
        {
            (true, true) => [_usernameField, _passwordField, _confirmPasswordField, _serverField],
            (true, false) => [_usernameField, _passwordField, _confirmPasswordField],
            (false, true) => [_usernameField, _passwordField, _serverField],
            (false, false) => [_usernameField, _passwordField],
        };

        int focusedIdx = -1;
        for (int i = 0; i < active.Length; i++)
        {
            if (active[i].IsFocused)
            {
                focusedIdx = i;
                active[i].IsFocused = false;
                break;
            }
        }

        int next = focusedIdx < 0
            ? 0
            : (reverse ? (focusedIdx + active.Length - 1) % active.Length : (focusedIdx + 1) % active.Length);
        active[next].IsFocused = true;
    }

    private void HandleBackspaceRepeat(in KeyboardSnapshot kb, float dt)
    {
        if (kb.IsKeyPressed(Key.Backspace))
        {
            this.ApplyBackspace();
            _backspaceHoldTime = 0f;
            _backspaceRepeatTime = 0f;
            return;
        }

        if (kb.IsKeyDown(Key.Backspace))
        {
            _backspaceHoldTime += dt;
            if (_backspaceHoldTime >= 0.38f)
            {
                _backspaceRepeatTime += dt;
                if (_backspaceRepeatTime >= 0.055f)
                {
                    _backspaceRepeatTime = 0f;
                    this.ApplyBackspace();
                }
            }
        }
        else
        {
            _backspaceHoldTime = 0f;
            _backspaceRepeatTime = 0f;
        }
    }

    private void ApplyBackspace()
    {
        _usernameField.Backspace();
        _passwordField.Backspace();
        _confirmPasswordField.Backspace();
        _serverField.Backspace();
    }

    private void OnCharacterReceived(char ch)
    {
        if (_pendingAuthTask is not null || _pendingAutoEnterTask is not null)
        {
            return;
        }

        _usernameField.AppendChar(ch);
        _passwordField.AppendChar(ch);
        if (_registerMode)
        {
            _confirmPasswordField.AppendChar(ch);
        }

        if (_showGatewayEditor)
        {
            _serverField.AppendChar(ch);
        }
    }

    private void AttachInput(IInputManager input)
    {
        this.DetachInput();
        _boundInput = input;
        _boundInput.CharacterReceived += this.OnCharacterReceived;
    }

    private void DetachInput()
    {
        if (_boundInput is { } bound)
        {
            bound.CharacterReceived -= this.OnCharacterReceived;
            _boundInput = null;
        }
    }

    private static bool TryParseEndpoint(string raw, ushort defaultPort, out string host, out ushort port)
    {
        host = string.Empty;
        port = defaultPort > 0 ? defaultPort : (ushort)57207;

        string trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        int colonIdx = trimmed.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < trimmed.Length - 1 &&
            ushort.TryParse(trimmed[(colonIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsedPort) &&
            parsedPort > 0)
        {
            host = trimmed[..colonIdx].Trim();
            port = parsedPort;
            return host.Length > 0;
        }

        host = trimmed;
        return host.Length > 0;
    }
}
