// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using Carbon.Primitives.Random;

namespace Carbon.Survival.Tests;

/// <summary>Fixed gameplay keys for deterministic tests (ADR-026). Never a production key source.</summary>
internal static class TestGameplayKeys
{
    /// <summary>Gets a fixed, non-zero key labelled by <paramref name="label"/>.</summary>
    public static GameplayKey Key(ulong label)
    {
        Span<byte> bytes = stackalloc byte[GameplayKey.SizeInBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, label);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], ~label);
        return GameplayKey.FromBytes(bytes);
    }

    /// <summary>Gets an entropy source that returns <see cref="Key"/> for every tick (no rotation).</summary>
    public static IGameplayEntropy Entropy(ulong label) => new FixedEntropy(Key(label));

    private sealed class FixedEntropy(GameplayKey key) : IGameplayEntropy
    {
        public GameplayKey KeyAt(ulong tick) => key;
    }
}
