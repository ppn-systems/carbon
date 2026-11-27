// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Networking.Session;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Settlements;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Audit COUP-5: the client's build reach and footprints come from the server's synced <c>structures.json</c>.</summary>
public sealed class SyncedStructureCatalogTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "carbon-structures-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyConfigCatalog_ShippedStructuresJson_BuildReachAndFootprintsMatchTheShippedFile()
    {
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            session.ApplyConfigCatalog(Catalog(1, ShippedJson()));

            session.BuildReachTiles.Should().Be(4f, "structures.json placementReachTiles");
            session.PreviewPlacement(0, 0, StructureType.Workbench, StructureRotation.North, out int width, out int height);
            (width, height).Should().Be((2, 1));
            session.PreviewPlacement(0, 0, StructureType.Workbench, StructureRotation.East, out width, out height);
            (width, height).Should().Be((1, 2));
        }
    }

    [Fact]
    public async Task BuildReachTiles_BeforeAnySync_IsZeroAndNothingCanBePlaced()
    {
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            session.BuildReachTiles.Should().Be(0f, "the client has no structures.json of its own");
            session.PreviewPlacement(0, 0, StructureType.Workbench, StructureRotation.North, out _, out _)
                .Should().Be(BuildResultCode.UnknownStructure);
        }
    }

    [Fact]
    public async Task TryCreateWorldGenerator_OnlyAfterTheCatalogIsSynced_BuildsWithTheServersCatalog()
    {
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            session.TryCreateWorldGenerator(1337UL).Should().BeNull("the menu backdrop must not plan villages from a catalog the server did not send");

            session.ApplyConfigCatalog(Catalog(1, ServerTuned(reachTiles: 7, workbenchWidth: 3)));
            ProceduralWorldGenerator? generator = session.TryCreateWorldGenerator(1337UL);

            generator.Should().NotBeNull();
            generator!.Seed.Should().Be(1337UL);
            generator.Structures.Rules.PlacementReachTiles.Should().Be(7, "the generator plans with the synced structures.json");
        }
    }

    [Fact]
    public async Task TryCreateWorldGenerator_SameSeedTwice_BuildsASecondGenerator()
    {
        // Characterisation (ADR-045 D7): the menu backdrop and the world it enters each get their own generator.
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            session.ApplyConfigCatalog(Catalog(1, ServerTuned(reachTiles: 7, workbenchWidth: 3)));

            ProceduralWorldGenerator? backdrop = session.TryCreateWorldGenerator(1337UL);
            ProceduralWorldGenerator? world = session.TryCreateWorldGenerator(1337UL);

            world.Should().NotBeSameAs(backdrop);
        }
    }

    [Fact]
    public async Task ApplyConfigCatalog_ServerChangedStructuresJson_ClientUsesTheServersReachAndFootprint()
    {
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            session.ApplyConfigCatalog(Catalog(1, ServerTuned(reachTiles: 7, workbenchWidth: 3)));

            session.BuildReachTiles.Should().Be(7f);
            session.PreviewPlacement(0, 0, StructureType.Workbench, StructureRotation.North, out int width, out int height);
            (width, height).Should().Be((3, 1), "the footprint comes from the synced document, not a client copy");
        }
    }

    [Fact]
    public async Task ApplyConfigCatalog_HotReload_ReplacesTheCatalogAndKeepsItOnAnInvalidDocument()
    {
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            session.ApplyConfigCatalog(Catalog(1, ShippedJson()));
            session.ApplyConfigCatalog(Catalog(2, ServerTuned(reachTiles: 6, workbenchWidth: 2)));
            session.BuildReachTiles.Should().Be(6f, "a newer manifest replaces the catalog");

            session.ApplyConfigCatalog(Catalog(3, "{\"structures\": 5}"));
            session.ApplyConfigCatalog(new ConfigCatalog(4, []));
            session.BuildReachTiles.Should().Be(6f, "an invalid or missing document keeps the last valid one");
        }
    }

    [Fact]
    public void Apply_ShippedDocument_ParsesIt()
    {
        SyncedStructureCatalog synced = new();

        synced.Apply(Catalog(1, ShippedJson())).Should().BeTrue();

        synced.Current!.TryGet(StructureType.WoodBed, out StructureDefinition bed).Should().BeTrue();
        bed.GetFootprint(StructureRotation.North).Should().Be((1, 2));
    }

    [Fact]
    public async Task CreateWorldGenerator_SyncedCatalogDiffersFromTheBuiltIn_PlansVillagesLikeTheServer()
    {
        MultiplayerSession session = this.NewSession();
        await using (session.ConfigureAwait(true))
        {
            FluentActions.Invoking(() => session.CreateWorldGenerator(42UL)).Should().Throw<InvalidOperationException>("villages need the server's structures.json");

            string served = ServerTuned(reachTiles: 4, workbenchWidth: 3);
            session.ApplyConfigCatalog(Catalog(1, served));
            ProceduralWorldGenerator client = session.CreateWorldGenerator(42UL);

            // What the server builds with the same served file (AuthoritativeWorld passes its catalog to the generator).
            ProceduralWorldGenerator server = new(42UL, structures: StructureCatalog.Parse(served));
            VillagePlots(client).Should().Equal(VillagePlots(server), "client and server plan villages from the same served structures.json");
            VillagePlots(client).Should().NotEqual(VillagePlots(new ProceduralWorldGenerator(42UL)), "a 3x1 workbench changes the layout, so the synced catalog really reaches the planner");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    internal static string ShippedJson()
    {
        using Stream stream = typeof(StructureCatalog).Assembly.GetManifestResourceStream(StructureCatalog.EmbeddedResourceName)
            ?? throw new InvalidOperationException("structures.json is not embedded.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    internal static ConfigCatalog Catalog(uint version, string structuresJson)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(structuresJson);
        return new ConfigCatalog(version, [new ConfigCatalogEntry("structures", ConfigContentHash.Compute(utf8), version, utf8)]);
    }

    private static List<string> VillagePlots(ProceduralWorldGenerator generator)
    {
        List<string> plots = [];
        for (int sy = -4; sy <= 4; sy++)
        {
            for (int sx = -4; sx <= 4; sx++)
            {
                foreach (VillagePlot plot in generator.Landmarks.GetLayout(sx, sy).Plots)
                {
                    plots.Add($"{plot.Role}@{plot.OriginX},{plot.OriginY} {plot.Width}x{plot.Height} t{plot.Turns} {plot.Template.Id} {string.Join(';', plot.Template.Furniture)}");
                }
            }
        }

        return plots;
    }

    private static string ServerTuned(int reachTiles, int workbenchWidth)
    {
        JsonNode root = JsonNode.Parse(ShippedJson())!;
        root["placementReachTiles"] = reachTiles;
        foreach (JsonNode? structure in root["structures"]!.AsArray())
        {
            if ((string?)structure!["id"] == "workbench")
            {
                structure["size"] = new JsonArray(workbenchWidth, 1);
            }
        }

        return root.ToJsonString();
    }

    private MultiplayerSession NewSession() => new(new DesktopOptions { ConfigCacheDirectory = _cacheDirectory });
}
