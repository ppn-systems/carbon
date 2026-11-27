// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class CharacterProgressionCodecCacheTests
{
    [Fact]
    public void Serialize_WithAttributesAndSkills_MatchesTheDocumentedLayout()
    {
        CharacterProgressionState state = Sample();

        byte[] blob = CharacterProgressionBinaryCodec.Serialize(state);

        blob.Should().Equal(ExpectedLayout(state), "the list-free rewrite must write the same bytes as before");
    }

    [Fact]
    public void Serialize_RoundTrip_RestoresEveryValue()
    {
        CharacterProgressionState state = Sample();
        CharacterProgressionState copy = new();

        CharacterProgressionBinaryCodec.TryDeserializeInto(CharacterProgressionBinaryCodec.Serialize(state), copy).Should().BeTrue();

        copy.GetAttribute(PrimaryAttributeId.Vitality).Should().Be(5);
        copy.GetAttribute(PrimaryAttributeId.Wisdom).Should().Be(2);
        copy.GetSkillRank(3).Should().Be(2);
        copy.GetSkillRank(7).Should().Be(1);
        copy.UnlockedSkillMask.Should().Be(state.UnlockedSkillMask);
    }

    [Fact]
    public void GetOrSerializeShared_StateUnchanged_ReturnsTheSameArray()
    {
        CharacterProgressionState state = Sample();

        byte[] first = CharacterProgressionBinaryCodec.GetOrSerializeShared(state);
        byte[] second = CharacterProgressionBinaryCodec.GetOrSerializeShared(state);

        second.Should().BeSameAs(first, "an unchanged state must not be re-serialized");
        first.Should().Equal(CharacterProgressionBinaryCodec.Serialize(state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GetOrSerializeShared_AfterAnyMutation_ReturnsFreshBytes(int mutation)
    {
        CharacterProgressionState state = Sample();
        byte[] before = CharacterProgressionBinaryCodec.GetOrSerializeShared(state);

        switch (mutation)
        {
            case 0: state.SetAttribute(PrimaryAttributeId.Agility, 9); break;
            case 1: state.AddAttribute(PrimaryAttributeId.Vitality, 1); break;
            case 2: state.SetSkillRank(3, 3); break;
            case 3: state.SetUnlockedSkillMask(CharacterSkillMask.None); break;
            case 4: state.Clear(); break;
            default: new CharacterProgressionState().CopyTo(state); break;
        }

        byte[] after = CharacterProgressionBinaryCodec.GetOrSerializeShared(state);

        after.Should().NotBeSameAs(before);
        after.Should().Equal(CharacterProgressionBinaryCodec.Serialize(state), "the cache must follow every mutation");
    }

    private static CharacterProgressionState Sample()
    {
        CharacterProgressionState state = new();
        state.SetAttribute(PrimaryAttributeId.Vitality, 5);
        state.SetAttribute(PrimaryAttributeId.Wisdom, 2);
        state.SetSkillRank(3, 2);
        state.SetSkillRank(7, 1);
        return state;
    }

    /// <summary>The layout the codec wrote before it stopped building a skill list: header, then attributes, then skills.</summary>
    private static byte[] ExpectedLayout(CharacterProgressionState state)
    {
        List<(PrimaryAttributeId Id, ushort Points)> attributes = [];
        state.CopyNonZeroAttributes(attributes);
        List<(ushort SkillId, byte Rank)> skills = [];
        state.CopyLearnedSkills(skills);

        byte[] expected = new byte[12 + (attributes.Count * 3) + (skills.Count * 3)];
        Span<byte> span = expected;
        span[0] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(1, 8), (ulong)state.UnlockedSkillMask);
        span[9] = (byte)attributes.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10, 2), (ushort)skills.Count);
        int offset = 12;
        foreach ((PrimaryAttributeId id, ushort points) in attributes)
        {
            span[offset] = (byte)id;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset + 1, 2), points);
            offset += 3;
        }

        foreach ((ushort skillId, byte rank) in skills)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), skillId);
            span[offset + 2] = rank;
            offset += 3;
        }

        return expected;
    }
}
