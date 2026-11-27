// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Menu;

/// <summary>The login screen's controls in tab order.</summary>
internal enum LoginFocus
{
    Username,
    Password,
    RememberMe,
    Submit,
}

/// <summary>State and rules of the login screen (ADR-037): username, password, "Remember me", Login.</summary>
internal sealed class LoginForm
{
    /// <summary>Tab order: username → password → Remember me → Login.</summary>
    public static readonly LoginFocus[] TabOrder = [LoginFocus.Username, LoginFocus.Password, LoginFocus.RememberMe, LoginFocus.Submit];

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public LoginFocus Focus { get; set; } = LoginFocus.Username;

    /// <summary>Moves focus to the next (or, with <paramref name="reverse"/>, previous) control, wrapping around.</summary>
    public void Tab(bool reverse) => this.Focus = FocusRing.Next(TabOrder, this.Focus, reverse);

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
}
