// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Config;

/// <summary>
/// Hard limits of the config sync protocol. Both sides enforce them; the server rejects requests
/// that exceed them and the client rejects manifests or chunks that exceed them.
/// </summary>
public static class ConfigProtocolLimits
{
    /// <summary>Maximum number of documents in a manifest and names in one request.</summary>
    public const int MaxDocuments = 64;

    /// <summary>Maximum length of a document name, in characters.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Maximum canonical size of one document, in bytes (4 MiB).</summary>
    public const int MaxDocumentBytes = 4 * 1024 * 1024;

    /// <summary>Payload bytes carried by one <see cref="S2C.ConfigDocument"/> chunk (16 KiB).</summary>
    public const int ChunkBytes = 16 * 1024;
}
