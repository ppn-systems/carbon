// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Text.Json.Serialization;
using Carbon.Configuration.Catalog;
using Carbon.Data.Abstractions;
using Carbon.Data.Registry;
using FluentAssertions;
using Xunit;

namespace Carbon.Configuration.Tests;

public sealed class ConfigCatalogTests
{
    [Fact]
    public void LoadRegistry_ArrayDocument_IndexesDefinitionsThroughJsonDefinitionLoader()
    {
        byte[] json = ConfigTestServer.Utf8("[{\"Id\":1,\"Name\":\"stick\"},{\"Id\":2,\"Name\":\"stone\"}]");
        ConfigCatalog catalog = new(3, [new ConfigCatalogEntry("items", ConfigContentHash.Compute(json), 1, json)]);

        DefinitionRegistry<int, TestItem> registry = catalog.LoadRegistry<int, TestItem>("items", TestJsonContext.Default.TestItemArray);

        registry.Count.Should().Be(2);
        registry.GetRequired(2).Name.Should().Be("stone");
    }

    [Fact]
    public void GetRequired_UnknownDocument_Throws()
    {
        ConfigCatalog catalog = new(1, []);

        Action act = () => catalog.GetRequired("items");

        act.Should().Throw<System.Collections.Generic.KeyNotFoundException>();
    }

    [Fact]
    public void Constructor_DuplicateName_Throws()
    {
        ConfigCatalogEntry entry = new("items", 0, 1, []);

        Action act = () => _ = new ConfigCatalog(1, [entry, entry]);

        act.Should().Throw<InvalidOperationException>();
    }
}

/// <summary>Test definition.</summary>
public sealed class TestItem : IDataDefinition<int>
{
    /// <inheritdoc/>
    public int Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;
}

[JsonSerializable(typeof(TestItem[]))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
