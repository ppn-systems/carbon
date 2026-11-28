// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Menu;

/// <summary>The login screen's controls in tab order.</summary>
internal enum LoginFocus
{
    Username,
    Password,
    ShowPassword,
    RememberMe,
    Submit,
}

/// <summary>What Enter does on a form.</summary>
internal enum FormEnterResult
{
    /// <summary>Focus moved to the next empty field instead of sending.</summary>
    MovedFocus,

    /// <summary>The form should be sent.</summary>
    Submit,
}

/// <summary>
/// State and rules of the login screen (ADR-037): username, password, show-password, "Remember me", Login. Errors follow
/// "reward early, punish late" (docs/art/menu-ui.md): a field is checked once the player leaves it or tries to send,
/// an empty field is never flagged before a send, and a shown error re-checks on every keystroke.
/// </summary>
internal sealed class LoginForm
{
    /// <summary>Tab order: username → password → show password → Remember me → Login.</summary>
    public static readonly LoginFocus[] TabOrder = [LoginFocus.Username, LoginFocus.Password, LoginFocus.ShowPassword, LoginFocus.RememberMe, LoginFocus.Submit];

    private LoginFocus _focus = LoginFocus.Username;
    private bool _passwordTouched;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    /// <summary>Gets whether the player has tried to send the form.</summary>
    public bool SubmitAttempted { get; private set; }

    /// <summary>Gets or sets the focused control; leaving a field marks it checked.</summary>
    public LoginFocus Focus
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

    /// <summary>Moves focus to the next (or, with <paramref name="reverse"/>, previous) control, wrapping around.</summary>
    public void Tab(bool reverse) => this.Focus = FocusRing.Next(TabOrder, this.Focus, reverse);

    /// <summary>
    /// Enter: in the username field with no password yet, moves on to the password; anywhere else, sends.
    /// </summary>
    public FormEnterResult Enter()
    {
        if (this.Focus == LoginFocus.Username && this.Password.Length == 0)
        {
            this.Focus = LoginFocus.Password;
            return FormEnterResult.MovedFocus;
        }

        return FormEnterResult.Submit;
    }

    /// <summary>Records a send attempt: every field's error shows from now on.</summary>
    public void MarkSubmitAttempted() => this.SubmitAttempted = true;

    /// <summary>Returns the localization key of the username error to show now, or <see langword="null"/>.</summary>
    public string? VisibleUsernameError() =>
        this.Username.Trim().Length == 0 && this.SubmitAttempted ? "login.error.username_required" : null;

    /// <summary>Returns the localization key of the password error to show now, or <see langword="null"/>.</summary>
    public string? VisiblePasswordError()
    {
        if (this.Password.Length == 0)
        {
            return this.SubmitAttempted ? "login.error.password_required" : null;
        }

        return this.Password.Length > AccountRules.MaxPassword && (_passwordTouched || this.SubmitAttempted) ? "login.error.password_length" : null;
    }

    /// <summary>Returns the localization key of the first problem, or <see langword="null"/> when the form can be sent.</summary>
    public string? Validate()
    {
        string user = this.Username.Trim();
        if (user.Length == 0 || this.Password.Length == 0)
        {
            return "auth.credentials_required";
        }

        return this.Password.Length > AccountRules.MaxPassword ? "login.error.password_length" : null;
    }

    private void Blur(LoginFocus left)
    {
        if (left == LoginFocus.Password)
        {
            _passwordTouched = true;
        }
    }
}
