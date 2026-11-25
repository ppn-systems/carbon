// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Persisted serialized chunk delta payload indexed by chunk grid coordinates.
/// </summary>
/// <param name="ChunkX">Chunk X coordinate.</param>
/// <param name="ChunkY">Chunk Y coordinate.</param>
/// <param name="Revision">Monotonic chunk delta revision.</param>
/// <param name="GeneratorVersion">World generator version the payload belongs to.</param>
/// <param name="Payload">Little-endian serialized <c>ChunkDelta</c> bytes.</param>
/// <param name="UpdatedAtUtc">Last save timestamp.</param>
public sealed record PersistedChunkDelta(
    int ChunkX,
    int ChunkY,
    uint Revision,
    ushort GeneratorVersion,
    byte[] Payload,
    DateTimeOffset UpdatedAtUtc);
