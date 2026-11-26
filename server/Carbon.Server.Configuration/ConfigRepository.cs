// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Config;
using Carbon.Contracts.Config.C2S;
using Carbon.Contracts.Config.S2C;
using Nalix.Codec.Pooling;

namespace Carbon.Server.Configuration;

/// <summary>
/// The authoritative source of gameplay config: loads every <c>*.json</c> in the config directory
/// (<c>resources/config</c>), canonicalizes and hashes it, builds the manifest clients sync against, and
/// answers <see cref="ConfigRequest"/>s. <see cref="Reload"/> swaps in a new snapshot atomically; when any
/// hash changes, the manifest version increases and <see cref="ManifestChanged"/> fires so the host can push
/// the new manifest to every connected client.
/// </summary>
/// <remarks>Thread-safe: readers see one immutable snapshot; reloads are serialized.</remarks>
public sealed class ConfigRepository
{
    /// <summary>File pattern of config documents.</summary>
    public const string SearchPattern = "*.json";

    private readonly Lock _reloadGate = new();
    private readonly List<Func<ConfigReloadResult, ConfigCatalog, Action>> _reloadSteps = [];
    private readonly FrozenSet<string> _serverOnly;
    private volatile Snapshot _snapshot;

    private ConfigRepository(string directory, Snapshot snapshot, FrozenSet<string> serverOnly)
    {
        this.Directory = directory;
        _snapshot = snapshot;
        _serverOnly = serverOnly;
    }

    /// <summary>Raised after a reload that changed the manifest.</summary>
    public event Action<ConfigReloadResult>? ManifestChanged;

    /// <summary>Gets the absolute config directory.</summary>
    public string Directory { get; }

    /// <summary>Gets the current manifest version.</summary>
    public uint ManifestVersion => _snapshot.Catalog.ManifestVersion;

    /// <summary>Gets every current document entry (server-only ones included), ordered by name.</summary>
    public IReadOnlyList<ConfigManifestEntry> Entries => _snapshot.Entries;

    /// <summary>
    /// Gets the names of documents the server uses but never sends to clients (for example drop rates or price tunables).
    /// They are left out of the manifest, and requests for them are rejected as unknown.
    /// </summary>
    public IReadOnlySet<string> ServerOnlyDocuments => _serverOnly;

    /// <summary>Gets the current documents, for server-side domain loaders.</summary>
    public ConfigCatalog Catalog => _snapshot.Catalog;

