// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.OpenGL.Buffers;
using FluentAssertions;
using Silk.NET.OpenGL;
using Xunit;

namespace Carbon.Graphics.OpenGL.Tests;

public sealed class VertexElementTests
{
    [Fact]
    public void VertexElement_ConstructorAndProperties_ShouldMatch()
    {
        VertexElement element = new(
            Location: 0,
            ComponentCount: 3,
            Type: VertexAttribPointerType.Float,
            Normalized: false,
            Offset: 12);

        element.Location.Should().Be(0u);
        element.ComponentCount.Should().Be(3);
        element.Type.Should().Be(VertexAttribPointerType.Float);
        element.Normalized.Should().BeFalse();
        element.Offset.Should().Be(12u);
    }

    [Fact]
    public void VertexElement_ValueEquality_ShouldBeReflexive()
    {
        VertexElement a = new(1, 2, VertexAttribPointerType.Float, false, 8);
        VertexElement b = new(1, 2, VertexAttribPointerType.Float, false, 8);
        VertexElement c = new(2, 2, VertexAttribPointerType.Float, false, 8);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
    }
}
