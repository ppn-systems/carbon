// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration;
using Carbon.Contracts.Config;
using Carbon.Contracts.Config.C2S;
using Carbon.Contracts.Config.S2C;
using FluentAssertions;
using Xunit;

namespace Carbon.Server.Configuration.Tests;

public sealed class ConfigRepositoryTests : IDisposable
{
    private readonly TempConfigDirectory _dir = new();

    [Fact]
    public void Load_Directory_BuildsManifestOrderedByNameFromCanonicalBytes()
    {
        _dir.Write("monsters.json", "[ { \"id\": 7 } ]");
        _dir.Write("items.json", "[\n  {\"id\": 1, \"name\": \"stick\"}\n]\n");
        _dir.Write("README.md", "not config");

        ConfigRepository repository = ConfigRepository.Load(_dir.Path);
        using ConfigManifest manifest = repository.CreateManifest();

        manifest.ManifestVersion.Should().Be(1u);
        manifest.Names.Should().Equal("items", "monsters");
        manifest.Versions.Should().Equal(1u, 1u);
        string canonical = Encoding.UTF8.GetString(repository.Catalog.GetUtf8("items"));
        canonical.Should().Be("[{\"id\":1,\"name\":\"stick\"}]");
        manifest.ContentHashes[0].Should().Be(ConfigContentHash.Compute(Encoding.UTF8.GetBytes(canonical)));
        manifest.ByteLengths[0].Should().Be(canonical.Length);
    }

    [Fact]
    public void Reload_WhitespaceOnlyEdit_KeepsHashAndManifestVersion()
    {
        _dir.Write("items.json", "[{\"id\":1}]");
        ConfigRepository repository = ConfigRepository.Load(_dir.Path);
        ulong before = repository.Entries[0].ContentHash;

        _dir.Write("items.json", "[\n    { \"id\" : 1 }\n]");
        ConfigReloadResult result = repository.Reload();

        result.HasChanges.Should().BeFalse();
        repository.ManifestVersion.Should().Be(1u);
        repository.Entries[0].ContentHash.Should().Be(before);
    }

    [Fact]
    public void Reload_ChangedDocument_BumpsVersionsChangesHashAndRaisesManifestChanged()
    {
        _dir.Write("items.json", "[{\"id\":1}]");
        _dir.Write("monsters.json", "[{\"id\":7}]");
        _dir.Write("loot.json", "[]");
        ConfigRepository repository = ConfigRepository.Load(_dir.Path);
        ConfigManifestEntry oldItems = repository.Entries.Single(static e => e.Name == "items");
        ConfigReloadResult? raised = null;
        repository.ManifestChanged += r => raised = r;

        _dir.Write("items.json", "[{\"id\":1},{\"id\":2}]");
        _dir.Delete("loot.json");
        _dir.Write("needs.json", "{\"hunger\":1}");
        ConfigReloadResult result = repository.Reload();

        result.HasChanges.Should().BeTrue();
        result.Changed.Should().BeEquivalentTo("items", "needs");
        result.Removed.Should().Equal("loot");
        raised.Should().BeSameAs(result);
        repository.ManifestVersion.Should().Be(2u);
        ConfigManifestEntry newItems = repository.Entries.Single(static e => e.Name == "items");
        newItems.ContentHash.Should().NotBe(oldItems.ContentHash);
        newItems.Version.Should().Be(2u);
        repository.Entries.Single(static e => e.Name == "monsters").Version.Should().Be(1u);
        repository.Entries.Single(static e => e.Name == "needs").Version.Should().Be(1u);
    }

    [Fact]
    public void Reload_InvalidJson_ThrowsAndKeepsPreviousSnapshot()
    {
        _dir.Write("items.json", "[{\"id\":1}]");
        ConfigRepository repository = ConfigRepository.Load(_dir.Path);
        ulong before = repository.Entries[0].ContentHash;

        _dir.Write("items.json", "[{\"id\":1},]");
        Action act = () => repository.Reload();

        act.Should().Throw<InvalidDataException>().WithMessage("*items.json*");
        repository.ManifestVersion.Should().Be(1u);
        repository.Entries[0].ContentHash.Should().Be(before);
    }

