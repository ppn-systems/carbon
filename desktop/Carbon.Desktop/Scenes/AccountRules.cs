// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Client-side copies of the server's account rules (<c>AuthService</c>), so the player sees a mistake before sending.
/// The server stays authoritative.
/// </summary>
internal static class AccountRules
{
    public const int MinUsername = 3;
    public const int MaxUsername = 24;
    public const int MinPassword = 6;
    public const int MaxPassword = 128;

    /// <summary>Returns whether <paramref name="ch"/> may appear in a username (ASCII letters, digits, underscore).</summary>
    public static bool IsUsernameChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';
}
