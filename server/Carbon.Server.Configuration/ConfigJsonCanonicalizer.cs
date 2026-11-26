// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Carbon.Server.Configuration;

/// <summary>
/// Validates a config file as strict JSON and rewrites it in canonical form (no insignificant whitespace,
/// one escaping policy, no BOM), so formatting-only edits do not change the content hash.
/// Property order is preserved: it is part of the document.
/// </summary>
public static class ConfigJsonCanonicalizer
{
    private static readonly JsonDocumentOptions s_readOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 64,
    };

    private static readonly JsonWriterOptions s_writeOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    /// <summary>Canonicalizes <paramref name="utf8Json"/>.</summary>
    /// <param name="utf8Json">Raw file bytes (an optional UTF-8 BOM is ignored).</param>
    /// <returns>Canonical UTF-8 JSON bytes.</returns>
    /// <exception cref="InvalidDataException">Thrown when the input is not strict JSON.</exception>
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (utf8Json.StartsWith(bom))
        {
            utf8Json = utf8Json[bom.Length..];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json.ToArray(), s_readOptions);
            ArrayBufferWriter<byte> buffer = new(utf8Json.Length);
            using (Utf8JsonWriter writer = new(buffer, s_writeOptions))
            {
                document.RootElement.WriteTo(writer);
            }

            return buffer.WrittenSpan.ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid JSON: {ex.Message}", ex);
        }
    }
}
