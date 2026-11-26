// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering;
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
/// Online authentication scene handling server endpoint selection, TCP X25519 encrypted login/registration,
/// and automated fast-path login when <see cref="DesktopClientOptions.AutoLogin"/> is enabled.
/// </summary>
public sealed class LoginScene : GameScene
{
    private readonly IClientWorldSession _session;
    private readonly DesktopClientOptions _options;
    private readonly Camera2D _camera;

    private readonly MenuTextField _serverField;
    private readonly MenuTextField _usernameField;
    private readonly MenuTextField _passwordField;
    private readonly MenuTextField[] _fields;

    private readonly MenuButton _signInButton = new("SIGN IN", MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _registerButton = new("REGISTER", MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _quitButton = new("QUIT", MenuButtonStyle.DangerRed);

    private GeometryBatch? _geometryBatch;
    private BitmapFont? _font;
    private Texture2D? _whitePixel;
    private IInputManager? _boundInput;

    private Task<ClientAuthResult>? _pendingAuthTask;
    private Task? _pendingAutoEnterTask;
    private bool _pendingIsRegister;
    private bool _autoStarted;
    private float _elapsed;
    private float _backspaceHoldTime;
    private float _backspaceRepeatTime;
    private string _statusMessage = "Enter credentials to connect via TCP + Authenticated UDP.";
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

        _serverField = new MenuTextField("SERVER (HOST:PORT)", "127.0.0.1:57207", $"{options.ServerHost}:{options.ServerTcpPort}", isPassword: false, maxLength: 96);
        _usernameField = new MenuTextField("USERNAME", "3-24 chars", options.Username, isPassword: false, maxLength: 24)
        {
            IsFocused = true,
        };
        _passwordField = new MenuTextField("PASSWORD", "min 6 chars", options.Password, isPassword: true, maxLength: 64);
        _fields = [_serverField, _usernameField, _passwordField];
    }

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);

        _geometryBatch = new GeometryBatch(context.Device);
        (_font, _whitePixel) = EmbeddedPixelFont.Create(context.Device);
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
            context.Window.Close();
            return;
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

        for (int i = 0; i < _fields.Length; i++)
        {
            _fields[i].Update((float)dt, mousePos, leftPressed && !busy);
        }

        _signInButton.IsEnabled = !busy;
        _registerButton.IsEnabled = !busy;
        _quitButton.IsEnabled = true;

        if (_quitButton.Update(mousePos, leftDown, leftPressed))
        {
            context.Window.Close();
            return;
        }

