// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Assets.Decoders;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Assets.Tests;

public sealed class QoiDecoderTests
{
    [Fact]
    public void IsQoi_WithValidMagic_ReturnsTrue()
    {
        // Arrange
        byte[] validHeader = [0x71, 0x6F, 0x69, 0x66, 0, 0, 0, 1, 0, 0, 0, 1, 4, 0];

        // Act & Assert
        QoiDecoder.IsQoi(validHeader).Should().BeTrue();
    }

    [Fact]
    public void IsQoi_WithInvalidMagic_ReturnsFalse()
    {
        // Arrange
        byte[] invalidHeader = [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 1, 0, 0, 0, 1, 4, 0];

        // Act & Assert
        QoiDecoder.IsQoi(invalidHeader).Should().BeFalse();
    }

    [Fact]
    public void Decode_Valid2x2SolidRedImage_DecodesAllPixelsCorrectly()
    {
        // Arrange: 2x2 solid red RGBA QOI
        byte[] qoiData =
        [
            0x71, 0x6F, 0x69, 0x66, // qoif
            0x00, 0x00, 0x00, 0x02, // width = 2
            0x00, 0x00, 0x00, 0x02, // height = 2
            0x04,                   // channels = RGBA
            0x00,                   // colorspace = sRGB
            0xFE, 0xFF, 0x00, 0x00, // QOI_OP_RGB (255, 0, 0, alpha=255) -> pixel 0
            0xC2,                   // QOI_OP_RUN (run count 3 -> pixels 1, 2, 3)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 // 8-byte end marker
        ];

        // Act
        DecodedImage image = QoiDecoder.Decode(qoiData);

        // Assert
        image.Width.Should().Be(2);
        image.Height.Should().Be(2);
        image.Pixels.Length.Should().Be(16); // 2x2 * 4 bytes = 16 bytes

        for (int p = 0; p < 4; p++)
        {
            int baseIdx = p * 4;
            image.Pixels[baseIdx + 0].Should().Be(255); // R
            image.Pixels[baseIdx + 1].Should().Be(0);   // G
            image.Pixels[baseIdx + 2].Should().Be(0);   // B
            image.Pixels[baseIdx + 3].Should().Be(255); // A
        }
    }
}