    [Theory]
    [InlineData("Items.json", "[]")]
    [InlineData("items.json", "{\"a\":1,\"a\":2}")]
    [InlineData("items.json", "// comment\n[]")]
    [InlineData("items.json", "")]
    public void Load_InvalidFile_FailsFast(string fileName, string content)
    {
        _dir.Write(fileName, content);

        Action act = () => ConfigRepository.Load(_dir.Path);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task HandleRequestAsync_MalformedRequests_AreRejectedWithoutSending()
    {
        _dir.Write("items.json", "[]");
        ConfigRepository repository = ConfigRepository.Load(_dir.Path);
        List<ConfigDocument> sent = [];

        (await Handle(repository, 1, sent, "nope")).Should().Be(ConfigRequestStatus.UnknownName);
        (await Handle(repository, 1, sent, "../items")).Should().Be(ConfigRequestStatus.InvalidName);
        (await Handle(repository, 1, sent, "items", "items")).Should().Be(ConfigRequestStatus.DuplicateName);
        (await Handle(repository, 9, sent, "items")).Should().Be(ConfigRequestStatus.StaleManifest);
        string[] tooMany = [.. Enumerable.Range(0, ConfigProtocolLimits.MaxDocuments + 1).Select(static i => "items")];
        (await Handle(repository, 1, sent, tooMany)).Should().Be(ConfigRequestStatus.TooManyNames);
        using ConfigRequest nullNames = new() { ManifestVersion = 1, Names = null! };
        (await repository.HandleRequestAsync(nullNames, Collect(sent))).Should().Be(ConfigRequestStatus.InvalidName);

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ServerOnlyDocuments_AreLoadedForTheServerButNeverSentToClients()
    {
        _dir.Write("items.json", "[]");
        _dir.Write("loot.json", "{\"dropRate\":0.05}");
        ConfigRepository repository = ConfigRepository.Load(_dir.Path, serverOnlyDocuments: ["loot"]);
        List<ConfigDocument> sent = [];

        using ConfigManifest manifest = repository.CreateManifest();
        manifest.Names.Should().Equal("items");
        manifest.ContentHashes.Should().HaveCount(1);
        repository.Catalog.Contains("loot").Should().BeTrue("the server itself still uses it");
        (await Handle(repository, 1, sent, "loot")).Should().Be(ConfigRequestStatus.UnknownName);
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleRequestAsync_LargeDocument_IsChunkedAndEndsWithTerminator()
    {
        string big = "[" + string.Join(',', Enumerable.Range(0, 9000).Select(static i => $"{{\"id\":{i}}}")) + "]";
        _dir.Write("items.json", big);
        ConfigRepository repository = ConfigRepository.Load(_dir.Path);
        List<ConfigDocument> sent = [];

        (await Handle(repository, 1, sent, "items")).Should().Be(ConfigRequestStatus.Ok);

        int length = repository.Entries[0].ByteLength;
        length.Should().BeGreaterThan(ConfigProtocolLimits.ChunkBytes * 2);
        sent.Should().HaveCount(((length + ConfigProtocolLimits.ChunkBytes - 1) / ConfigProtocolLimits.ChunkBytes) + 1);
        sent[^1].IsTerminator.Should().BeTrue();
        sent[^1].IsEndOfStream.Should().BeTrue();
        byte[] joined = [.. sent.Take(sent.Count - 1).SelectMany(static c => c.Payload)];
        joined.Should().Equal(repository.Catalog.GetUtf8("items").ToArray());
    }

    public void Dispose() => _dir.Dispose();

    private static ValueTask<ConfigRequestStatus> Handle(ConfigRepository repository, uint version, List<ConfigDocument> sent, params string[] names)
    {
        ConfigRequest request = new() { ManifestVersion = version, Names = names };
        return repository.HandleRequestAsync(request, Collect(sent));
    }

    /// <summary>Copies each packet through the wire format, since the repository returns pooled packets after send.</summary>
    internal static Func<ConfigDocument, CancellationToken, ValueTask> Collect(List<ConfigDocument> sent) => (packet, _) =>
    {
        sent.Add(ConfigDocument.Deserialize(packet.Serialize()));
        return ValueTask.CompletedTask;
    };
}
