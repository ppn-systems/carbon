// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Input;
using Nalix.Abstractions.Primitives;
using Silk.NET.Input;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// First screen of the client (ADR-037). With a remembered account it greets the player by name and offers Continue,
/// Log out and Server; without one it offers Login, Register and Server. Esc or Quit closes the game.
/// </summary>
internal sealed class StartScene : MenuScene
{
    /// <summary><c>CARBON_UI_PREVIEW</c> value that shows the signed-in state with a sample account (screenshots only).</summary>
    public const string SignedInPreview = "signed-in";

    /// <summary>A non-zero stand-in token for the signed-in preview; no server ever issued it.</summary>
    private static readonly Bytes32 s_previewToken = Bytes32.Parse(new string('1', Bytes32.Size * 2));

    private readonly StartMenuModel _model;
    private readonly Dictionary<StartMenuAction, MenuButton> _buttons = new()
    {
        [StartMenuAction.Continue] = new(GameText.Get("login.continue"), MenuButtonStyle.SecondaryEmerald),
        [StartMenuAction.LogOut] = new(GameText.Get("login.logout"), MenuButtonStyle.NeutralSlate),
        [StartMenuAction.Login] = new(GameText.Get("login.login"), MenuButtonStyle.SecondaryEmerald),
        [StartMenuAction.Register] = new(GameText.Get("login.register"), MenuButtonStyle.PrimaryGold),
        [StartMenuAction.Server] = new(GameText.Get("login.server"), MenuButtonStyle.NeutralSlate),
    };

    private readonly MenuButton _quitButton = new(GameText.Get("login.quit"), MenuButtonStyle.DangerRed);
    private readonly CachedText _greeting = new();
    private readonly CachedText _serverLine = new();

    private Task<AuthenticationResult>? _pendingSignIn;
    private Task? _pendingAutoEnter;
    private Task? _pendingRevoke;
    private bool _autoStarted;
    private RectangleF _card;

    public StartScene(MenuFlow flow)
        : base(nameof(StartScene), flow, "start")
    {
        IRememberedAccountStore accounts = string.Equals(flow.Options.UiPreview, SignedInPreview, StringComparison.Ordinal)
            ? new PreviewAccountStore(new RememberedAccount(flow.Options.Username, flow.SelectedServer.Host, flow.SelectedServer.Port, s_previewToken, DateTimeOffset.MaxValue))
            : flow.Accounts;
        _model = new StartMenuModel(accounts);
    }

    /// <summary>Gets the buttons on screen, in order (tests read this).</summary>
    public IReadOnlyList<StartMenuAction> VisibleActions => _model.Actions;

    private bool Busy => _pendingSignIn is not null || _pendingAutoEnter is not null;

    /// <inheritdoc/>
    public override void Enter(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Enter(context);
        if (_autoStarted)
        {
            return;
        }

        _autoStarted = true;
        GameServer server = this.Flow.SelectedServer;
        switch (this.Flow.Options.CaptureScene)
        {
            case "login":
                ChangeTo(context, () => new LoginScene(this.Flow));
                return;
            case "register":
                ChangeTo(context, () => new RegisterScene(this.Flow));
                return;
            case "server":
                ChangeTo(context, () => new ServerSelectScene(this.Flow));
                return;
            case "characters":
                // Screenshot review: register the configured account and stop at character select.
                _pendingSignIn = this.Flow.Session.RegisterAsync(server.Host, server.Port, this.Flow.Options.Username, this.Flow.Options.Password);
                return;
            default:
                break;
        }

        if (this.Flow.Options.AutoLogin)
        {
            this.SetStatus(GameText.Get("login.status.auto_sign_in"), isError: false);
            _pendingAutoEnter = this.Flow.Session.InitializeAsync();
        }
    }

