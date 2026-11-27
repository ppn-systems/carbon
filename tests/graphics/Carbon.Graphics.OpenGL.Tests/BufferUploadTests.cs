// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.OpenGL.Buffers;
using Carbon.Graphics.OpenGL.States;
using FluentAssertions;
using Silk.NET.OpenGL;
using Xunit;

namespace Carbon.Graphics.OpenGL.Tests;

/// <summary>
/// <c>ReplaceData</c> (every geometry batch flush) orphans the buffer at the size it sends, in one call: re-specifying
/// the whole capacity each flush made the driver allocate unused bytes, which a software rasterizer pays for per byte.
/// A discarding <c>SetData</c> keeps the full capacity, because the sprite batch appends ranges after it.
/// </summary>
public sealed class BufferUploadTests
{
    [Fact]
    public void VertexBufferReplaceData_OrphansAtTheSentSizeInOneCall()
    {
        using GL gl = RecordingGl.Create();
        using VertexBuffer<Vertex16> vbo = new(gl, new GlStateCache(gl), 32768, BufferUsage.DynamicDraw);
        RecordingGl.Calls.Clear();

        vbo.ReplaceData(new Vertex16[100]);

        RecordingGl.Calls.Should().ContainSingle("orphan and upload are one glBufferData");
        RecordingGl.Calls[0].Name.Should().Be("glBufferData");
        RecordingGl.Calls[0].Size.Should().Be(100 * 16, "the store is sized to the 100 vertices sent, not the 32k capacity");
        RecordingGl.Calls[0].HasData.Should().BeTrue();
    }

    [Fact]
    public void IndexBufferReplaceData_OrphansAtTheSentSizeInOneCall()
    {
        using GL gl = RecordingGl.Create();
        using IndexBuffer ebo = new(gl, new GlStateCache(gl), 65536, DrawElementsType.UnsignedInt, BufferUsage.DynamicDraw);
        RecordingGl.Calls.Clear();

        ebo.ReplaceData(new uint[150]);

        RecordingGl.Calls.Should().ContainSingle();
        RecordingGl.Calls[0].Name.Should().Be("glBufferData");
        RecordingGl.Calls[0].Size.Should().Be(150 * sizeof(uint));
    }

    [Fact]
    public void VertexBufferSetData_WithoutDiscard_UpdatesInPlace()
    {
        using GL gl = RecordingGl.Create();
        using VertexBuffer<Vertex16> vbo = new(gl, new GlStateCache(gl), 1024, BufferUsage.DynamicDraw);
        RecordingGl.Calls.Clear();

        vbo.SetData(new Vertex16[10], 20);

        RecordingGl.Calls.Should().ContainSingle();
        RecordingGl.Calls[0].Should().Be(new RecordingGl.BufferCall("glBufferSubData", (uint)GLEnum.ArrayBuffer, 20 * 16, 10 * 16, true));
    }

    [Fact]
    public void VertexBufferSetData_PartialUpdatePastAReplacedStore_Throws()
    {
        using GL gl = RecordingGl.Create();
        using VertexBuffer<Vertex16> vbo = new(gl, new GlStateCache(gl), 1024, BufferUsage.DynamicDraw);
        vbo.ReplaceData(new Vertex16[10]);

        Action pastStore = () => vbo.SetData(new Vertex16[10], 5);

        pastStore.Should().Throw<InvalidOperationException>("the store now holds only the 10 vertices of the last ReplaceData");
    }

    [Fact]
    public void VertexBufferSetData_DiscardThenAppend_KeepsTheFullCapacity()
    {
        // The sprite batch discards at offset 0, then appends texture runs at higher offsets in the same frame.
        using GL gl = RecordingGl.Create();
        using VertexBuffer<Vertex16> vbo = new(gl, new GlStateCache(gl), 1024, BufferUsage.DynamicDraw);
        vbo.ReplaceData(new Vertex16[4]);
        RecordingGl.Calls.Clear();

        vbo.SetData(new Vertex16[8], 0, discardBuffer: true);
        Action append = () => vbo.SetData(new Vertex16[8], 8);

        append.Should().NotThrow("a discarding SetData re-specifies the whole capacity");
        RecordingGl.Calls[0].Should().Be(new RecordingGl.BufferCall("glBufferData", (uint)GLEnum.ArrayBuffer, 0, 1024 * 16, false));
    }

    [Fact]
    public void VertexBufferSetData_PartialUpdateInsideTheStore_IsAllowed()
    {
        using GL gl = RecordingGl.Create();
        using VertexBuffer<Vertex16> vbo = new(gl, new GlStateCache(gl), 1024, BufferUsage.DynamicDraw);
        vbo.ReplaceData(new Vertex16[10]);

        Action inside = () => vbo.SetData(new Vertex16[4], 6);

        inside.Should().NotThrow();
    }

    private readonly record struct Vertex16(float X, float Y, float Z, uint Color);
}
