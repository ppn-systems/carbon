// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Assets.Decoders;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Assets.Tests;

public sealed class QoiDecoderOpcodeTests
{
    [Fact]
    public void Decode_OpRgba_ShouldDecodeFullAlphaChannel()
    {
        // 1x1 image with QOI_OP_RGBA
        byte[] qoiData =
        [
            0x71, 0x6F, 0x69, 0x66, // qoif
            0x00, 0x00, 0x00, 0x01, // width = 1
            0x00, 0x00, 0x00, 0x01, // height = 1
            0x04,                   // channels = RGBA
            0x00,                   // colorspace = sRGB
            0xFF, 0x12, 0x34, 0x56, 0x78, // QOI_OP_RGBA (18, 52, 86, 120)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 // 8-byte end marker
        ];

        DecodedImage image = QoiDecoder.Decode(qoiData);

        image.Width.Should().Be(1);
        image.Height.Should().Be(1);
        image.Pixels[0].Should().Be(0x12);
        image.Pixels[1].Should().Be(0x34);
        image.Pixels[2].Should().Be(0x56);
        image.Pixels[3].Should().Be(0x78);
    }

    [Fact]
    public void Decode_OpDiff_ShouldApplySmallDeltas()
    {
        // 2x1 image:
        // pixel 0: QOI_OP_RGB (100, 100, 100, 255)
        // pixel 1: QOI_OP_DIFF: dr = +1, dg = 0, db = -1
        // dr bias is 2: dr=1 => 3 (0b11), dg=0 => 2 (0b10), db=-1 => 1 (0b01)
        // byte = 0x40 | (3 << 4) | (2 << 2) | 1 = 0x40 | 0x30 | 0x08 | 0x01 = 0x79
        byte[] qoiData =
        [
            0x71, 0x6F, 0x69, 0x66,
            0x00, 0x00, 0x00, 0x02, // width = 2
            0x00, 0x00, 0x00, 0x01, // height = 1
            0x04,                   // channels = 4
            0x00,
            0xFE, 100, 100, 100,    // QOI_OP_RGB
            0x79,                   // QOI_OP_DIFF (+1, 0, -1)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01
        ];

        DecodedImage image = QoiDecoder.Decode(qoiData);

        image.Width.Should().Be(2);
        image.Height.Should().Be(1);

        // Pixel 0
        image.Pixels[0].Should().Be(100);
        image.Pixels[1].Should().Be(100);
        image.Pixels[2].Should().Be(100);
        image.Pixels[3].Should().Be(255);

        // Pixel 1
        image.Pixels[4].Should().Be(101);
        image.Pixels[5].Should().Be(100);
        image.Pixels[6].Should().Be(99);
        image.Pixels[7].Should().Be(255);
    }

    [Fact]
    public void Decode_InvalidHeader_ShouldThrowAppropriateExceptions()
    {
        // 1. Data too small
        byte[] tooShort = [0x71, 0x6F, 0x69];
        Action actShort = () => QoiDecoder.Decode(tooShort);
        actShort.Should().Throw<ArgumentException>();

        // 2. Bad magic
        byte[] badMagic =
        [
            0x12, 0x34, 0x56, 0x78,
            0, 0, 0, 1,
            0, 0, 0, 1,
            4, 0,
            0, 0, 0, 0, 0, 0, 0, 1
        ];
        Action actMagic = () => QoiDecoder.Decode(badMagic);
        actMagic.Should().Throw<FormatException>();

        // 3. Zero dimensions
        byte[] zeroDim =
        [
            0x71, 0x6F, 0x69, 0x66,
            0, 0, 0, 0, // width = 0
            0, 0, 0, 1,
            4, 0,
            0, 0, 0, 0, 0, 0, 0, 1
        ];
        Action actZero = () => QoiDecoder.Decode(zeroDim);
        actZero.Should().Throw<FormatException>();
    }
}
