// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Procedural;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Batching.Tests;

public sealed class ChunkMeshBuilderTests
{
    [Fact]
    public void AddTile_ShouldIncreaseVertexCountByFour()
    {
        ChunkMeshBuilder builder = new();
        builder.VertexCount.Should().Be(0);

        builder.AddTile(
            position: new Vector2(0f, 0f),
            size: new Vector2(32f, 32f),
            uvRect: new RectangleF(0f, 0f, 1f, 1f),
            color: ColorRgba.White,
            depth: 0.5f);

        builder.VertexCount.Should().Be(4);

        builder.AddTile(
            position: new Vector2(32f, 0f),
            size: new Vector2(32f, 32f),
            uvRect: new RectangleF(0f, 0f, 1f, 1f),
            color: ColorRgba.White,
            depth: 0.5f);

        builder.VertexCount.Should().Be(8);
    }

    [Fact]
    public void AddRectangle_ShouldAppendFourVerticesAndSixIndices()
    {
        ChunkMeshBuilder builder = new();
        RectangleF rect = new(10f, 20f, 32f, 32f);

        builder.AddRectangle(rect, ColorRgba.Green, depth: 0.1f);

        builder.VertexCount.Should().Be(4);
        builder.IndexCount.Should().Be(6);
    }

    [Fact]
    public void AddTriangle_ShouldAppendThreeVerticesAndThreeIndices()
    {
        ChunkMeshBuilder builder = new();
        Vector2 p0 = new(0f, 0f);
        Vector2 p1 = new(10f, 0f);
        Vector2 p2 = new(0f, 10f);

        builder.AddTriangle(p0, p1, p2, ColorRgba.Blue);

        builder.VertexCount.Should().Be(3);
        builder.IndexCount.Should().Be(3);
    }

    [Fact]
    public void AddTriangle_WithVertexColours_ShouldAppendThreeVerticesAndThreeIndices()
    {
        ChunkMeshBuilder builder = new();

        builder.AddTriangle(new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(0f, 10f), ColorRgba.Red, ColorRgba.Green, ColorRgba.Blue);

        builder.VertexCount.Should().Be(3);
        builder.IndexCount.Should().Be(3);
    }

    [Fact]
    public void AddGradientRectangle_ShouldAppendFiveVerticesAndFourTriangles()
    {
        ChunkMeshBuilder builder = new();

        builder.AddGradientRectangle(
            new RectangleF(0f, 0f, 32f, 32f),
            ColorRgba.Red,
            ColorRgba.Green,
            ColorRgba.Blue,
            ColorRgba.White,
            ColorRgba.Black);

        builder.VertexCount.Should().Be(5);
        builder.IndexCount.Should().Be(12);
    }

    [Fact]
    public void Clear_ShouldResetVertexCountToZero()
    {
        ChunkMeshBuilder builder = new();
        builder.AddTile(
            position: new Vector2(10f, 10f),
            size: new Vector2(64f, 64f),
            uvRect: new RectangleF(0f, 0f, 1f, 1f),
            color: ColorRgba.Red);

        builder.VertexCount.Should().Be(4);

        builder.Clear();

        builder.VertexCount.Should().Be(0);
    }
}
