// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Primitives.Tests;

public sealed class GameplayKeyTests
{
    [Fact]
    public void Hash_ReferenceKeyAndMessage_MatchesSipHash24TestVector()
    {
        // SipHash-2-4 reference: key 00..0f, 32-byte message 00..1f (vectors from the SipHash paper's reference code).
        byte[] keyBytes = new byte[GameplayKey.SizeInBytes];
        for (int i = 0; i < keyBytes.Length; i++)
        {
            keyBytes[i] = (byte)i;
        }

        byte[] message = new byte[32];
        for (int i = 0; i < message.Length; i++)
        {
            message[i] = (byte)i;
        }

        GameplayKey key = GameplayKey.FromBytes(keyBytes);
        ulong hash = key.Hash(
            BinaryPrimitives.ReadUInt64LittleEndian(message),
            BinaryPrimitives.ReadUInt64LittleEndian(message.AsSpan(8)),
            BinaryPrimitives.ReadUInt64LittleEndian(message.AsSpan(16)),
            BinaryPrimitives.ReadUInt64LittleEndian(message.AsSpan(24)));

        hash.Should().Be(0x7127512F72F27CCEUL, "the roll PRF is standard SipHash-2-4, not an ad hoc mix");
    }

    [Fact]
    public void FromBytes_WrongLengthOrAllZero_Throws()
    {
        Action shortKey = () => GameplayKey.FromBytes(new byte[8]);
        Action zeroKey = () => GameplayKey.FromBytes(new byte[GameplayKey.SizeInBytes]);

        shortKey.Should().Throw<ArgumentException>();
        zeroKey.Should().Throw<ArgumentException>("an all-zero key is an uninitialized secret");
        default(GameplayKey).IsZero.Should().BeTrue();
    }

    [Fact]
    public void WriteBytes_RoundTripsThroughFromBytes()
    {
        GameplayKey key = TestGameplayKeys.Key(0xC0FFEEUL);
        byte[] bytes = new byte[GameplayKey.SizeInBytes];
        key.WriteBytes(bytes);

        GameplayKey.FromBytes(bytes).Should().Be(key);
    }

    [Fact]
    public void ToString_NeverRevealsTheKeyBytes()
    {
        GameplayKey key = TestGameplayKeys.Key(0x1122334455667788UL);
        byte[] bytes = new byte[GameplayKey.SizeInBytes];
        key.WriteBytes(bytes);

        string text = key.ToString();
        text.Should().Be("GameplayKey(redacted)");
        text.Should().NotContain(Convert.ToHexString(bytes.AsSpan(0, 4)));
        $"{key}".ToUpperInvariant().Should().NotContain("1122334455667788");
    }

    [Fact]
    public void Hash_DifferentKeyOrDomain_GivesUnrelatedOutputs()
    {
        GameplayKey a = TestGameplayKeys.Key(1UL);
        GameplayKey b = TestGameplayKeys.Key(2UL);

        a.Hash(GameplayDomain.Forge, 10UL, 20UL, 30UL).Should().Be(a.Hash(GameplayDomain.Forge, 10UL, 20UL, 30UL), "a PRF is a pure function");
        a.Hash(GameplayDomain.Forge, 10UL, 20UL, 30UL).Should().NotBe(b.Hash(GameplayDomain.Forge, 10UL, 20UL, 30UL), "the key changes every output");
        a.Hash(GameplayDomain.Forge, 10UL, 20UL, 30UL).Should().NotBe(a.Hash(GameplayDomain.Loot, 10UL, 20UL, 30UL), "domains separate roll kinds");
    }

    [Fact]
    public void Derive_SameInputsSameSubkey_DifferentEpochsDifferentSubkeys()
    {
        GameplayKey secret = TestGameplayKeys.Key(77UL);
        HashSet<GameplayKey> subkeys = [];
        for (ulong epoch = 0; epoch < 256; epoch++)
        {
            GameplayKey subkey = secret.Derive(GameplayDomain.Epoch, epoch, 5UL);
            subkey.Should().Be(secret.Derive(GameplayDomain.Epoch, epoch, 5UL));
            subkey.Should().NotBe(secret);
            subkeys.Add(subkey).Should().BeTrue("every epoch gets its own subkey");
        }
    }

    [Fact]
    public void Sample_ManyInputs_IsUniformInUnitInterval()
    {
        GameplayKey key = TestGameplayKeys.Key(9UL);
        const int Samples = 50_000;
        int below = 0;
        for (ulong i = 0; i < Samples; i++)
        {
            double sample = key.Sample(GameplayDomain.FoodPoisoning, i);
            sample.Should().BeInRange(0.0, 1.0);
            below += sample < 0.3 ? 1 : 0;
        }

        (below / (double)Samples).Should().BeApproximately(0.3, 0.01);
    }
}
