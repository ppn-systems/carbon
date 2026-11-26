// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.World;
using Carbon.Maps.Building;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class ConstructionUxTests
{
    private static ConstructionMarker Site(int x, int y, ConstructionStage stage, float progress = 0f) =>
        new(x, y, 2, 1, StructureType.Workbench, StructureRotation.North, stage, progress, IsDeconstruction: false);

    [Fact]
    public void Apply_SitePushes_AddUpdateAndRemoveBySameAnchor()
    {
        ConstructionMarkerBook book = new();
        List<ConstructionMarker> markers = [];

        book.Apply(Site(3, 4, ConstructionStage.Blueprint));
        book.Apply(Site(3, 4, ConstructionStage.Frame, 0.5f));
        book.CopyTo(markers);
        markers.Should().ContainSingle().Which.Progress.Should().Be(0.5f);
        book.CoversSite(4, 4).Should().BeTrue("the 2x1 footprint covers (4,4)");
        book.CoversSite(5, 4).Should().BeFalse();

        book.Apply(Site(3, 4, ConstructionStage.Removed));
        book.CopyTo(markers);
        markers.Should().BeEmpty();
    }

    [Fact]
    public void Apply_Deconstruction_IsTrackedApartFromSites()
    {
        ConstructionMarkerBook book = new();
        List<ConstructionMarker> markers = [];
        ConstructionMarker deconstruct = new(3, 4, 1, 1, StructureType.WoodWall, StructureRotation.North, ConstructionStage.Complete, 0.4f, IsDeconstruction: true);

        book.Apply(Site(3, 4, ConstructionStage.Blueprint));
        book.Apply(deconstruct);
        book.DeconstructionCount.Should().Be(1);
        book.SiteCount.Should().Be(1);

        book.Apply(deconstruct with { Stage = ConstructionStage.Removed });
        book.CopyTo(markers);
        markers.Should().ContainSingle().Which.IsDeconstruction.Should().BeFalse("removing a deconstruction leaves the site");
    }

    [Fact]
    public void ReplaceChunkSites_ImportedDelta_ReplacesOnlyThatChunksSites()
    {
        ConstructionMarkerBook book = new();
        book.Apply(Site(3, 4, ConstructionStage.Blueprint));
        book.Apply(Site(40, 4, ConstructionStage.Blueprint));

        ChunkDelta delta = new(0, 0);
        ConstructionSite site = new(StructureType.WoodWall, StructureRotation.North, ownerId: 1, width: 1, height: 1, costEntries: 1);
        delta.SetSite(10, 2, site);
        book.ReplaceChunkSites(0, 0, delta.Sites, StructureCatalog.LoadEmbedded());

        List<ConstructionMarker> markers = [];
        book.CopyTo(markers);
        markers.Select(m => (m.AnchorX, m.AnchorY)).Should().BeEquivalentTo([(10, 2), (40, 4)]);
        markers.Single(m => m.AnchorX == 10).Type.Should().Be(StructureType.WoodWall);
    }

    [Fact]
    public void Describe_EveryBuildResultCode_HasADistinctLabel()
    {
        BuildResultCode[] codes = Enum.GetValues<BuildResultCode>();
        string[] labels = [.. codes.Select(BuildResultText.Describe)];

        labels.Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l));
        labels.Distinct().Count().Should().Be(codes.Length, "players must be able to tell the rules apart");
    }
}
