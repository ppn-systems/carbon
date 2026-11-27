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
using Carbon.Graphics.Batching.Geometry;
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

    private RectangleF _identity;
    private string? _initialOf;
    private string _initial = string.Empty;

    private readonly MenuButton _quitButton = new(GameText.Get("login.quit"), MenuButtonStyle.DangerRed);
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

    /// <summary>Gets the button with keyboard focus (tests read this).</summary>
    internal StartMenuAction Focus => _model.Focus;

    /// <summary>Gets where <paramref name="action"/>'s button sits, in canvas units (tests click it).</summary>
    internal RectangleF ButtonBounds(StartMenuAction action) =>
        action == StartMenuAction.Quit ? _quitButton.Bounds : _buttons[action].Bounds;

    /// <inheritdoc/>
    protected override string? FooterHint => GameText.Get("login.footer.start");

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
        bool signedIn = _model.SignedInName is not null;
        float headH = signedIn ? 146f : 132f;
        float panelH = headH + (_model.Actions.Count * 58f) + 48f + (this.StatusMessage.Length > 0 ? 56f : 16f);
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = PanelTop(height, panelH);
        _card = new RectangleF(px, py, panelW, panelH);
        _identity = new RectangleF(px + 32f, py + 26f, panelW - 64f, signedIn ? 74f : 60f);

        float bx = px + 56f;
        float bw = panelW - 112f;
        float by = py + headH;
        foreach (MenuButton button in _buttons.Values)
        {
            button.Bounds = default;
        }

        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].Bounds = new RectangleF(bx, by, bw, 46f);
            by += 58f;
        }

        _quitButton.Bounds = new RectangleF(MathF.Floor(px + ((panelW - 128f) * 0.5f)), by + 2f, 128f, 32f);
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
        Vector2 mousePos = this.PointerPosition(in mouse);
        bool busy = this.Busy;

        bool shift = kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight);
        if (kb.IsKeyPressed(Key.Down) || (kb.IsKeyPressed(Key.Tab) && !shift))
        {
            _model.MoveFocus(reverse: false);
        }
        else if (kb.IsKeyPressed(Key.Up) || (kb.IsKeyPressed(Key.Tab) && shift))
        {
            _model.MoveFocus(reverse: true);
        }

        StartMenuAction? clicked = null;
        foreach (StartMenuAction action in _model.Actions)
        {
            MenuButton button = _buttons[action];
            button.IsEnabled = !busy;
            button.IsBusy = busy && action == StartMenuAction.Continue && _pendingSignIn is not null;
            button.IsFocused = _model.Focus == action;
            if (button.Update(mousePos, leftDown, leftPressed, dt))
            {
                clicked = action;
            }
        }

        _quitButton.IsFocused = _model.Focus == StartMenuAction.Quit;
        if (_quitButton.Update(mousePos, leftDown, leftPressed, dt))
        {
            clicked = StartMenuAction.Quit;
        }

        bool activate = kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter) || kb.IsKeyPressed(Key.Space);
        if (clicked is null && activate && (!busy || _model.Focus == StartMenuAction.Quit))
        {
            clicked = _model.Focus;
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
            case StartMenuAction.Quit:
                context.Window.Close();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <inheritdoc/>
    protected override void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        skin.DrawInset(spriteBatch, in _identity, CarbonUiPalette.SlotFill, _model.SignedInName is null ? CarbonUiPalette.BorderSubtle : CarbonUiPalette.BorderActive);
        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].DrawSkin(spriteBatch, skin);
        }

        _quitButton.DrawSkin(spriteBatch, skin);
    }

    /// <inheritdoc/>
    protected override void DrawGeometry(GeometryBatch geometry, BitmapFont font)
    {
        if (_model.SignedInName is not null)
        {
            // The player's badge: a gold ring around their initial.
            Vector2 centre = new(_identity.X + 38f, _identity.Y + (_identity.Height * 0.5f));
            geometry.FillCircle(centre, 24f, new ColorRgba(52, 44, 30, 255), segments: 28);
            geometry.DrawCircle(centre, 24f, CarbonUiPalette.BorderActive, segments: 28, thickness: 2f);
        }
        else
        {
            MenuIcons.DrawDiamond(geometry, new Vector2(_identity.X + 22f, _identity.Y + 20f), 4f, CarbonUiPalette.BorderActive);
        }

        MenuIcons.DrawField(geometry, MenuFieldIcon.Globe, new Vector2(_identity.X + 12f, _identity.Bottom + 19f), MenuPalette.Icon);
        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].DrawOverlay(geometry);
        }

        _quitButton.DrawOverlay(geometry);
    }

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
        ColorRgba black = CarbonUiPalette.TextOutline;
        if (_model.SignedInName is { } name)
        {
            Vector2 centre = new(_identity.X + 38f, _identity.Y + (_identity.Height * 0.5f));
            if (!ReferenceEquals(_initialOf, name))
            {
                _initialOf = name;
                _initial = name.Length > 0 ? name[..1].ToUpperInvariant() : string.Empty;
            }

            string initial = _initial;
            TextMeasureResult im = font.MeasureString(initial, TextLayoutOptions.Default, 2f);
            spriteBatch.DrawOutlinedString(font, initial, new Vector2(MathF.Floor(centre.X - (im.Width * 0.5f)), MathF.Floor(centre.Y - (im.Height * 0.5f))), CarbonUiPalette.TextWarm, black, 1, 2f);

            float tx = _identity.X + 76f;
            float tw = _identity.Right - tx - 12f;
            spriteBatch.DrawOutlinedString(font, GameText.Get("login.signed_in_label"), new Vector2(tx, _identity.Y + 10f), CarbonUiPalette.TextMuted, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, name, tw, 1f), new Vector2(tx, _identity.Y + 29f), CarbonUiPalette.TextWarm, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, GameText.Get("login.remembered_note"), tw, 1f), new Vector2(tx, _identity.Y + 49f), MenuPalette.SuccessText, black, 1, 1f);
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, GameText.Get("login.welcome"), new Vector2(_identity.X + 36f, _identity.Y + 12f), CarbonUiPalette.TextWarm, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, GameText.Get("login.tagline"), _identity.Width - 48f, 1f), new Vector2(_identity.X + 36f, _identity.Y + 34f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        string server = _serverLine.Format("login.server_line", new TextArg("name", ServerSelectScene.DisplayName(this.Flow.SelectedServer)));
        string fitted = CarbonUiSkin.TruncateToWidth(font, server, _identity.Width - 30f, 1f);
        spriteBatch.DrawOutlinedString(font, fitted, new Vector2(_identity.X + 26f, _identity.Bottom + 11f), CarbonUiPalette.TextMuted, black, 1, 1f);

        foreach (StartMenuAction action in _model.Actions)
        {
            _buttons[action].DrawText(spriteBatch, font);
        }

        _quitButton.DrawText(spriteBatch, font);
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
