// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Simulation.Spatial;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

public sealed class SpatialHashGrid2DTests
{
    [Fact]
    public void WorldToCell_ShouldMapCoordinatesToCorrectGridBuckets()
    {
        SpatialHashGrid2D grid = new(32f);

        CellCoord c00 = grid.WorldToCell(new Vector2(10f, 15f));
        c00.X.Should().Be(0);
        c00.Y.Should().Be(0);

        CellCoord cNeg = grid.WorldToCell(new Vector2(-5f, -33f));
        cNeg.X.Should().Be(-1);
        cNeg.Y.Should().Be(-2);

        CellCoord c32 = grid.WorldToCell(new Vector2(32f, 64f));
        c32.X.Should().Be(1);
        c32.Y.Should().Be(2);
    }

    [Fact]
    public void InsertAndUpdateAndRemove_ShouldManageEntityCellBuckets()
    {
        SpatialHashGrid2D grid = new(32f);
        grid.EntityCount.Should().Be(0);

        grid.Insert(1, new Vector2(10f, 10f));
        grid.EntityCount.Should().Be(1);
        grid.ActiveCellCount.Should().Be(1);

        bool migrated = grid.Update(1, new Vector2(20f, 20f));
        migrated.Should().BeFalse();

        migrated = grid.Update(1, new Vector2(40f, 10f));
        migrated.Should().BeTrue();

        bool removed = grid.Remove(1);
        removed.Should().BeTrue();
        grid.EntityCount.Should().Be(0);
        grid.ActiveCellCount.Should().Be(0);
    }

    [Fact]
    public void QueryNeighbors_ShouldRetrieveEntitiesIn3x3SurroundingCells()
    {
        SpatialHashGrid2D grid = new(32f);

        grid.Insert(1, new Vector2(0f, 0f));
        grid.Insert(2, new Vector2(35f, 0f));
        grid.Insert(3, new Vector2(-10f, -10f));
        grid.Insert(4, new Vector2(100f, 100f));

        Span<int> buffer = stackalloc int[16];
        grid.QueryNeighbors(new CellCoord(0, 0), buffer, out int count);

        count.Should().Be(3);
        buffer[..count].ToArray().Should().Contain([1, 2, 3]);
        buffer[..count].ToArray().Should().NotContain(4);
    }
}
