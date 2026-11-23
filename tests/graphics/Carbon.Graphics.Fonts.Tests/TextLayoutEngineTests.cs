// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Fonts.Tests;

public sealed class TextLayoutEngineTests
{
    private readonly BitmapFontAtlas _atlas;
    private readonly FontMetrics _metrics;

    public TextLayoutEngineTests()
    {
        ITexture fakeTexture = Substitute.For<ITexture>();
        _atlas = new BitmapFontAtlas(fakeTexture);

        // Register sample character glyphs: each letter is 10px wide with 10px advance
        for (char c = 'a'; c <= 'z'; c++)
        {
            _atlas.AddGlyph(new FontGlyph(c, new RectangleF(0f, 0f, 10f, 16f), 0f, 0f, 10f));
        }

        _metrics = new FontMetrics(fontSize: 16f, lineHeight: 20f, baseLine: 16f, spaceWidth: 8f);
    }

    [Fact]
    public void Measure_SingleLineText_CalculatesTotalWidthAndLineCountAccurately()
    {
        // Arrange
        ReadOnlySpan<char> text = "hello"; // 5 letters * 10 advance = 50px

        // Act
        TextMeasureResult result = TextLayoutEngine.Measure(_atlas, _metrics, text, TextLayoutOptions.Default);

        // Assert
        result.LineCount.Should().Be(1);
        result.Width.Should().Be(50f);
        result.Height.Should().Be(20f);
    }

    [Fact]
    public void Measure_WithExplicitNewlines_IncrementsLineCountAndAccumulatesHeight()
    {
        // Arrange
        ReadOnlySpan<char> text = "hello\nworld";

        // Act
        TextMeasureResult result = TextLayoutEngine.Measure(_atlas, _metrics, text, TextLayoutOptions.Default);

        // Assert
        result.LineCount.Should().Be(2);
        result.Width.Should().Be(50f);
        result.Height.Should().Be(40f);
    }

    [Fact]
    public void Measure_WithWordWrap_BreaksAtSpacesWhenExceedingMaxWidth()
    {
        // Arrange
        ReadOnlySpan<char> text = "hello world"; // 50px + 8px space + 50px = 108px
        TextLayoutOptions options = new(maxWidth: 60f, wordWrap: true);

        // Act
        TextMeasureResult result = TextLayoutEngine.Measure(_atlas, _metrics, text, options);

        // Assert
        result.LineCount.Should().Be(2);
        result.Width.Should().Be(50f);
    }
}

