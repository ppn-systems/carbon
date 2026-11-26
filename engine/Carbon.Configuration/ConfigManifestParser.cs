// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Carbon.Contracts.Config;
using Carbon.Contracts.Config.S2C;

namespace Carbon.Configuration;

/// <summary>
/// Converts a <see cref="ConfigManifest"/> packet to entries, rejecting any manifest that breaks the protocol
/// limits (mismatched arrays, invalid or duplicate names, oversize documents).
/// </summary>
public static class ConfigManifestParser
{
    /// <summary>Validates and converts <paramref name="manifest"/>.</summary>
    /// <param name="manifest">Received manifest packet.</param>
    /// <param name="entries">Entries in manifest order when valid.</param>
    /// <param name="error">Reason the manifest was rejected.</param>
    /// <returns><see langword="true"/> when the manifest is well-formed.</returns>
    public static bool TryParse(
        ConfigManifest manifest,
        [NotNullWhen(true)] out ConfigManifestEntry[]? entries,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        entries = null;

        string[] names = manifest.Names ?? [];
        int count = names.Length;
        if (count > ConfigProtocolLimits.MaxDocuments)
        {
            error = $"Manifest lists {count} documents; the limit is {ConfigProtocolLimits.MaxDocuments}.";
            return false;
        }

        if (manifest.ContentHashes?.Length != count || manifest.ByteLengths?.Length != count || manifest.Versions?.Length != count)
        {
            error = "Manifest arrays have different lengths.";
            return false;
        }

        ConfigManifestEntry[] result = new ConfigManifestEntry[count];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string name = names[i];
            if (!ConfigDocumentName.IsValid(name) || !seen.Add(name))
            {
                error = $"Manifest entry {i} has an invalid or duplicate name.";
                return false;
            }

            int length = manifest.ByteLengths[i];
            if (length is < 0 or > ConfigProtocolLimits.MaxDocumentBytes)
            {
                error = $"Manifest entry '{name}' has an invalid byte length {length}.";
                return false;
            }

            result[i] = new ConfigManifestEntry(name, manifest.ContentHashes[i], length, manifest.Versions[i]);
        }

        entries = result;
        error = null;
        return true;
    }
}
