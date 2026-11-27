// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Login screen (ADR-037): username, then password (masked), a "Remember me" checkbox, Login and Back. Tab moves
/// username → password → Remember me → Login; Enter submits. "Remember me" keeps a server-issued token, never the password.
/// </summary>
internal sealed class LoginScene : MenuScene
{
    private readonly LoginForm _form = new();
    private readonly MenuTextField _usernameField;
    private readonly MenuTextField _passwordField;
    private readonly MenuCheckbox _rememberBox = new(GameText.Get("login.remember_me"));
    private readonly MenuButton _submitButton = new(GameText.Get("login.login"), MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _backButton = new(GameText.Get("login.back"), MenuButtonStyle.NeutralSlate);

    private Task<AuthenticationResult>? _pendingAuth;
    private Task<bool>? _pendingRemember;
    private string _signedInUser = string.Empty;
    private GameServer _signedInServer;
    private RectangleF _card;

    public LoginScene(MenuFlow flow)
        : base("Login", flow, "login")
    {
        _usernameField = new MenuTextField(GameText.Get("login.field.username"), GameText.Get("login.hint.username"), string.Empty, isPassword: false, maxLength: AccountRules.MaxUsername)
        {
            CharacterFilter = AccountRules.IsUsernameChar,
        };
        _passwordField = new MenuTextField(GameText.Get("login.field.password"), GameText.Get("login.hint.password"), string.Empty, isPassword: true, maxLength: AccountRules.MaxPassword);
        _signedInServer = flow.SelectedServer;
        this.SetStatus(GameText.Get("login.status.sign_in_hint"), isError: false);
        this.SyncFocus();
    }

    private bool Busy => _pendingAuth is not null || _pendingRemember is not null;

    /// <inheritdoc/>
    protected override RectangleF Layout(int width, int height)
    {
        const float panelW = 500f;
        const float panelH = 420f;
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;
        _card = new RectangleF(px, py, panelW, panelH);

        float fx = px + 28f;
        float fw = panelW - 56f;
        _usernameField.Bounds = new RectangleF(fx, py + 84f, fw, 38f);
        _passwordField.Bounds = new RectangleF(fx, py + 158f, fw, 38f);
        _rememberBox.Bounds = new RectangleF(fx, py + 208f, fw, 30f);
        _submitButton.Bounds = new RectangleF(fx, py + 252f, fw, 44f);
        _backButton.Bounds = new RectangleF(fx, py + 308f, 120f, 36f);
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
        bool busy = this.Busy;
        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = mouse.IsButtonPressed(MouseButton.Left);
        Vector2 mousePos = mouse.Position;

        if (kb.IsKeyPressed(Key.Escape) && !busy)
        {
            ChangeTo(context, () => new StartScene(this.Flow));
            return;
        }

        if (kb.IsKeyPressed(Key.Tab))
        {
            _form.Tab(reverse: kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight));
        }

        if (!busy && kb.IsKeyPressed(Key.Space) && _form.Focus == LoginFocus.RememberMe)
        {
            _rememberBox.IsChecked = !_rememberBox.IsChecked;
        }

        if (!busy && leftPressed)
        {
            if (_usernameField.Bounds.Contains(mousePos))
            {
                _form.Focus = LoginFocus.Username;
            }
            else if (_passwordField.Bounds.Contains(mousePos))
            {
                _form.Focus = LoginFocus.Password;
            }
            else if (_rememberBox.Update(mousePos, leftPressed))
            {
                _form.Focus = LoginFocus.RememberMe;
            }
        }

        this.SyncFocus();
        _usernameField.Update(dt, mousePos, leftPressed: false);
        _passwordField.Update(dt, mousePos, leftPressed: false);

        _submitButton.IsEnabled = !busy;
        _backButton.IsEnabled = !busy;
        if (_backButton.Update(mousePos, leftDown, leftPressed))
        {
            ChangeTo(context, () => new StartScene(this.Flow));
            return;
        }

        bool enter = kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter);
        if ((_submitButton.Update(mousePos, leftDown, leftPressed) || enter) && !busy)
        {
            this.BeginLogin();
        }
    }

    /// <inheritdoc/>
    protected override void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        _usernameField.DrawSkin(spriteBatch, skin);
        _passwordField.DrawSkin(spriteBatch, skin);
        _rememberBox.DrawSkin(spriteBatch, skin);
        _submitButton.DrawSkin(spriteBatch, skin);
        _backButton.DrawSkin(spriteBatch, skin);
    }

    /// <inheritdoc/>
    protected override void DrawGeometry(GeometryBatch geometry, BitmapFont font)
    {
        _usernameField.DrawCursorGeometry(geometry, font);
        _passwordField.DrawCursorGeometry(geometry, font);
        _rememberBox.DrawGeometry(geometry);
    }

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
        spriteBatch.DrawOutlinedString(font, GameText.Get("login.header.login"), new Vector2(_card.X + 28f, _card.Y + 24f), CarbonUiPalette.TextWarm, CarbonUiPalette.TextOutline, 1, 1f);
        _usernameField.DrawText(spriteBatch, font);
        _passwordField.DrawText(spriteBatch, font);
        _rememberBox.DrawText(spriteBatch, font);
        _submitButton.DrawText(spriteBatch, font);
        _backButton.DrawText(spriteBatch, font);
    }

    /// <inheritdoc/>
    protected override void OnCharacter(char ch)
    {
        if (this.Busy)
        {
            return;
        }

        _usernameField.AppendChar(ch);
        _passwordField.AppendChar(ch);
    }

    /// <inheritdoc/>
    protected override void OnBackspace()
    {
        _usernameField.Backspace();
        _passwordField.Backspace();
    }

    private void SyncFocus()
    {
        _usernameField.IsFocused = _form.Focus == LoginFocus.Username;
        _passwordField.IsFocused = _form.Focus == LoginFocus.Password;
        _rememberBox.IsFocused = _form.Focus == LoginFocus.RememberMe;
        _submitButton.IsFocused = _form.Focus == LoginFocus.Submit;
    }

    private void BeginLogin()
    {
        _form.Username = _usernameField.Text;
        _form.Password = _passwordField.Text;
        _form.RememberMe = _rememberBox.IsChecked;
        if (_form.Validate() is { } errorKey)
        {
            this.SetStatus(GameText.Get(errorKey), isError: true);
            return;
        }

        GameServer server = this.Flow.SelectedServer;
        _signedInUser = _form.Username.Trim();
        _signedInServer = server;
        this.SetStatus(GameText.Get("login.status.signing_in"), isError: false);
        _pendingAuth = this.SignInAsync(server, _signedInUser, _form.Password, _form.RememberMe);
    }

    /// <summary>
    /// Signs in. Without "Remember me", an account remembered earlier is forgotten first (its token revoked), one request
    /// at a time on the session.
    /// </summary>
    private async Task<AuthenticationResult> SignInAsync(GameServer server, string username, string password, bool remember)
    {
        if (!remember)
        {
            await this.Flow.ForgetAsync(this.Flow.Accounts.Load()).ConfigureAwait(false);
        }

        return await this.Flow.Session.LoginAsync(server.Host, server.Port, username, password).ConfigureAwait(false);
    }

    /// <summary>Finishes sign-in and the remember-me step; returns whether the scene changed.</summary>
    private bool CompletePending(SceneContext context)
    {
        if (_pendingAuth is { IsCompleted: true } auth)
        {
            _pendingAuth = null;
            AuthenticationResult result;
            try
            {
                result = auth.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"[Auth] Sign-in failed: {ex.Message}");
                this.SetStatus(GameText.Get("login.status.auth_error"), isError: true);
                return false;
            }

            if (!result.Succeeded)
            {
                this.SetStatus(result.Message, isError: true);
                return false;
            }

            // The password was needed only for this sign-in; clear the field.
            _passwordField.Text = string.Empty;
            if (!_form.RememberMe)
            {
                ChangeTo(context, () => new CharacterSelectScene(this.Flow));
                return true;
            }

            _pendingRemember = this.Flow.RememberAsync(_signedInUser, _signedInServer);
        }

        if (_pendingRemember is { IsCompleted: true } remember)
        {
            _pendingRemember = null;
            try
            {
                if (!remember.GetAwaiter().GetResult())
                {
                    Console.Error.WriteLine("[Auth] The server did not issue a remember-me token.");
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Signed in all the same; the player is asked for the password next time.
                Console.Error.WriteLine($"[Auth] Could not remember the sign-in: {ex.Message}");
            }

            ChangeTo(context, () => new CharacterSelectScene(this.Flow));
            return true;
        }

        return false;
    }
}
