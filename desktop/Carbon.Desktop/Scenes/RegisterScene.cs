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
/// Register screen (ADR-037), separate from login: username, password (with a strength meter and a show/hide eye),
/// confirm password (live "match" feedback), Register and Back. Field errors show under their field
/// (docs/art/menu-ui.md); the form mirrors the server's rules so mistakes show early, and the server still decides.
/// </summary>
internal sealed class RegisterScene : MenuScene
{
    private const int MeterSteps = 4;

    private readonly RegisterForm _form = new();
    private readonly MenuTextField _usernameField;
    private readonly MenuTextField _passwordField;
    private readonly MenuTextField _confirmField;
    private readonly MenuButton _submitButton = new(GameText.Get("login.register"), MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _backButton = new(GameText.Get("login.back"), MenuButtonStyle.NeutralSlate);
    private readonly CachedText _strengthText = new();

    private Task<AuthenticationResult>? _pendingRegister;
    private RectangleF _card;

    public RegisterScene(MenuFlow flow)
        : base(nameof(RegisterScene), flow, "register")
    {
        _usernameField = new MenuTextField(GameText.Get("login.field.username"), GameText.Get("login.hint.username"), string.Empty, isPassword: false, maxLength: AccountRules.MaxUsername)
        {
            CharacterFilter = AccountRules.IsUsernameChar,
            Icon = MenuFieldIcon.User,
        };
        string capsMessage = GameText.Get("login.caps_lock_on");
        _passwordField = new MenuTextField(GameText.Get("login.field.password"), GameText.Get("login.hint.password"), string.Empty, isPassword: true, maxLength: AccountRules.MaxPassword)
        {
            Icon = MenuFieldIcon.Lock,
            CapsLockMessage = capsMessage,
        };
        _confirmField = new MenuTextField(GameText.Get("login.field.confirm"), GameText.Get("login.hint.confirm"), string.Empty, isPassword: true, maxLength: AccountRules.MaxPassword)
        {
            Icon = MenuFieldIcon.Lock,
            CapsLockMessage = capsMessage,
        };
        this.SetStatus(GameText.Get("login.status.register_hint"), isError: false);
        if (this.IsFormFeedbackPreview)
        {
            // Screenshot preview: a too-short username, a typed password (strength meter) and a confirm that differs.
            string password = flow.Options.Password;
            _usernameField.Text = flow.Options.Username[..Math.Min(2, flow.Options.Username.Length)];
            _passwordField.Text = password;
            _confirmField.Text = password[..Math.Max(0, password.Length - 1)];
            _form.MarkSubmitAttempted();
            _form.Focus = RegisterFocus.Submit;
            this.RefreshFeedback();
            this.SetStatus(GameText.Get("login.error.username_length"), isError: true);
        }

        this.SyncFocus();
    }

    /// <summary>Gets the focused control (tests).</summary>
    internal RegisterFocus Focus => _form.Focus;

    /// <inheritdoc/>
    protected override string? Header => GameText.Get("login.header.register");

    /// <inheritdoc/>
    protected override RectangleF Layout(int width, int height)
    {
        const float panelW = 500f;
        const float panelH = 520f;
        float px = MathF.Floor((width - panelW) * 0.5f);
        float py = MathF.Floor((height - panelH) * 0.5f) + 40f;
        _card = new RectangleF(px, py, panelW, panelH);

        float fx = px + 32f;
        float fw = panelW - 64f;
        _usernameField.Bounds = new RectangleF(fx, py + 96f, fw, 40f);
        _passwordField.Bounds = new RectangleF(fx, py + 192f, fw, 40f);
        _confirmField.Bounds = new RectangleF(fx, py + 288f, fw, 40f);
        _submitButton.Bounds = new RectangleF(fx, py + 360f, fw, 46f);
        _backButton.Bounds = new RectangleF(fx, py + 420f, 120f, 36f);
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

        if (!busy && kb.IsKeyPressed(Key.Space) && _form.Focus == RegisterFocus.ShowPassword)
        {
            this.ToggleReveal();
        }

        if (!busy && leftPressed)
        {
            if (_passwordField.RevealBounds.Contains(mousePos) || _confirmField.RevealBounds.Contains(mousePos))
            {
                this.ToggleReveal();
                _form.Focus = _confirmField.RevealBounds.Contains(mousePos) ? RegisterFocus.Confirm : RegisterFocus.Password;
            }
            else if (_usernameField.Bounds.Contains(mousePos))
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
        this.RefreshFeedback();

        _submitButton.IsBusy = busy;
        _backButton.IsEnabled = !busy;
        if (_backButton.Update(mousePos, leftDown, leftPressed, dt))
        {
            ChangeTo(context, () => new StartScene(this.Flow));
            return;
        }

        bool clicked = _submitButton.Update(mousePos, leftDown, leftPressed, dt);
        bool enter = kb.IsKeyPressed(Key.Enter) || kb.IsKeyPressed(Key.KeypadEnter);
        bool spaceOnSubmit = kb.IsKeyPressed(Key.Space) && _form.Focus == RegisterFocus.Submit;
        if (busy)
        {
            return;
        }

        if (clicked || spaceOnSubmit || (enter && _form.Enter() == FormEnterResult.Submit))
        {
            this.BeginRegister();
        }

        this.SyncFocus();
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
        if (_form.Focus == RegisterFocus.ShowPassword)
        {
            RectangleF eye = _passwordField.RevealBounds;
            MenuButton.DrawFocusRing(geometry, new RectangleF(eye.X + 3f, eye.Y + 3f, eye.Width - 6f, eye.Height - 6f), this.Elapsed);
        }

        if (this.ShowsMeter())
        {
            DrawStrengthMeter(geometry, _passwordField.Bounds, _form.Strength);
        }

        _submitButton.DrawOverlay(geometry);
        _backButton.DrawOverlay(geometry);
    }

    /// <inheritdoc/>
    protected override void DrawTexts(SpriteBatch spriteBatch, BitmapFont font)
    {
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

    /// <summary>The meter shows while a password is typed and no error or caps-lock warning needs the line.</summary>
    private bool ShowsMeter() =>
        _passwordField.Length > 0 && _passwordField.Feedback != FieldFeedback.Error && !_passwordField.ShowsCapsLockWarning;

    /// <summary>Four segments at the right of the password's feedback line, lit up to the strength and coloured by it.</summary>
    private static void DrawStrengthMeter(GeometryBatch geometry, in RectangleF field, PasswordStrengthLevel level)
    {
        const float segW = 34f;
        const float gap = 4f;
        int lit = (int)level;
        ColorRgba color = level switch
        {
            PasswordStrengthLevel.TooShort or PasswordStrengthLevel.Weak => MenuPalette.ErrorText,
            PasswordStrengthLevel.Fair => MenuPalette.WarningText,
            PasswordStrengthLevel.Good or PasswordStrengthLevel.Strong => MenuPalette.SuccessText,
            _ => MenuPalette.SuccessText,
        };
        float x = field.Right - ((segW + gap) * MeterSteps) + gap;
        float y = field.Bottom + 10f;
        for (int i = 0; i < MeterSteps; i++)
        {
            geometry.FillRectangle(new RectangleF(x + (i * (segW + gap)), y, segW, 5f), i < lit ? color : MenuPalette.MeterTrack);
        }
    }

    private void ToggleReveal()
    {
        _passwordField.ToggleReveal();
        _confirmField.IsRevealed = _passwordField.IsRevealed;
    }

    private void SyncFocus()
    {
        _usernameField.IsFocused = _form.Focus == RegisterFocus.Username;
        _passwordField.IsFocused = _form.Focus == RegisterFocus.Password;
        _confirmField.IsFocused = _form.Focus == RegisterFocus.Confirm;
        _submitButton.IsFocused = _form.Focus == RegisterFocus.Submit;
    }

    /// <summary>Copies the fields into the form and shows each field's error, the strength word or "passwords match".</summary>
    private void RefreshFeedback()
    {
        _form.Username = _usernameField.Text;
        _form.Password = _passwordField.Text;
        _form.Confirm = _confirmField.Text;
        _passwordField.CapsLockOn = this.CapsLock.IsOn;
        _confirmField.CapsLockOn = this.CapsLock.IsOn;

        if (_form.VisibleUsernameError() is { } userError)
        {
            _usernameField.SetFeedback(FieldFeedback.Error, GameText.Get(userError));
        }
        else
        {
            _usernameField.SetFeedback(_form.Username.Trim().Length >= AccountRules.MinUsername && _form.Focus != RegisterFocus.Username ? FieldFeedback.Success : FieldFeedback.None, string.Empty);
        }

        if (_form.VisiblePasswordError() is { } passwordError)
        {
            _passwordField.SetFeedback(FieldFeedback.Error, GameText.Get(passwordError));
        }
        else if (_form.Password.Length > 0)
        {
            string word = GameText.Get(PasswordStrength.LabelKey(_form.Strength));
            _passwordField.SetFeedback(FieldFeedback.Hint, _strengthText.Format("login.strength.label", new TextArg("level", word)));
        }
        else
        {
            _passwordField.SetFeedback(FieldFeedback.None, string.Empty);
        }

        if (_form.VisibleConfirmError() is { } confirmError)
        {
            _confirmField.SetFeedback(FieldFeedback.Error, GameText.Get(confirmError));
        }
        else
        {
            _confirmField.SetFeedback(_form.PasswordsMatch ? FieldFeedback.Success : FieldFeedback.None, GameText.Get("login.passwords_match"));
        }
    }

    private void BeginRegister()
    {
        _form.Username = _usernameField.Text;
        _form.Password = _passwordField.Text;
        _form.Confirm = _confirmField.Text;
        _form.MarkSubmitAttempted();
        this.RefreshFeedback();
        if (_form.Validate() is { } errorKey)
        {
            this.SetStatus(GameText.Get(errorKey), isError: true);
            _form.Focus = _form.VisibleUsernameError() is not null ? RegisterFocus.Username
                : _form.VisiblePasswordError() is not null ? RegisterFocus.Password
                : RegisterFocus.Confirm;
            this.SyncFocus();
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
