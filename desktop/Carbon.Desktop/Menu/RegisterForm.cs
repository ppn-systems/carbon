// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Menu;

/// <summary>The register screen's controls in tab order.</summary>
internal enum RegisterFocus
{
    Username,
    Password,
    Confirm,
    Submit,
}

/// <summary>State and rules of the register screen (ADR-037): username, password, confirm password, Register.</summary>
internal sealed class RegisterForm
{
    /// <summary>Tab order: username → password → confirm → Register.</summary>
    public static readonly RegisterFocus[] TabOrder = [RegisterFocus.Username, RegisterFocus.Password, RegisterFocus.Confirm, RegisterFocus.Submit];

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Confirm { get; set; } = string.Empty;

    public RegisterFocus Focus { get; set; } = RegisterFocus.Username;

    /// <summary>Moves focus to the next (or, with <paramref name="reverse"/>, previous) control, wrapping around.</summary>
    public void Tab(bool reverse) => this.Focus = FocusRing.Next(TabOrder, this.Focus, reverse);

    /// <summary>
    /// Returns the localization key of the first problem, or <see langword="null"/> when the form can be sent. Mirrors the
    /// server's registration rules; the server still decides.
    /// </summary>
    public string? Validate()
    {
        string user = this.Username.Trim();
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

        if (this.Password.Length is < AccountRules.MinPassword or > AccountRules.MaxPassword)
        {
            return "login.error.password_length";
        }

        return string.Equals(this.Password, this.Confirm, StringComparison.Ordinal) ? null : "login.error.password_mismatch";
    }
}
