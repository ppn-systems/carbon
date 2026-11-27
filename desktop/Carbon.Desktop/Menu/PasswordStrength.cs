// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Menu;

/// <summary>How strong a new password looks; shown as a four-step meter under the register password field.</summary>
internal enum PasswordStrengthLevel
{
    /// <summary>Shorter than the server allows.</summary>
    TooShort,
    Weak,
    Fair,
    Good,
    Strong,
}

/// <summary>
/// A length-first strength estimate (NIST SP 800-63B-4: length matters most, no composition rules). Length sets the
/// base level; using three or more kinds of character lifts it one step; obvious patterns (one repeated character, a
/// straight run like <c>abcdef</c> / <c>123456</c>, or the username inside the password) drop it to weak. Advice only:
/// the server's rules (<see cref="AccountRules"/>) decide what is accepted.
/// </summary>
internal static class PasswordStrength
{
    /// <summary>Rates <paramref name="password"/>; <paramref name="username"/> (may be empty) counts as a weakness.</summary>
    public static PasswordStrengthLevel Evaluate(ReadOnlySpan<char> password, ReadOnlySpan<char> username)
    {
        if (password.Length < AccountRules.MinPassword)
        {
            return PasswordStrengthLevel.TooShort;
        }

        if (IsOneRun(password) || (username.Trim().Length >= AccountRules.MinUsername && password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return PasswordStrengthLevel.Weak;
        }

        int level = password.Length switch
        {
            < 8 => (int)PasswordStrengthLevel.Weak,
            < 12 => (int)PasswordStrengthLevel.Fair,
            < 16 => (int)PasswordStrengthLevel.Good,
            _ => (int)PasswordStrengthLevel.Strong,
        };

        if (CharacterKinds(password) >= 3)
        {
            level++;
        }

        return (PasswordStrengthLevel)Math.Min(level, (int)PasswordStrengthLevel.Strong);
    }

    /// <summary>Returns the localization key of the word shown beside the meter.</summary>
    public static string LabelKey(PasswordStrengthLevel level) => level switch
    {
        PasswordStrengthLevel.TooShort => "login.strength.too_short",
        PasswordStrengthLevel.Weak => "login.strength.weak",
        PasswordStrengthLevel.Fair => "login.strength.fair",
        PasswordStrengthLevel.Good => "login.strength.good",
        PasswordStrengthLevel.Strong => "login.strength.strong",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>True when every character repeats the first one or steps by +1 / -1 from the previous one.</summary>
    private static bool IsOneRun(ReadOnlySpan<char> s)
    {
        bool same = true;
        bool up = true;
        bool down = true;
        for (int i = 1; i < s.Length; i++)
        {
            int step = char.ToLowerInvariant(s[i]) - char.ToLowerInvariant(s[i - 1]);
            same &= step == 0;
            up &= step == 1;
            down &= step == -1;
        }

        return same || up || down;
    }

    private static int CharacterKinds(ReadOnlySpan<char> s)
    {
        bool lower = false;
        bool upper = false;
        bool digit = false;
        bool other = false;
        foreach (char ch in s)
        {
            if (char.IsLower(ch))
            {
                lower = true;
            }
            else if (char.IsUpper(ch))
            {
                upper = true;
            }
            else if (char.IsDigit(ch))
            {
                digit = true;
            }
            else
            {
                other = true;
            }
        }

        return (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (other ? 1 : 0);
    }
}
