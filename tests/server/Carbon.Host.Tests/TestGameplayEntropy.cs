// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using Carbon.Application.World.Entropy;
using Carbon.Primitives.Random;

namespace Carbon.Host.Tests;

/// <summary>Fixed gameplay secrets for deterministic server tests (ADR-026). Never a production key source.</summary>
internal static class TestGameplayEntropy
{
    /// <summary>Gets a fixed, non-zero secret labelled by <paramref name="label"/>.</summary>
    public static GameplayKey Key(ulong label)
    {
        Span<byte> bytes = stackalloc byte[GameplayKey.SizeInBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, label);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], ~label);
        return GameplayKey.FromBytes(bytes);
    }

    /// <summary>Creates an entropy source from the secret labelled <paramref name="label"/> and a fixed boot salt.</summary>
    public static GameplayEntropy Create(ulong label, ulong bootSalt = 0UL, ushort tickRate = 30, int epochSeconds = GameplayEntropy.DefaultEpochSeconds) =>
        new(Key(label), bootSalt, tickRate, epochSeconds);
}
