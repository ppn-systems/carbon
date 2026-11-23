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
