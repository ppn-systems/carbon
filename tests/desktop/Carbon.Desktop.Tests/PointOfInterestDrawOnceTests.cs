// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Desktop.Rendering.World;
using Carbon.Maps.Landmarks;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// A point of interest is listed by every chunk within its radius, so the world renderer meets a village several times
/// per frame. It must draw the village's pieces and collect its lights once.
/// </summary>
public sealed class PointOfInterestDrawOnceTests
{
    [Fact]
    public void TryMarkDrawn_SameVillageFromSeveralChunks_DrawsItOnce()
    {
        List<(int TileX, int TileY)> drawn = new();
        PointOfInterest village = new(PointOfInterestType.RiverVillage, 120, -40, 18);

        bool fromFirstChunk = WorldSceneRenderer.TryMarkDrawn(drawn, in village);
        bool fromSecondChunk = WorldSceneRenderer.TryMarkDrawn(drawn, in village);
        bool fromThirdChunk = WorldSceneRenderer.TryMarkDrawn(drawn, in village);

        fromFirstChunk.Should().BeTrue("the first chunk that lists the village draws it");
        fromSecondChunk.Should().BeFalse("a second chunk listing the same village must not draw its pieces or lights again");
        fromThirdChunk.Should().BeFalse();
        drawn.Should().ContainSingle();
    }

    [Fact]
    public void TryMarkDrawn_TwoDifferentPointsOfInterest_DrawsBoth()
    {
        List<(int TileX, int TileY)> drawn = new();
        PointOfInterest village = new(PointOfInterestType.RiverVillage, 120, -40, 18);
        PointOfInterest shrine = new(PointOfInterestType.MountainShrine, 300, 64, 6);

        WorldSceneRenderer.TryMarkDrawn(drawn, in village).Should().BeTrue();
        WorldSceneRenderer.TryMarkDrawn(drawn, in shrine).Should().BeTrue("a different point of interest is drawn too");
    }

    [Fact]
    public void TryMarkDrawn_AfterTheFrameClearsTheList_DrawsAgain()
    {
        List<(int TileX, int TileY)> drawn = new();
        PointOfInterest village = new(PointOfInterestType.RiverVillage, 120, -40, 18);
        _ = WorldSceneRenderer.TryMarkDrawn(drawn, in village);

        drawn.Clear();

        WorldSceneRenderer.TryMarkDrawn(drawn, in village).Should().BeTrue("every frame draws the village once");
    }
}
