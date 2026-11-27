// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Reflection;
using Carbon.Contracts.Identity.C2S;
using Nalix.Abstractions.Serialization;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>Character creation carries a name and a look only: characters start classless (ADR-030).</summary>
public sealed class CharacterCreateContractTests
{
    [Fact]
    public void CharacterCreate_WireLayout_HasNameAndAppearanceButNoClass()
    {
        string[] wireFields = [.. typeof(CharacterCreate)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<SerializeOrderAttribute>() is not null)
            .OrderBy(p => p.GetCustomAttribute<SerializeOrderAttribute>()!.Order)
            .Select(p => p.Name)];

        Assert.Equal(["CharacterName", "AppearancePreset"], wireFields);
    }

    [Fact]
    public void Deserialize_ProtocolOnePayloadWithTrailingClassByte_IsRejectedNotMisread()
    {
        using CharacterCreate current = new() { CharacterName = "Ashborn", AppearancePreset = 9 };
        byte[] protocolOne = [.. current.Serialize(), 2];

        CharacterCreate? read = CharacterCreate.Deserialize(protocolOne);

        Assert.Null(read);
    }

    [Fact]
    public void Deserialize_ProtocolTwoPayload_RoundTripsNameAndLook()
    {
        using CharacterCreate current = new() { CharacterName = "Ashborn", AppearancePreset = 9 };

        using CharacterCreate read = CharacterCreate.Deserialize(current.Serialize());

        Assert.Equal("Ashborn", read.CharacterName);
        Assert.Equal((byte)9, read.AppearancePreset);
    }
}
