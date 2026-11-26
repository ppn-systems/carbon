// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
    private volatile Snapshot _snapshot;

    private ConfigRepository(string directory, Snapshot snapshot)
    {
        this.Directory = directory;
        _snapshot = snapshot;
    }

    /// <summary>Raised after a reload that changed the manifest.</summary>
    public event Action<ConfigReloadResult>? ManifestChanged;

    /// <summary>Gets the absolute config directory.</summary>
    public string Directory { get; }

    /// <summary>Gets the current manifest version.</summary>
    public uint ManifestVersion => _snapshot.Catalog.ManifestVersion;

    /// <summary>Gets the current manifest entries, ordered by name.</summary>
    public IReadOnlyList<ConfigManifestEntry> Entries => _snapshot.Entries;

    /// <summary>Gets the current documents, for server-side domain loaders.</summary>
    public ConfigCatalog Catalog => _snapshot.Catalog;

    /// <summary>Loads the config directory. Fails fast: the server must not start with broken config.</summary>
    /// <param name="directory">Config directory (normally <c>resources/config</c>).</param>
    /// <returns>The loaded repository.</returns>
    /// <exception cref="InvalidDataException">Thrown when a file is invalid.</exception>
    public static ConfigRepository Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.GetFullPath(directory);
        return new ConfigRepository(full, BuildSnapshot(full, previous: null));
    }

    /// <summary>
    /// Re-reads the config directory. On any invalid file the current snapshot is kept and the exception is
    /// rethrown, so a bad edit never reaches clients.
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
        int count = snapshot.Entries.Length;
        string[] names = new string[count];
        ulong[] hashes = new ulong[count];
        int[] lengths = new int[count];
        uint[] versions = new uint[count];
        for (int i = 0; i < count; i++)
        {
            ConfigManifestEntry entry = snapshot.Entries[i];
            names[i] = entry.Name;
            hashes[i] = entry.ContentHash;
            lengths[i] = entry.ByteLength;
            versions[i] = entry.Version;
        }

        ConfigManifest manifest = ConfigManifest.Create();
        manifest.ManifestVersion = snapshot.Catalog.ManifestVersion;
        manifest.Names = names;
        manifest.ContentHashes = hashes;
        manifest.ByteLengths = lengths;
        manifest.Versions = versions;
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
        ConfigRequestStatus status = Validate(request, snapshot);
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

    private static ConfigRequestStatus Validate(ConfigRequest request, Snapshot snapshot)
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

            if (!snapshot.Catalog.Contains(name))
            {
                return ConfigRequestStatus.UnknownName;
            }
        }

        return ConfigRequestStatus.Ok;
    }

    private static Snapshot BuildSnapshot(string directory, Snapshot? previous)
    {
        if (!System.IO.Directory.Exists(directory))
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
