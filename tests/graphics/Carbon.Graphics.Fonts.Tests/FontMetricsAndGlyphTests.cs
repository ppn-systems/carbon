// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Model;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Fonts.Tests;

public sealed class FontMetricsAndGlyphTests
{
    [Fact]
    public void FontMetrics_Constructor_CalculatesDefaultSpaceAndTabWidths()
    {
        FontMetrics metrics = new(fontSize: 24f, lineHeight: 30f, baseLine: 24f);

        metrics.FontSize.Should().Be(24f);
        metrics.LineHeight.Should().Be(30f);
        metrics.BaseLine.Should().Be(24f);
        metrics.SpaceWidth.Should().BeApproximately(24f * 0.33f, 0.001f);
        metrics.TabWidth.Should().BeApproximately(metrics.SpaceWidth * 4f, 0.001f);
    }

    [Fact]
    public void FontMetrics_CustomWidthsAndEquality_ShouldMatch()
    {
        FontMetrics m1 = new(16f, 20f, 16f, spaceWidth: 10f, tabWidth: 40f);
        FontMetrics m2 = new(16f, 20f, 16f, spaceWidth: 10f, tabWidth: 40f);
        FontMetrics m3 = new(16f, 20f, 16f, spaceWidth: 8f, tabWidth: 32f);

        m1.Should().Be(m2);
        (m1 == m2).Should().BeTrue();
        (m1 != m3).Should().BeTrue();
    }

    [Fact]
    public void FontMetrics_InvalidDimensions_ShouldThrowArgumentOutOfRangeException()
    {
        Action actNegativeSize = () => _ = new FontMetrics(-5f, 20f, 16f);
        actNegativeSize.Should().Throw<ArgumentOutOfRangeException>();

        Action actNegativeLineHeight = () => _ = new FontMetrics(16f, 0f, 16f);
        actNegativeLineHeight.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FontGlyph_PropertiesAndEquality_ShouldBeReflexive()
    {
        RectangleF bounds = new(10f, 20f, 16f, 24f);
        FontGlyph glyph = new('A', bounds, xOffset: 1f, yOffset: -2f, xAdvance: 18f, page: 0);

        glyph.Character.Should().Be('A');
        glyph.Bounds.Should().Be(bounds);
        glyph.Width.Should().Be(16f);
        glyph.Height.Should().Be(24f);
        glyph.XOffset.Should().Be(1f);
        glyph.YOffset.Should().Be(-2f);
        glyph.XAdvance.Should().Be(18f);
        glyph.Page.Should().Be(0);
        glyph.IsValid.Should().BeTrue();

        FontGlyph copy = new('A', bounds, 1f, -2f, 18f, 0);
        glyph.Should().Be(copy);
        (glyph == copy).Should().BeTrue();

        FontGlyph.Empty.IsValid.Should().BeFalse();
    }
}