    /// <inheritdoc/>
    protected override RectangleF Layout(int width, int height)
    {
        const float panelW = 460f;
        const float panelH = 380f;
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;
        _card = new RectangleF(px, py, panelW, panelH);

        float bx = px + 60f;
        float bw = panelW - 120f;
        float by = py + 104f;
        foreach (MenuButton button in _buttons.Values)
        {
            button.Bounds = default;
        }

        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].Bounds = new RectangleF(bx, by, bw, 46f);
            by += 58f;
        }

        _quitButton.Bounds = new RectangleF(MathF.Floor(px + ((panelW - 120f) * 0.5f)), by + 4f, 120f, 30f);
        return _card;
    }

    /// <inheritdoc/>
    protected override void UpdateMenu(SceneContext context, float dt)
    {
        if (this.CompletePending(context))
        {
            return;
        }

        KeyboardSnapshot kb = context.Input.Keyboard;
        MouseSnapshot mouse = context.Input.Mouse;
        if (kb.IsKeyPressed(Key.Escape))
        {
            context.Window.Close();
            return;
        }

        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position;
        bool busy = this.Busy;

        if (_quitButton.Update(mousePos, leftDown, leftPressed))
        {
            context.Window.Close();
            return;
        }

        StartMenuAction? clicked = null;
        foreach (StartMenuAction action in _model.Actions)
        {
            MenuButton button = _buttons[action];
            button.IsEnabled = !busy;
            if (button.Update(mousePos, leftDown, leftPressed))
            {
                clicked = action;
            }
        }

        if (clicked is null && !busy && (kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter)))
        {
            clicked = _model.Actions[0];
        }

        if (clicked is { } chosen)
        {
            this.Run(context, chosen);
        }
    }

    private void Run(SceneContext context, StartMenuAction action)
    {
        switch (action)
        {
            case StartMenuAction.Continue:
                if (_model.Remembered is { } account)
                {
                    this.SetStatus(GameText.Format("login.status.continuing", new TextArg("name", account.Username)), isError: false);
                    _pendingSignIn = this.Flow.Session.LoginWithTokenAsync(account.ServerHost, account.ServerPort, account.Token);
                }

                break;
            case StartMenuAction.LogOut:
                _pendingRevoke = this.Flow.ForgetAsync(_model.LogOut());
                this.SetStatus(GameText.Get("login.status.logged_out"), isError: false);
                break;
            case StartMenuAction.Login:
                ChangeTo(context, () => new LoginScene(this.Flow));
                break;
            case StartMenuAction.Register:
                ChangeTo(context, () => new RegisterScene(this.Flow));
                break;
            case StartMenuAction.Server:
                ChangeTo(context, () => new ServerSelectScene(this.Flow));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <inheritdoc/>
    protected override void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].DrawSkin(spriteBatch, skin);
        }

        _quitButton.DrawSkin(spriteBatch, skin);
    }

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
        float cx = _card.X + (_card.Width * 0.5f);
        if (_model.SignedInName is { } name)
        {
            string greeting = _greeting.Format("login.signed_in_as", new TextArg("name", name));
            DrawCentered(spriteBatch, font, greeting, cx, _card.Y + 34f, CarbonUiPalette.TextWarm);
        }
        else
        {
            DrawCentered(spriteBatch, font, GameText.Get("login.welcome"), cx, _card.Y + 34f, CarbonUiPalette.TextWarm);
        }

        string server = _serverLine.Format("login.server_line", new TextArg("name", ServerSelectScene.DisplayName(this.Flow.SelectedServer)));
        DrawCentered(spriteBatch, font, CarbonUiSkin.TruncateToWidth(font, server, _card.Width - 56f, 1f), cx, _card.Y + 64f, CarbonUiPalette.TextMuted);

        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].DrawText(spriteBatch, font);
        }

        _quitButton.DrawText(spriteBatch, font);
    }

    private static void DrawCentered(SpriteBatch spriteBatch, BitmapFont font, string text, float centerX, float y, ColorRgba color)
    {
        TextMeasureResult m = font.MeasureString(text, TextLayoutOptions.Default, 1f);
        spriteBatch.DrawOutlinedString(font, text, new Vector2(MathF.Floor(centerX - (m.Width * 0.5f)), y), color, CarbonUiPalette.TextOutline, 1, 1f);
    }

    /// <summary>Finishes sign-in or auto-enter work; returns whether the scene changed.</summary>
    private bool CompletePending(SceneContext context)
    {
        if (_pendingRevoke is { IsCompleted: true })
        {
            _pendingRevoke = null;
        }

        if (_pendingAutoEnter is { IsCompleted: true } autoEnter)
        {
            _pendingAutoEnter = null;
            try
            {
                autoEnter.GetAwaiter().GetResult();
                this.EnteringWorld = true;
                ChangeTo(context, () => new WorldGameplayScene(this.Flow));
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"[Auth] Automatic sign-in failed: {ex.Message}");
                this.SetStatus(GameText.Get("auth.server_unreachable"), isError: true);
            }
        }

        if (_pendingSignIn is { IsCompleted: true } signIn)
        {
            _pendingSignIn = null;
            try
            {
                AuthenticationResult result = signIn.GetAwaiter().GetResult();
                if (result.Succeeded)
                {
                    ChangeTo(context, () => new CharacterSelectScene(this.Flow));
                    return true;
                }

                if (_model.Remembered is not null && result.CredentialsRejected)
                {
                    // The server no longer accepts the token (expired or revoked elsewhere): forget it locally.
                    _ = _model.LogOut();
                    this.SetStatus(GameText.Get("login.status.remembered_expired"), isError: true);
                }
                else
                {
                    this.SetStatus(result.Message, isError: true);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"[Auth] Sign-in failed: {ex.Message}");
                this.SetStatus(GameText.Get("login.status.auth_error"), isError: true);
            }
        }

        return false;
    }

    /// <summary>A read-only remembered account for the signed-in screenshot; nothing touches disk.</summary>
    private sealed class PreviewAccountStore(RememberedAccount account) : IRememberedAccountStore
    {
        private RememberedAccount? _account = account;

        public RememberedAccount? Load() => _account;

        public void Save(RememberedAccount account) => _account = account;

        public void Clear() => _account = null;
    }
}