        bool enterSubmit = !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter));
        if (_signInButton.Update(mousePos, leftDown, leftPressed) || enterSubmit)
        {
            this.BeginAuth(isRegister: false);
        }
        else if (_registerButton.Update(mousePos, leftDown, leftPressed))
        {
            this.BeginAuth(isRegister: true);
        }

        if (busy)
        {
            _statusIsError = false;
            _statusMessage = _pendingAutoEnterTask is not null
                ? $"Connecting & entering world ({_options.ServerHost}:{_options.ServerTcpPort})..."
                : (_pendingIsRegister ? "Creating account via TCP X25519..." : "Signing in via TCP X25519...");
        }
    }

    /// <inheritdoc/>
    public override void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_geometryBatch is null || _font is null)
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

        float panelW = 500f;
        float panelH = 420f;
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;

        _geometryBatch.Begin(proj);
        MenuBackdropRenderer.Draw(_geometryBatch, width, height, _elapsed);

        RectangleF card = new(px, py, panelW, panelH);
        _geometryBatch.FillRectangle(card, new ColorRgba(10, 15, 28, 232));
        _geometryBatch.DrawRectangle(card, new ColorRgba(250, 204, 21, 210), 2f);
        _geometryBatch.FillRectangle(new RectangleF(px, py, panelW, 6f), new ColorRgba(250, 204, 21, 235));

        for (int i = 0; i < _fields.Length; i++)
        {
            _fields[i].DrawGeometry(_geometryBatch, _font);
        }

        _signInButton.DrawGeometry(_geometryBatch);
        _registerButton.DrawGeometry(_geometryBatch);
        _quitButton.DrawGeometry(_geometryBatch);
        _geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        ColorRgba black = new(8, 10, 16, 255);

        const string title = "CARBON ONLINE";
        TextMeasureResult titleMeasure = _font.MeasureString(title, TextLayoutOptions.Default, 4f);
        spriteBatch.DrawOutlinedString(
            _font,
            title,
            new Vector2(MathF.Floor((width - titleMeasure.Width) * 0.5f), py - 68f),
            new ColorRgba(250, 204, 21, 255),
            black,
            2,
            4f);

        const string subtitle = "TCP + NALIX AUTHENTICATED UDP";
        TextMeasureResult subMeasure = _font.MeasureString(subtitle, TextLayoutOptions.Default, 2f);
        spriteBatch.DrawOutlinedString(
            _font,
            subtitle,
            new Vector2(MathF.Floor((width - subMeasure.Width) * 0.5f), py - 30f),
            new ColorRgba(125, 211, 252, 255),
            black,
            1,
            2f);

        for (int i = 0; i < _fields.Length; i++)
        {
            _fields[i].DrawText(spriteBatch, _font);
        }

        _signInButton.DrawText(spriteBatch, _font);
        _registerButton.DrawText(spriteBatch, _font);
        _quitButton.DrawText(spriteBatch, _font);

        ColorRgba statusColor = _statusIsError
            ? new ColorRgba(248, 113, 113, 255)
            : new ColorRgba(110, 231, 183, 255);
        string msg = _statusMessage.Length > 52 ? _statusMessage[..52] : _statusMessage;
        spriteBatch.DrawOutlinedString(_font, msg, new Vector2(px + 28f, py + panelH - 36f), statusColor, black, 1, 2f);

        spriteBatch.End();
        base.Render(context, spriteBatch, dt);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.DetachInput();
            _geometryBatch?.Dispose();
            _geometryBatch = null;
            _font?.Dispose();
            _font = null;
            _whitePixel?.Dispose();
            _whitePixel = null;
        }

        base.Dispose(disposing);
    }

    private void LayoutControls(int width, int height)
    {
        const float panelW = 500f;
        const float panelH = 420f;
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;
        float fieldX = px + 28f;
        float fieldW = panelW - 56f;

        _serverField.Bounds = new RectangleF(fieldX, py + 48f, fieldW, 38f);
        _usernameField.Bounds = new RectangleF(fieldX, py + 122f, fieldW, 38f);
        _passwordField.Bounds = new RectangleF(fieldX, py + 196f, fieldW, 38f);

        float btnY = py + 264f;
        _signInButton.Bounds = new RectangleF(fieldX, btnY, 212f, 44f);
        _registerButton.Bounds = new RectangleF(fieldX + 232f, btnY, 212f, 44f);
        _quitButton.Bounds = new RectangleF(fieldX + 152f, btnY + 58f, 140f, 36f);
    }

    private void BeginAuth(bool isRegister)
    {
        if (!TryParseEndpoint(_serverField.Text, _options.ServerTcpPort, out string host, out ushort port))
        {
            _statusIsError = true;
            _statusMessage = "Invalid server format. Use host or host:port.";
            return;
        }

        _pendingIsRegister = isRegister;
        string user = _usernameField.Text;
        string pass = _passwordField.Text;
        _pendingAuthTask = isRegister
            ? _session.RegisterAsync(host, port, user, pass)
            : _session.LoginAsync(host, port, user, pass);
    }

    private void CycleFocus(bool reverse)
    {
        int focusedIdx = 0;
        for (int i = 0; i < _fields.Length; i++)
        {
            if (_fields[i].IsFocused)
            {
                focusedIdx = i;
                _fields[i].IsFocused = false;
                break;
            }
        }

        int next = reverse
            ? (focusedIdx + _fields.Length - 1) % _fields.Length
            : (focusedIdx + 1) % _fields.Length;
        _fields[next].IsFocused = true;
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
        for (int i = 0; i < _fields.Length; i++)
        {
            _fields[i].Backspace();
        }
    }

    private void OnCharacterReceived(char ch)
    {
        if (_pendingAuthTask is not null || _pendingAutoEnterTask is not null)
        {
            return;
        }

        for (int i = 0; i < _fields.Length; i++)
        {
            _fields[i].AppendChar(ch);
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
