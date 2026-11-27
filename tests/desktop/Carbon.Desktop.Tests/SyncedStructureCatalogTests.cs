// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Networking.Session;
using Carbon.Maps.Building;
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

    private MultiplayerSession NewSession() => new(new DesktopOptions { ConfigCacheDirectory = _cacheDirectory });
}
