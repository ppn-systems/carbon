// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Carbon.Contracts.Config.S2C;

namespace Carbon.Configuration.Tests;

/// <summary>Minimal in-test stand-in for the server: builds manifests and chunked document streams.</summary>
internal static class ConfigTestServer
{
    public static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    public static ConfigManifest Manifest(uint manifestVersion, params (string Name, byte[] Bytes, uint Version)[] documents) => new()
    {
        ManifestVersion = manifestVersion,
        Names = [.. documents.Select(static d => d.Name)],
        ContentHashes = [.. documents.Select(static d => ConfigContentHash.Compute(d.Bytes))],
        ByteLengths = [.. documents.Select(static d => d.Bytes.Length)],
        Versions = [.. documents.Select(static d => d.Version)],
    };

    /// <summary>Chunks <paramref name="bytes"/>; the advertised hash is always the hash of <paramref name="advertised"/>.</summary>
    public static List<ConfigDocument> Chunks(string name, byte[] bytes, uint version, int chunkBytes, byte[]? advertised = null)
    {
        byte[] reference = advertised ?? bytes;
        List<ConfigDocument> chunks = [];
        int offset = 0;
        do
        {
            int length = Math.Min(chunkBytes, bytes.Length - offset);
            chunks.Add(new ConfigDocument
            {
                Name = name,
                ContentHash = ConfigContentHash.Compute(reference),
                Version = version,
                TotalLength = reference.Length,
                Offset = offset,
                Payload = bytes.AsSpan(offset, length).ToArray(),
            });
            offset += length;
        }
        while (offset < bytes.Length);

        return chunks;
    }

    public static ConfigDocument Terminator() => new() { IsEndOfStream = true, IsTerminator = true };
}
