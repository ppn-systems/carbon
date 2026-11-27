// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Menu;

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
    public const int MinHeroName = 2;
    public const int MaxHeroName = 20;

    /// <summary>Returns whether <paramref name="ch"/> may appear in a username (ASCII letters, digits, underscore).</summary>
    public static bool IsUsernameChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';

    /// <summary>
    /// Returns whether <paramref name="ch"/> may appear in a hero name. The server accepts the same set as usernames
    /// (<c>AuthService.IsValidIdentifier</c>): no spaces, no accented letters.
    /// </summary>
    public static bool IsHeroNameChar(char ch) => IsUsernameChar(ch);

    /// <summary>Returns the localization key of what is wrong with a hero name, or <see langword="null"/> when the server will take it.</summary>
    public static string? HeroNameError(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < MinHeroName or > MaxHeroName)
        {
            return "character.name.length";
        }

        foreach (char ch in trimmed)
        {
            if (!IsHeroNameChar(ch))
            {
                return "character.name.chars";
            }
        }

        return null;
    }
}
