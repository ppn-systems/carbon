// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Configuration.Caching;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Config.S2C;

namespace Carbon.Configuration.Sync;

/// <summary>
/// Client side of the server-authoritative config sync. Transport-agnostic: feed it the packets the server
/// sends and send the returned names back in a <see cref="Contracts.Config.C2S.ConfigRequest"/>.
/// </summary>
/// <remarks>
/// Flow:
/// <list type="number">
///   <item><see cref="HandleManifest"/> compares every entry with the hash-verified cache and returns the names to request
///   (empty when the cache is current, in which case <see cref="Ready"/> is raised immediately).</item>
///   <item><see cref="HandleDocument"/> reassembles chunks. Only requested documents whose chunk metadata matches the
///   manifest are accepted; a finished document is accepted only when its hash matches the manifest, then persisted.</item>
///   <item>On the stream terminator, every requested document that did not verify is returned for a re-request,
///   up to <c>maxAttemptsPerDocument</c> attempts; after that the client enters <see cref="ConfigSyncState.Faulted"/>.</item>
/// </list>
/// A new manifest (for example after a server hot reload) restarts the comparison at any time.
/// Not thread-safe: call it from the single thread that dispatches inbound packets.
/// </remarks>
public sealed class ConfigSyncClient
{
    private readonly ConfigDocumentStore _store;
    private readonly int _maxAttemptsPerDocument;
    private readonly Dictionary<string, PendingDocument> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _verified = new(StringComparer.Ordinal);
    private ConfigManifestEntry[] _manifest = [];

    /// <summary>Initializes a new instance of the <see cref="ConfigSyncClient"/> class.</summary>
    /// <param name="store">Hash-verified cache.</param>
    /// <param name="maxAttemptsPerDocument">Download attempts per document before faulting.</param>
    public ConfigSyncClient(ConfigDocumentStore store, int maxAttemptsPerDocument = 3)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttemptsPerDocument, 1);
        _store = store;
        _maxAttemptsPerDocument = maxAttemptsPerDocument;
    }

    /// <summary>Raised when every manifest document is verified, with the new catalog.</summary>
    public event Action<ConfigCatalog>? Ready;

    /// <summary>Raised when the sync faults, with the reason.</summary>
    public event Action<string>? Faulted;

    /// <summary>Gets the current state.</summary>
    public ConfigSyncState State { get; private set; }

    /// <summary>Gets the version of the last accepted manifest.</summary>
    public uint ManifestVersion { get; private set; }

    /// <summary>Gets the reason of the last fault, or <see langword="null"/>.</summary>
    public string? FaultReason { get; private set; }

    /// <summary>Gets the last complete catalog, or <see langword="null"/> before the first successful sync.</summary>
    public ConfigCatalog? Catalog { get; private set; }

    /// <summary>Handles a manifest from the server.</summary>
    /// <param name="manifest">Manifest packet.</param>
    /// <returns>Names to request from the server; empty when nothing needs downloading.</returns>
    public string[] HandleManifest(ConfigManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        _pending.Clear();
        _verified.Clear();
        if (!ConfigManifestParser.TryParse(manifest, out ConfigManifestEntry[]? entries, out string? error))
        {
            this.Fault(error);
            return [];
        }

        _manifest = entries;
        this.ManifestVersion = manifest.ManifestVersion;
        this.FaultReason = null;

        List<string> request = [];
        foreach (ConfigManifestEntry entry in entries)
        {
            if (_store.TryLoad(entry, out byte[]? cached))
            {
                _verified[entry.Name] = cached;
            }
            else
            {
                _pending[entry.Name] = new PendingDocument(entry);
                request.Add(entry.Name);
            }
        }

        if (request.Count == 0)
        {
            this.Complete();
            return [];
        }

        this.State = ConfigSyncState.Downloading;
        return [.. request];
    }

    /// <summary>Handles one document chunk or the stream terminator.</summary>
    /// <param name="chunk">Document packet.</param>
    /// <returns>Names to request again (only at the end of a stream); usually empty.</returns>
    public string[] HandleDocument(ConfigDocument chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (this.State != ConfigSyncState.Downloading)
        {
            return [];
        }

        if (!chunk.IsTerminator && _pending.TryGetValue(chunk.Name ?? string.Empty, out PendingDocument? pending))
        {
            this.Accept(pending, chunk);
            if (_pending.Count == 0)
            {
                this.Complete();
                return [];
            }
        }

        return chunk.IsEndOfStream ? this.EndOfStream() : [];
    }

    private void Accept(PendingDocument pending, ConfigDocument chunk)
    {
        ConfigManifestEntry entry = pending.Entry;
        byte[] payload = chunk.Payload ?? [];

        // Never trust a chunk that disagrees with the manifest or arrives out of order.
        bool consistent = !pending.Rejected
            && chunk.ContentHash == entry.ContentHash
            && chunk.Version == entry.Version
            && chunk.TotalLength == entry.ByteLength
            && chunk.Offset == pending.Received
            && payload.Length <= entry.ByteLength - pending.Received;
        if (!consistent)
        {
            pending.Reject();
            return;
        }

        pending.Buffer ??= new byte[entry.ByteLength];
        payload.CopyTo(pending.Buffer, pending.Received);
        pending.Received += payload.Length;
        if (pending.Received < entry.ByteLength)
        {
            return;
        }

        if (ConfigContentHash.Compute(pending.Buffer) != entry.ContentHash)
        {
            pending.Reject();
            return;
        }

        _store.Save(entry.Name, pending.Buffer);
        _verified[entry.Name] = pending.Buffer;
        _ = _pending.Remove(entry.Name);
    }

    private string[] EndOfStream()
    {
        List<string> retry = new(_pending.Count);
        foreach (PendingDocument pending in _pending.Values)
        {
            if (++pending.Attempts > _maxAttemptsPerDocument)
            {
                this.Fault($"Config document '{pending.Entry.Name}' failed verification {_maxAttemptsPerDocument} times.");
                return [];
            }

            pending.Reset();
            retry.Add(pending.Entry.Name);
        }

        return [.. retry];
    }

    private void Complete()
    {
        ConfigCatalogEntry[] documents = new ConfigCatalogEntry[_manifest.Length];
        for (int i = 0; i < _manifest.Length; i++)
        {
            ConfigManifestEntry entry = _manifest[i];
            documents[i] = new ConfigCatalogEntry(entry.Name, entry.ContentHash, entry.Version, _verified[entry.Name]);
        }

        _verified.Clear();
        this.Catalog = new ConfigCatalog(this.ManifestVersion, documents);
        this.State = ConfigSyncState.Ready;
        Ready?.Invoke(this.Catalog);
    }

    private void Fault(string reason)
    {
        _pending.Clear();
        _verified.Clear();
        this.FaultReason = reason;
        this.State = ConfigSyncState.Faulted;
        Faulted?.Invoke(reason);
    }

    private sealed class PendingDocument(ConfigManifestEntry entry)
    {
        public ConfigManifestEntry Entry { get; } = entry;

        public byte[]? Buffer { get; set; }

        public int Received { get; set; }

        public bool Rejected { get; private set; }

        public int Attempts { get; set; } = 1;

        public void Reject()
        {
            this.Rejected = true;
            this.Buffer = null;
            this.Received = 0;
        }

        public void Reset()
        {
            this.Rejected = false;
            this.Buffer = null;
            this.Received = 0;
        }
    }
}
