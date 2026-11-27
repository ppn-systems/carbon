// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Carbon.Contracts.Identity.C2S;
using Nalix.Abstractions.Serialization;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>
/// Character creation carries a name and a structured look only: characters start classless (ADR-030), and the look is a
/// race id, a body type and option choices (ADR-042).
/// </summary>
public sealed class CharacterCreateContractTests
{
    [Fact]
    public void CharacterCreate_WireLayout_HasNameAndStructuredLookButNoClassOrPackedPreset()
    {
        (string Name, int Order)[] wireFields = [.. typeof(CharacterCreate)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<SerializeOrderAttribute>() is not null)
            .Select(p => (p.Name, p.GetCustomAttribute<SerializeOrderAttribute>()!.Order))
            .OrderBy(f => f.Order)];

        // Orders 1 (packed preset, protocol 8) and 2 (starting class, protocol 2) stay retired gaps (ADR-038 D4).
        Assert.Equal([("CharacterName", 0), ("RaceId", 3), ("BodyType", 4), ("AppearanceOptions", 5)], wireFields);
    }

    [Fact]
    public void Deserialize_ProtocolSevenPayloadWithPackedPresetByte_IsRejectedNotMisread()
    {
        using CharacterCreate nameOnly = new() { CharacterName = "Ashborn" };
        byte[] full = nameOnly.Serialize();
        int nameEnd = IndexOfName(full, "Ashborn") + "Ashborn".Length;
        byte[] protocolSeven = [.. full[..nameEnd], 9];

        CharacterCreate? read = CharacterCreate.Deserialize(protocolSeven);

        Assert.Null(read);
    }

    [Fact]
    public void Deserialize_ProtocolEightPayload_RoundTripsNameAndLook()
    {
        using CharacterCreate current = new()
        {
            CharacterName = "Ashborn",
            RaceId = 513,
            BodyType = 1,
            AppearanceOptions = [3, 0, 2, 1],
        };

        using CharacterCreate read = CharacterCreate.Deserialize(current.Serialize());

        Assert.Equal("Ashborn", read.CharacterName);
        Assert.Equal((ushort)513, read.RaceId);
        Assert.Equal((byte)1, read.BodyType);
        Assert.Equal([3, 0, 2, 1], read.AppearanceOptions);
    }

    private static int IndexOfName(byte[] payload, string name)
    {
        byte[] needle = Encoding.UTF8.GetBytes(name);
        for (int i = 0; i + needle.Length <= payload.Length; i++)
        {
            if (payload.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
