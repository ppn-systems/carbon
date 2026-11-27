// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Infrastructure.Security;
using FluentAssertions;
using Nalix.Abstractions.Primitives;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>The PBKDF2 hasher on <see cref="Bytes32"/>: what verifies and what must not.</summary>
public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_RightPassword_IsTrue()
    {
        _hasher.HashPassword("open sesame", out Bytes32 hash, out byte[] salt);

        _hasher.Verify("open sesame", hash, salt).Should().BeTrue();
        hash.IsZero.Should().BeFalse("a PBKDF2 key is never all zeroes");
        salt.Should().HaveCount(16);
    }

    [Fact]
    public void Verify_WrongPassword_IsFalse()
    {
        _hasher.HashPassword("open sesame", out Bytes32 hash, out byte[] salt);

        _hasher.Verify("open sesamE", hash, salt).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    public void Verify_WrongHashOfTheSameLength_IsFalse(int flippedByte)
    {
        _hasher.HashPassword("open sesame", out Bytes32 hash, out byte[] salt);
        byte[] bytes = hash.ToByteArray();
        bytes[flippedByte] ^= 0x80;
        Bytes32 tampered = new(bytes);

        _hasher.Verify("open sesame", tampered, salt).Should().BeFalse("a 32-byte hash off by one bit must not verify");
    }

    [Fact]
    public void Verify_WrongSalt_IsFalse()
    {
        _hasher.HashPassword("open sesame", out Bytes32 hash, out byte[] salt);
        salt[0] ^= 0x01;

        _hasher.Verify("open sesame", hash, salt).Should().BeFalse();
    }

    [Fact]
    public void Verify_ZeroHashEmptySaltOrEmptyPassword_IsFalse()
    {
        _hasher.HashPassword("open sesame", out Bytes32 hash, out byte[] salt);

        _hasher.Verify("open sesame", Bytes32.Zero, salt).Should().BeFalse("an unreadable stored hash reads as zero");
        _hasher.Verify("open sesame", hash, ReadOnlySpan<byte>.Empty).Should().BeFalse();
        _hasher.Verify(string.Empty, hash, salt).Should().BeFalse();
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_DiffersBySalt()
    {
        _hasher.HashPassword("open sesame", out Bytes32 first, out byte[] firstSalt);
        _hasher.HashPassword("open sesame", out Bytes32 second, out byte[] secondSalt);

        first.Should().NotBe(second);
        firstSalt.Should().NotEqual(secondSalt);
    }
}
