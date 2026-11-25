// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Carbon.Data.Abstractions;
using Carbon.Data.Registry;
using Carbon.Data.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Data.Tests;

public sealed partial class DefinitionRegistryTests
{
    [Fact]
    public void DefinitionRegistry_DuplicateIds_ThrowsInvalidOperationException()
    {
        SampleDef[] duplicates = [new SampleDef(1, "oak"), new SampleDef(1, "birch")];

        Action act = () => _ = new DefinitionRegistry<uint, SampleDef>(duplicates);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void JsonDefinitionLoader_WithSourceGeneratedContext_LoadsImmutableRegistry()
    {
        const string json = """
        [
          { "Id": 10, "Code": "iron_ore" },
          { "Id": 20, "Code": "copper_ore" }
        ]
        """;

        DefinitionRegistry<uint, SampleDef> registry = JsonDefinitionLoader.LoadFromJson<uint, SampleDef>(
            json,
            SampleJsonContext.Default.SampleDefArray);

        registry.Count.Should().Be(2);
        registry.Contains(10).Should().BeTrue();
        registry.TryGet(20, out SampleDef? copper).Should().BeTrue();
        copper!.Code.Should().Be("copper_ore");

        Action missing = () => _ = registry.GetRequired(999);
        missing.Should().Throw<KeyNotFoundException>();
    }

    public sealed record SampleDef(uint Id, string Code) : IDataDefinition<uint>;

    [JsonSerializable(typeof(SampleDef[]))]
    private sealed partial class SampleJsonContext : JsonSerializerContext;
}
