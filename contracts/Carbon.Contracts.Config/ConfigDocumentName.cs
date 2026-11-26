// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Config;

/// <summary>
/// Validation rule for config document names: the file stem of <c>resources/config/&lt;name&gt;.json</c>.
/// A valid name is 1–<see cref="ConfigProtocolLimits.MaxNameLength"/> characters of <c>[a-z0-9_-]</c>,
/// starting with a letter or digit. The rule also makes the name safe to use as a cache file name.
/// </summary>
public static class ConfigDocumentName
{
    /// <summary>Determines whether <paramref name="name"/> is a valid document name.</summary>
    /// <param name="name">Candidate name.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > ConfigProtocolLimits.MaxNameLength)
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool alnum = c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
            if (!alnum && (i == 0 || c is not ('_' or '-')))
            {
                return false;
            }
        }

        return true;
    }
}
