// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Configuration;

/// <summary>One document of a config manifest.</summary>
/// <param name="Name">Document name (see <see cref="Contracts.Config.ConfigDocumentName"/>).</param>
/// <param name="ContentHash">Hash of the canonical bytes (<see cref="ConfigContentHash"/>).</param>
/// <param name="ByteLength">Canonical byte length.</param>
/// <param name="Version">Per-document revision.</param>
public readonly record struct ConfigManifestEntry(string Name, ulong ContentHash, int ByteLength, uint Version);