    /// <summary>Loads the config directory. Fails fast: the server must not start with broken config.</summary>
    /// <param name="directory">Config directory (normally <c>resources/config</c>).</param>
    /// <param name="serverOnlyDocuments">Documents loaded for the server but never sent to clients.</param>
    /// <returns>The loaded repository.</returns>
    /// <exception cref="InvalidDataException">Thrown when a file is invalid.</exception>
    public static ConfigRepository Load(string directory, IEnumerable<string>? serverOnlyDocuments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.GetFullPath(directory);
        return new ConfigRepository(full, BuildSnapshot(full, previous: null), (serverOnlyDocuments ?? []).ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Registers a reload step for a server domain built from config, so a reload swaps the served manifest and the
    /// running tables together. On each changed reload, <paramref name="prepare"/> receives the diff and the candidate
    /// documents and builds the new domain without touching live state; throwing <see cref="InvalidDataException"/>
    /// rejects the reload, leaving the manifest and every domain unchanged. The returned action is the commit,
    /// which runs only after every step prepared successfully and must not fail.
    /// </summary>
    /// <param name="prepare">Builds the new domain and returns the action that installs it.</param>
    public void AddReloadStep(Func<ConfigReloadResult, ConfigCatalog, Action> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        lock (_reloadGate)
        {
            _reloadSteps.Add(prepare);
        }
    }

    /// <summary>
    /// Creates a repository with no documents and no directory (for hosts or tests without gameplay config).
    /// Its manifest is empty, so clients become ready immediately; <see cref="Reload"/> throws.
    /// </summary>
    /// <returns>An empty repository.</returns>
    public static ConfigRepository CreateEmpty() =>
        new(string.Empty, new Snapshot([], new ConfigCatalog(1, [])), FrozenSet<string>.Empty);

    /// <summary>
    /// Re-reads the config directory. On any invalid file, or any reload step rejecting the new documents, the current
    /// snapshot and every domain are kept and the exception is rethrown, so a bad edit never reaches clients or the server.
    /// </summary>
    /// <returns>What changed.</returns>
    /// <exception cref="InvalidDataException">Thrown when a file is invalid.</exception>
    public ConfigReloadResult Reload()
    {
        ConfigReloadResult result;
        lock (_reloadGate)
        {
            Snapshot previous = _snapshot;
            Snapshot next = BuildSnapshot(this.Directory, previous);
            result = Diff(previous, next);
            if (!result.HasChanges)
            {
                return result;
            }

            // Two-phase: every step prepares against the candidate first, so one invalid domain rejects the whole reload
            // and nothing changes; only then do the steps commit and the served snapshot switch, before clients are told.
            Action[] commits = new Action[_reloadSteps.Count];
            for (int i = 0; i < commits.Length; i++)
            {
                commits[i] = _reloadSteps[i](result, next.Catalog);
            }

            for (int i = 0; i < commits.Length; i++)
            {
                commits[i]();
            }

            _snapshot = next;
        }

        ManifestChanged?.Invoke(result);
        return result;
    }

    /// <summary>Creates the manifest packet of the current snapshot. The caller sends and disposes it.</summary>
    /// <returns>A rented manifest packet.</returns>
    public ConfigManifest CreateManifest()
    {
        Snapshot snapshot = _snapshot;
        List<string> names = new(snapshot.Entries.Length);
        List<ulong> hashes = new(snapshot.Entries.Length);
        List<int> lengths = new(snapshot.Entries.Length);
        List<uint> versions = new(snapshot.Entries.Length);
        foreach (ConfigManifestEntry entry in snapshot.Entries)
        {
            if (_serverOnly.Contains(entry.Name))
            {
                continue;
            }

            names.Add(entry.Name);
            hashes.Add(entry.ContentHash);
            lengths.Add(entry.ByteLength);
            versions.Add(entry.Version);
        }

        ConfigManifest manifest = ConfigManifest.Create();
        manifest.ManifestVersion = snapshot.Catalog.ManifestVersion;
        manifest.Names = [.. names];
        manifest.ContentHashes = [.. hashes];
        manifest.ByteLengths = [.. lengths];
        manifest.Versions = [.. versions];
        return manifest;
    }

    /// <summary>
    /// Validates a client request and, when valid, sends the document stream: the chunks of every requested
    /// document followed by one terminator. Malformed requests are rejected without throwing and without sending.
    /// Each packet is rented for the duration of its <paramref name="send"/> call and returned to the pool afterwards.
    /// </summary>
    /// <param name="request">Untrusted client request.</param>
    /// <param name="send">Sends one packet to the requesting client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation outcome.</returns>
    public async ValueTask<ConfigRequestStatus> HandleRequestAsync(
        ConfigRequest request,
        Func<ConfigDocument, CancellationToken, ValueTask> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(send);

        Snapshot snapshot = _snapshot;
        ConfigRequestStatus status = this.Validate(request, snapshot);
        if (status != ConfigRequestStatus.Ok)
        {
            return status;
        }

        foreach (string name in request.Names)
        {
            ConfigCatalogEntry document = snapshot.Catalog.GetRequired(name);
            int total = document.Utf8.Length;
            int offset = 0;
            do
            {
                int length = Math.Min(ConfigProtocolLimits.ChunkBytes, total - offset);
                using PacketScope<ConfigDocument> lease = PacketFactory<ConfigDocument>.Acquire();
                ConfigDocument chunk = lease.Value;
                chunk.IsEndOfStream = false;
                chunk.IsTerminator = false;
                chunk.Name = document.Name;
                chunk.ContentHash = document.ContentHash;
                chunk.Version = document.Version;
                chunk.TotalLength = total;
                chunk.Offset = offset;
                chunk.Payload = document.Utf8.Slice(offset, length).ToArray();
                await send(chunk, cancellationToken).ConfigureAwait(false);
                offset += length;
            }
            while (offset < total);
        }

        using (PacketScope<ConfigDocument> lease = PacketFactory<ConfigDocument>.Acquire())
        {
            ConfigDocument terminator = lease.Value;
            terminator.IsEndOfStream = true;
            terminator.IsTerminator = true;
            terminator.Name = string.Empty;
            terminator.ContentHash = 0;
            terminator.Version = 0;
            terminator.TotalLength = 0;
            terminator.Offset = 0;
            terminator.Payload = [];
            await send(terminator, cancellationToken).ConfigureAwait(false);
        }

        return ConfigRequestStatus.Ok;
    }

    private ConfigRequestStatus Validate(ConfigRequest request, Snapshot snapshot)
    {
        string[]? names = request.Names;
        if (names is null)
        {
            return ConfigRequestStatus.InvalidName;
        }

        if (names.Length > ConfigProtocolLimits.MaxDocuments)
        {
            return ConfigRequestStatus.TooManyNames;
        }

        if (request.ManifestVersion != snapshot.Catalog.ManifestVersion)
        {
            return ConfigRequestStatus.StaleManifest;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!ConfigDocumentName.IsValid(name))
            {
                return ConfigRequestStatus.InvalidName;
            }

            if (!seen.Add(name))
            {
                return ConfigRequestStatus.DuplicateName;
            }

            if (!snapshot.Catalog.Contains(name) || _serverOnly.Contains(name))
            {
                return ConfigRequestStatus.UnknownName;
            }
        }

        return ConfigRequestStatus.Ok;
    }

    private static Snapshot BuildSnapshot(string directory, Snapshot? previous)
    {
        if (directory.Length == 0 || !System.IO.Directory.Exists(directory))
        {
            throw new InvalidDataException($"Config directory '{directory}' does not exist.");
        }

        string[] files = System.IO.Directory.GetFiles(directory, SearchPattern, SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length > ConfigProtocolLimits.MaxDocuments)
        {
            throw new InvalidDataException($"{files.Length} config documents exceed the limit of {ConfigProtocolLimits.MaxDocuments}.");
        }

        List<ConfigManifestEntry> entries = new(files.Length);
        List<ConfigCatalogEntry> documents = new(files.Length);
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!ConfigDocumentName.IsValid(name))
            {
                throw new InvalidDataException($"Config file '{Path.GetFileName(file)}' must be named [a-z0-9_-] (max {ConfigProtocolLimits.MaxNameLength} chars).");
            }

            byte[] canonical;
            try
            {
                canonical = ConfigJsonCanonicalizer.Canonicalize(File.ReadAllBytes(file));
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"Config file '{Path.GetFileName(file)}': {ex.Message}", ex);
            }

