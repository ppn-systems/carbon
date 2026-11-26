// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using Carbon.Contracts.Config;
using Carbon.Contracts.Config.C2S;
using Carbon.Contracts.Config.S2C;
using Xunit;

namespace Carbon.Contracts.Tests;

public sealed class ConfigContractTests
{
    [Fact]
    public void ConfigManifest_RoundTrip_PreservesParallelArrays()
    {
        using ConfigManifest manifest = new()
        {
            ManifestVersion = 12,
            Names = ["items", "monsters"],
            ContentHashes = [0xCBF29CE484222325UL, ulong.MaxValue],
            ByteLengths = [42, 4 * 1024 * 1024],
            Versions = [1, 7],
        };

        using ConfigManifest copy = ConfigManifest.Deserialize(manifest.Serialize());

        Assert.Equal(12u, copy.ManifestVersion);
        Assert.Equal(manifest.Names, copy.Names);
        Assert.Equal(manifest.ContentHashes, copy.ContentHashes);
        Assert.Equal(manifest.ByteLengths, copy.ByteLengths);
        Assert.Equal(manifest.Versions, copy.Versions);
    }

    [Fact]
    public void ConfigRequest_RoundTrip_PreservesNames()
    {
        using ConfigRequest request = new() { ManifestVersion = 3, Names = ["loot", "recipes"] };

        using ConfigRequest copy = ConfigRequest.Deserialize(request.Serialize());

        Assert.Equal(3u, copy.ManifestVersion);
        Assert.Equal(["loot", "recipes"], copy.Names);
    }

    [Fact]
    public void ConfigDocument_FullSizeChunk_RoundTripsPayloadAndMetadata()
    {
        byte[] payload = [.. Enumerable.Range(0, ConfigProtocolLimits.ChunkBytes).Select(static i => (byte)(i * 31))];
        using ConfigDocument chunk = new()
        {
            Name = "structures",
            ContentHash = 0x0123456789ABCDEFUL,
            Version = 5,
            TotalLength = 3 * ConfigProtocolLimits.ChunkBytes,
            Offset = ConfigProtocolLimits.ChunkBytes,
            Payload = payload,
        };

        using ConfigDocument copy = ConfigDocument.Deserialize(chunk.Serialize());

        Assert.False(copy.IsTerminator);
        Assert.Equal("structures", copy.Name);
        Assert.Equal(0x0123456789ABCDEFUL, copy.ContentHash);
        Assert.Equal(5u, copy.Version);
        Assert.Equal(3 * ConfigProtocolLimits.ChunkBytes, copy.TotalLength);
        Assert.Equal(ConfigProtocolLimits.ChunkBytes, copy.Offset);
        Assert.Equal(payload, copy.Payload);

        using ConfigDocument terminator = new() { IsEndOfStream = true, IsTerminator = true };
        using ConfigDocument terminatorCopy = ConfigDocument.Deserialize(terminator.Serialize());
        Assert.True(terminatorCopy.IsEndOfStream);
        Assert.True(terminatorCopy.IsTerminator);
    }

    [Theory]
    [InlineData("items", true)]
    [InlineData("loot_tables-v2", true)]
    [InlineData("9lives", true)]
    [InlineData("", false)]
    [InlineData("Items", false)]
    [InlineData("_items", false)]
    [InlineData("../items", false)]
    [InlineData("items.json", false)]
    [InlineData(null, false)]
    public void ConfigDocumentName_IsValid_EnforcesRule(string? name, bool expected) =>
        Assert.Equal(expected, ConfigDocumentName.IsValid(name));

    [Fact]
    public void ConfigDocumentName_IsValid_RejectsOverlongName() =>
        Assert.False(ConfigDocumentName.IsValid(new string('a', ConfigProtocolLimits.MaxNameLength + 1)));
}
