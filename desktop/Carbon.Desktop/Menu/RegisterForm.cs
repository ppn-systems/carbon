// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Menu;

/// <summary>The register screen's controls in tab order.</summary>
internal enum RegisterFocus
{
    Username,
    Password,
    ShowPassword,
    Confirm,
    Submit,
}

/// <summary>
/// State and rules of the register screen (ADR-037): username, password, show-password, confirm password, Register.
/// Field errors follow "reward early, punish late" (docs/art/menu-ui.md); the confirm field compares live once it has
/// text, and the password gets a strength meter while typing.
/// </summary>
internal sealed class RegisterForm
{
    /// <summary>Tab order: username → password → show passwords → confirm → Register.</summary>
    public static readonly RegisterFocus[] TabOrder = [RegisterFocus.Username, RegisterFocus.Password, RegisterFocus.ShowPassword, RegisterFocus.Confirm, RegisterFocus.Submit];

    private RegisterFocus _focus = RegisterFocus.Username;
    private bool _usernameTouched;
    private bool _passwordTouched;
    private bool _confirmTouched;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Confirm { get; set; } = string.Empty;

    /// <summary>Gets whether the player has tried to send the form.</summary>
    public bool SubmitAttempted { get; private set; }

    /// <summary>Gets or sets the focused control; leaving a field marks it checked.</summary>
    public RegisterFocus Focus
    {
        get => _focus;
        set
        {
            if (value != _focus)
            {
                this.Blur(_focus);
                _focus = value;
            }
        }
    }

    /// <summary>Gets the strength of the password as typed.</summary>
    public PasswordStrengthLevel Strength => PasswordStrength.Evaluate(this.Password, this.Username);

    /// <summary>Moves focus to the next (or, with <paramref name="reverse"/>, previous) control, wrapping around.</summary>
    public void Tab(bool reverse) => this.Focus = FocusRing.Next(TabOrder, this.Focus, reverse);

    /// <summary>Enter: moves to the next empty field (username → password → confirm); once all have text, sends.</summary>
    public FormEnterResult Enter()
    {
        if (this.Focus == RegisterFocus.Username && this.Password.Length == 0)
        {
            this.Focus = RegisterFocus.Password;
            return FormEnterResult.MovedFocus;
        }

        if (this.Focus is RegisterFocus.Password or RegisterFocus.ShowPassword && this.Confirm.Length == 0)
        {
            this.Focus = RegisterFocus.Confirm;
            return FormEnterResult.MovedFocus;
        }

        return FormEnterResult.Submit;
    }

    /// <summary>Records a send attempt: every field's error shows from now on.</summary>
    public void MarkSubmitAttempted() => this.SubmitAttempted = true;

    /// <summary>Returns the username error to show now (localization key), or <see langword="null"/>.</summary>
    public string? VisibleUsernameError()
    {
        string user = this.Username.Trim();
        if (user.Length == 0 && !this.SubmitAttempted)
        {
            return null;
        }

        return _usernameTouched || this.SubmitAttempted ? UsernameError(user) : null;
    }

    /// <summary>Returns the password error to show now (localization key), or <see langword="null"/>.</summary>
    public string? VisiblePasswordError()
    {
        if (this.Password.Length == 0 && !this.SubmitAttempted)
        {
            return null;
        }

        return _passwordTouched || this.SubmitAttempted ? PasswordError(this.Password) : null;
    }

    /// <summary>
    /// Returns the confirm error to show now (localization key), or <see langword="null"/>. While the confirm text is
    /// still a beginning of the password, it is "still typing", not wrong, until the field is left or the form sent.
    /// </summary>
    public string? VisibleConfirmError()
    {
        if (this.Confirm.Length == 0)
        {
            return this.SubmitAttempted && this.Password.Length > 0 ? "login.error.confirm_required" : null;
        }

        if (string.Equals(this.Password, this.Confirm, StringComparison.Ordinal))
        {
            return null;
        }

        bool stillTyping = this.Password.StartsWith(this.Confirm, StringComparison.Ordinal) && !_confirmTouched && !this.SubmitAttempted;
        return stillTyping ? null : "login.error.password_mismatch";
    }

    /// <summary>Gets whether both passwords are filled in and equal.</summary>
    public bool PasswordsMatch => this.Confirm.Length > 0 && string.Equals(this.Password, this.Confirm, StringComparison.Ordinal);

    /// <summary>
    /// Returns the localization key of the first problem, or <see langword="null"/> when the form can be sent. Mirrors the
    /// server's registration rules; the server still decides.
    /// </summary>
    public string? Validate()
    {
        if (UsernameError(this.Username.Trim()) is { } userError)
        {
            return userError;
        }

        if (PasswordError(this.Password) is { } passwordError)
        {
            return passwordError;
        }

        return string.Equals(this.Password, this.Confirm, StringComparison.Ordinal) ? null : "login.error.password_mismatch";
    }

    private static string? UsernameError(string user)
    {
        if (user.Length is < AccountRules.MinUsername or > AccountRules.MaxUsername)
        {
            return "login.error.username_length";
        }

        foreach (char ch in user)
        {
            if (!AccountRules.IsUsernameChar(ch))
            {
                return "login.error.username_chars";
            }
        }

        return null;
    }

    private static string? PasswordError(string password) =>
        password.Length is < AccountRules.MinPassword or > AccountRules.MaxPassword ? "login.error.password_length" : null;

    private void Blur(RegisterFocus left)
    {
        switch (left)
        {
            case RegisterFocus.Username:
                _usernameTouched = true;
                break;
            case RegisterFocus.Password:
                _passwordTouched = true;
                break;
            case RegisterFocus.Confirm:
                _confirmTouched = true;
                break;
            case RegisterFocus.ShowPassword:
            case RegisterFocus.Submit:
            default:
                break;
        }
    }
}
