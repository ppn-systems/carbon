// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Items.Races;

namespace Carbon.Desktop.Tests.Fakes;

/// <summary>Race catalogs for client tests (ADR-042), delivered the way the config sync delivers them.</summary>
internal static class TestRaces
{
    /// <summary>Gets the shipped <c>races.json</c> bytes (the build-time copy embedded in <c>Carbon.Items</c>).</summary>
    public static byte[] ShippedJson()
    {
        using Stream stream = typeof(RaceCatalog).Assembly.GetManifestResourceStream("Carbon.Items.Config.races.json")!;
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Wraps <paramref name="json"/> in a verified config catalog.</summary>
    public static ConfigCatalog Catalog(byte[] json) =>
        new(1, [new ConfigCatalogEntry(RaceCatalog.DocumentName, ConfigContentHash.Compute(json), 1, json)]);

    /// <summary>Gets a synced catalog holding <paramref name="json"/> (the shipped one by default).</summary>
    public static SyncedRaceCatalog Synced(byte[]? json = null)
    {
        SyncedRaceCatalog synced = new();
        _ = synced.Apply(Catalog(json ?? ShippedJson()));
        return synced;
    }
}
