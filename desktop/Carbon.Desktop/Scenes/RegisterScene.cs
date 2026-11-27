// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
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
/// Register screen (ADR-037), separate from login: username, password, confirm password, Register and Back. The form
/// checks the server's rules first so mistakes show at once; the server still decides.
/// </summary>
internal sealed class RegisterScene : MenuScene
{
    private readonly RegisterForm _form = new();
    private readonly MenuTextField _usernameField;
    private readonly MenuTextField _passwordField;
    private readonly MenuTextField _confirmField;
    private readonly MenuButton _submitButton = new(GameText.Get("login.register"), MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _backButton = new(GameText.Get("login.back"), MenuButtonStyle.NeutralSlate);

    private Task<AuthenticationResult>? _pendingRegister;
    private RectangleF _card;

    public RegisterScene(MenuFlow flow)
        : base(nameof(RegisterScene), flow, "register")
    {
        _usernameField = new MenuTextField(GameText.Get("login.field.username"), GameText.Get("login.hint.username"), string.Empty, isPassword: false, maxLength: AccountRules.MaxUsername)
        {
            CharacterFilter = AccountRules.IsUsernameChar,
        };
        _passwordField = new MenuTextField(GameText.Get("login.field.password"), GameText.Get("login.hint.password"), string.Empty, isPassword: true, maxLength: AccountRules.MaxPassword);
        _confirmField = new MenuTextField(GameText.Get("login.field.confirm"), GameText.Get("login.hint.confirm"), string.Empty, isPassword: true, maxLength: AccountRules.MaxPassword);
        this.SetStatus(GameText.Get("login.status.register_hint"), isError: false);
        this.SyncFocus();
    }

    /// <inheritdoc/>
    protected override RectangleF Layout(int width, int height)
    {
        const float panelW = 500f;
        const float panelH = 450f;
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 22f;
        _card = new RectangleF(px, py, panelW, panelH);

        float fx = px + 28f;
        float fw = panelW - 56f;
        _usernameField.Bounds = new RectangleF(fx, py + 84f, fw, 38f);
        _passwordField.Bounds = new RectangleF(fx, py + 158f, fw, 38f);
        _confirmField.Bounds = new RectangleF(fx, py + 232f, fw, 38f);
        _submitButton.Bounds = new RectangleF(fx, py + 286f, fw, 44f);
        _backButton.Bounds = new RectangleF(fx, py + 342f, 120f, 36f);
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
        bool busy = _pendingRegister is not null;
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

        if (!busy && leftPressed)
        {
            if (_usernameField.Bounds.Contains(mousePos))
            {
                _form.Focus = RegisterFocus.Username;
            }
            else if (_passwordField.Bounds.Contains(mousePos))
            {
                _form.Focus = RegisterFocus.Password;
            }
            else if (_confirmField.Bounds.Contains(mousePos))
            {
                _form.Focus = RegisterFocus.Confirm;
            }
        }

        this.SyncFocus();
        _usernameField.Update(dt, mousePos, leftPressed: false);
        _passwordField.Update(dt, mousePos, leftPressed: false);
        _confirmField.Update(dt, mousePos, leftPressed: false);

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
            this.BeginRegister();
        }
    }

    /// <inheritdoc/>
    protected override void DrawSkins(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        _usernameField.DrawSkin(spriteBatch, skin);
        _passwordField.DrawSkin(spriteBatch, skin);
        _confirmField.DrawSkin(spriteBatch, skin);
        _submitButton.DrawSkin(spriteBatch, skin);
        _backButton.DrawSkin(spriteBatch, skin);
    }

    /// <inheritdoc/>
    protected override void DrawGeometry(GeometryBatch geometry, BitmapFont font)
    {
        _usernameField.DrawCursorGeometry(geometry, font);
        _passwordField.DrawCursorGeometry(geometry, font);
        _confirmField.DrawCursorGeometry(geometry, font);
    }

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
        spriteBatch.DrawOutlinedString(font, GameText.Get("login.header.register"), new Vector2(_card.X + 28f, _card.Y + 24f), CarbonUiPalette.TextWarm, CarbonUiPalette.TextOutline, 1, 1f);
        _usernameField.DrawText(spriteBatch, font);
        _passwordField.DrawText(spriteBatch, font);
        _confirmField.DrawText(spriteBatch, font);
        _submitButton.DrawText(spriteBatch, font);
        _backButton.DrawText(spriteBatch, font);
    }

    /// <inheritdoc/>
    protected override void OnCharacter(char ch)
    {
        if (_pendingRegister is not null)
        {
            return;
        }

        _usernameField.AppendChar(ch);
        _passwordField.AppendChar(ch);
        _confirmField.AppendChar(ch);
    }

    /// <inheritdoc/>
    protected override void OnBackspace()
    {
        _usernameField.Backspace();
        _passwordField.Backspace();
        _confirmField.Backspace();
    }

    private void SyncFocus()
    {
        _usernameField.IsFocused = _form.Focus == RegisterFocus.Username;
        _passwordField.IsFocused = _form.Focus == RegisterFocus.Password;
        _confirmField.IsFocused = _form.Focus == RegisterFocus.Confirm;
        _submitButton.IsFocused = _form.Focus == RegisterFocus.Submit;
    }

    private void BeginRegister()
    {
        _form.Username = _usernameField.Text;
        _form.Password = _passwordField.Text;
        _form.Confirm = _confirmField.Text;
        if (_form.Validate() is { } errorKey)
        {
            this.SetStatus(GameText.Get(errorKey), isError: true);
            return;
        }

        GameServer server = this.Flow.SelectedServer;
        this.SetStatus(GameText.Get("login.status.registering"), isError: false);
        _pendingRegister = this.Flow.Session.RegisterAsync(server.Host, server.Port, _form.Username.Trim(), _form.Password);
    }

    private bool CompletePending(SceneContext context)
    {
        if (_pendingRegister is not { IsCompleted: true } register)
        {
            return false;
        }

        _pendingRegister = null;
        try
        {
            AuthenticationResult result = register.GetAwaiter().GetResult();
            if (result.Succeeded)
            {
                ChangeTo(context, () => new CharacterSelectScene(this.Flow));
                return true;
            }

            this.SetStatus(result.Message, isError: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"[Auth] Registration failed: {ex.Message}");
            this.SetStatus(GameText.Get("login.status.auth_error"), isError: true);
        }

        return false;
    }
}