            if (canonical.Length > ConfigProtocolLimits.MaxDocumentBytes)
            {
                throw new InvalidDataException($"Config file '{Path.GetFileName(file)}' exceeds {ConfigProtocolLimits.MaxDocumentBytes} bytes.");
            }

            ulong hash = ConfigContentHash.Compute(canonical);
            uint version = 1;
            if (previous is not null && previous.Catalog.TryGet(name, out ConfigCatalogEntry? old))
            {
                version = old.ContentHash == hash ? old.Version : old.Version + 1;
            }

            entries.Add(new ConfigManifestEntry(name, hash, canonical.Length, version));
            documents.Add(new ConfigCatalogEntry(name, hash, version, canonical));
        }

        uint manifestVersion = 1;
        if (previous is not null)
        {
            Snapshot candidate = new([.. entries], new ConfigCatalog(previous.Catalog.ManifestVersion, documents));
            manifestVersion = Diff(previous, candidate).HasChanges
                ? previous.Catalog.ManifestVersion + 1
                : previous.Catalog.ManifestVersion;
        }

        return new Snapshot([.. entries], new ConfigCatalog(manifestVersion, documents));
    }

    private static ConfigReloadResult Diff(Snapshot previous, Snapshot next)
    {
        List<string> changed = [];
        foreach (ConfigManifestEntry entry in next.Entries)
        {
            if (!previous.Catalog.TryGet(entry.Name, out ConfigCatalogEntry? old) || old.ContentHash != entry.ContentHash)
            {
                changed.Add(entry.Name);
            }
        }

        List<string> removed = [];
        foreach (ConfigManifestEntry entry in previous.Entries)
        {
            if (!next.Catalog.Contains(entry.Name))
            {
                removed.Add(entry.Name);
            }
        }

        return new ConfigReloadResult(next.Catalog.ManifestVersion, changed, removed);
    }

    private sealed record Snapshot(ConfigManifestEntry[] Entries, ConfigCatalog Catalog);
}
