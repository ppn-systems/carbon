// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.OpenGL.Buffers;
using FluentAssertions;
using Silk.NET.OpenGL;
using Xunit;

namespace Carbon.Graphics.OpenGL.Tests;

public sealed class VertexLayoutTests
{
    [Fact]
    public void VertexLayout_Constructor_InitializesStrideAndElementsCorrectly()
    {
        // Arrange
        VertexElement[] elements =
        [
            new VertexElement(0, 3, VertexAttribPointerType.Float, false, 0),
            new VertexElement(1, 4, VertexAttribPointerType.UnsignedByte, true, 12),
            new VertexElement(2, 2, VertexAttribPointerType.Float, false, 16)
        ];

        // Act
        VertexLayout layout = new(24, elements);

        // Assert
        layout.Stride.Should().Be(24);
        layout.Elements.Should().HaveCount(3);
        layout.Elements[0].Location.Should().Be(0);
        layout.Elements[0].Offset.Should().Be(0);
        layout.Elements[1].Offset.Should().Be(12);
        layout.Elements[2].Offset.Should().Be(16);
    }
}
