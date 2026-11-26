// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Server.Configuration;

/// <summary>Outcome of a <see cref="ConfigRepository.Reload"/>.</summary>
/// <param name="ManifestVersion">Manifest version after the reload.</param>
/// <param name="Changed">Documents added or whose hash changed.</param>
/// <param name="Removed">Documents that no longer exist.</param>
public sealed record ConfigReloadResult(uint ManifestVersion, IReadOnlyList<string> Changed, IReadOnlyList<string> Removed)
{
    /// <summary>Gets a value indicating whether the manifest changed (clients must receive the new manifest).</summary>
    public bool HasChanges => this.Changed.Count > 0 || this.Removed.Count > 0;
}
