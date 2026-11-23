// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Assets.Decoders;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Assets.Tests;

public sealed class ImageDecoderTests
{
    [Fact]
    public void Decode_WhenDataIsEmpty_ThrowsArgumentException()
    {
        // Act
        Action act = () => ImageDecoder.Decode(ReadOnlySpan<byte>.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WhenFormatUnrecognized_ThrowsInvalidOperationException()
    {
        // Arrange
        byte[] invalidData = [0x01, 0x02, 0x03, 0x04];

        // Act
        Action act = () => ImageDecoder.Decode(invalidData);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}

