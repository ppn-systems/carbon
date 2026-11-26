// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Text;
using Carbon.Primitives.Hashing;
using FluentAssertions;
using Xunit;

namespace Carbon.Primitives.Tests;

public sealed class Fnv1a64Tests
{
    [Theory]
    [InlineData("", 0xCBF29CE484222325UL)]
    [InlineData("a", 0xAF63DC4C8601EC8CUL)]
    [InlineData("foobar", 0x85944171F73967E8UL)]
    public void Hash_KnownVectors_MatchReference(string input, ulong expected) =>
        Fnv1a64.Hash(Encoding.ASCII.GetBytes(input)).Should().Be(expected);

    [Fact]
    public void Append_InTwoParts_EqualsSingleHash()
    {
        byte[] data = Encoding.ASCII.GetBytes("foobar");

        ulong incremental = Fnv1a64.Append(Fnv1a64.Append(Fnv1a64.OffsetBasis, data.AsSpan(0, 3)), data.AsSpan(3));

        incremental.Should().Be(Fnv1a64.Hash(data));
    }
}
