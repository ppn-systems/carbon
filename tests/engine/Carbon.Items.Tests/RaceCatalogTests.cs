// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using Carbon.Contracts.Identity;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Progression;
using Carbon.Items.Races;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary><c>races.json</c> (ADR-042): what the server accepts as a race catalog and as a character look.</summary>
public sealed class RaceCatalogTests
{
    private const string Body = """{"id":0,"code":"male","nameKey":"race.body.male","rig":"characters/base_hero.cvg.json"}""";
    private const string HairStyle = """{"code":"hair_style","nameKey":"appearance.hair","role":"hair","choices":[{"nameKey":"appearance.hair.crop","part":"hair_crop"},{"nameKey":"appearance.hair.long","part":"hair_long"}]}""";
    private const string Skin = """{"code":"skin_tone","nameKey":"appearance.skin","role":"skin","choices":[{"nameKey":"appearance.skin.fair","colors":["#F5C6A5","#D49B78"]}]}""";

    private static readonly RaceCatalog s_shipped = RaceCatalog.LoadShippedDefaults();

    [Fact]
    public void LoadShippedDefaults_ShippedFile_MatchesResourcesConfig()
    {
        RaceCatalog onDisk = RaceCatalog.LoadFromJson(FindRepoFile("resources/config/races.json"));

        onDisk.Races.ConvertAll(r => r.Id).Should().Equal(s_shipped.Races.ConvertAll(r => r.Id), "the embedded copy is the build-time races.json");
    }

    [Fact]
    public void LoadShippedDefaults_Human_HasMaleBodyFourOptionsAndNoStatChange()
    {
        RaceDefinition human = s_shipped.FindRace(1)!;

        human.Code.Should().Be("human");
        human.FindBody(0)!.Code.Should().Be("male");
        human.Options.Should().HaveCount(4);
        human.Options.ConvertAll(o => o.Code).Should().Equal("hair_style", "hair_color", "eye_color", "skin_tone");
        human.StatModifiers.IsNonNegative.Should().BeTrue();
        new[]
        {
            human.StatModifiers.MaxHealth, human.StatModifiers.MaxMana, human.StatModifiers.MaxStamina, human.StatModifiers.Defense,
            human.StatModifiers.Attack, human.StatModifiers.MoveSpeed, human.StatModifiers.CritChance, human.StatModifiers.Evasion,
        }.Should().OnlyContain(v => v == 0f, "humans ship with no balance change");
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"races":[]}""", "schemaVersion")]
    [InlineData("""{"schemaVersion":1,"races":[]}""", "at least one race")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":0,"code":"human","nameKey":"race.human","bodies":[BODY]}]}""", "0 means no race")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY]},{"id":1,"code":"elf","nameKey":"race.elf","bodies":[BODY]}]}""", "repeats race id")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"Human","nameKey":"race.human","bodies":[BODY]}]}""", ".code")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"Race Human","bodies":[BODY]}]}""", "localization key")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[]}]}""", "1..8 body types")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY,BODY]}]}""", "repeats body id")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[{"id":0,"code":"male","nameKey":"race.body.male","rig":"../secret.cvg.json"}]}]}""", ".rig")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[{"id":0,"code":"male","nameKey":"race.body.male","rig":"characters/hero.png"}]}]}""", ".rig")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[HAIR,HAIR]}]}""", "repeats option code")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[{"code":"skin_tone","nameKey":"appearance.skin","role":"skin","choices":[]}]}]}""", "1..256 choices")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[{"code":"skin_tone","nameKey":"appearance.skin","role":"skin","choices":[{"nameKey":"appearance.skin.fair","colors":["F5C6A5"]}]}]}]}""", "#RRGGBB")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[{"code":"hair_style","nameKey":"appearance.hair","role":"hair","choices":[{"nameKey":"appearance.hair.crop","part":"hair_crop"},{"nameKey":"appearance.hair.long","colors":["#000000"]}]}]}]}""", "like the option's first choice")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"statModifiers":{"maxHealth":1e9}}]}""", "statModifiers")]
    [InlineData("""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"wings":true}]}""", "malformed")]
    public void LoadFromJson_InvalidCatalog_IsRejectedWithItsPath(string template, string expectedFragment)
    {
        ArgumentNullException.ThrowIfNull(template);
        string json = template.Replace("BODY", Body, StringComparison.Ordinal).Replace("HAIR", HairStyle, StringComparison.Ordinal);

        Action load = () => RaceCatalog.LoadFromJson(json);

        load.Should().Throw<InvalidDataException>().Which.Message.Should().Contain(expectedFragment);
    }

    [Fact]
    public void LoadFromJson_SeventeenOptions_IsRejected()
    {
        string options = string.Join(',', Enumerable.Range(0, CharacterLook.MaxOptions + 1)
            .Select(i => HairStyle.Replace("\"hair_style\"", $"\"opt{i}\"", StringComparison.Ordinal)));
        string json = $$"""{"schemaVersion":1,"races":[{"id":1,"code":"human","nameKey":"race.human","bodies":[{{Body}}],"options":[{{options}}]}]}""";

        Action load = () => RaceCatalog.LoadFromJson(json);

        load.Should().Throw<InvalidDataException>().Which.Message.Should().Contain("at most 16 options");
    }

    [Fact]
    public void Check_ShippedDefaultLook_IsValid()
    {
        s_shipped.Check(s_shipped.DefaultLook()).Should().Be(CharacterLookStatus.Valid);
        s_shipped.Check(new CharacterLook(1, 0, [3, 3, 3, 3])).Should().Be(CharacterLookStatus.Valid, "the last choice of every human option is allowed");
    }

    [Theory]
    [InlineData((ushort)0, (byte)0, new byte[] { 0, 0, 0, 0 }, CharacterLookStatus.UnknownRace)]
    [InlineData((ushort)2, (byte)0, new byte[] { 0, 0, 0, 0 }, CharacterLookStatus.UnknownRace)]
    [InlineData((ushort)1, (byte)1, new byte[] { 0, 0, 0, 0 }, CharacterLookStatus.UnknownBodyType)]
    [InlineData((ushort)1, (byte)0, new byte[] { 0, 0, 0 }, CharacterLookStatus.OptionCountMismatch)]
    [InlineData((ushort)1, (byte)0, new byte[] { 0, 0, 0, 0, 0 }, CharacterLookStatus.OptionCountMismatch)]
    [InlineData((ushort)1, (byte)0, new byte[] { 4, 0, 0, 0 }, CharacterLookStatus.OptionOutOfRange)]
    [InlineData((ushort)1, (byte)0, new byte[] { 0, 0, 0, 255 }, CharacterLookStatus.OptionOutOfRange)]
    public void Check_LookNamingSomethingTheCatalogLacks_ReportsWhat(ushort raceId, byte bodyType, byte[] options, CharacterLookStatus expected) =>
        s_shipped.Check(new CharacterLook(raceId, bodyType, options)).Should().Be(expected);

    [Fact]
    public void RequireCompatibleWith_AppendedRaceBodyOptionAndChoice_IsAccepted()
    {
        RaceCatalog previous = Catalog($$"""{"id":1,"code":"human","nameKey":"race.human","bodies":[{{Body}}],"options":[{{HairStyle}}]}""");
        RaceCatalog next = Catalog(
            $$"""{"id":1,"code":"human","nameKey":"race.human","bodies":[{{Body}},{"id":1,"code":"female","nameKey":"race.body.female","rig":"characters/human_female.cvg.json"}],"options":[{{HairStyle.Replace("]}", ",{\"nameKey\":\"appearance.hair.bob\",\"part\":\"hair_bob\"}]}", StringComparison.Ordinal)}},{{Skin}}]}""",
            $$"""{"id":2,"code":"elf","nameKey":"race.elf","bodies":[{{Body}}]}""");

        Action reload = () => next.RequireCompatibleWith(previous);

        reload.Should().NotThrow("adding races, bodies, trailing options and trailing choices keeps every stored look valid");
    }

    [Theory]
    [InlineData("""{"id":2,"code":"elf","nameKey":"race.elf","bodies":[BODY],"options":[HAIR]}""", "removes race 1")]
    [InlineData("""{"id":1,"code":"human","nameKey":"race.human","bodies":[{"id":3,"code":"male","nameKey":"race.body.male","rig":"characters/base_hero.cvg.json"}],"options":[HAIR]}""", "removes body type 0")]
    [InlineData("""{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[]}""", "removes appearance options")]
    [InlineData("""{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[SKIN]}""", "moves or shrinks option 'hair_style'")]
    [InlineData("""{"id":1,"code":"human","nameKey":"race.human","bodies":[BODY],"options":[{"code":"hair_style","nameKey":"appearance.hair","role":"hair","choices":[{"nameKey":"appearance.hair.crop","part":"hair_crop"}]}]}""", "moves or shrinks")]
    public void RequireCompatibleWith_RemovingWhatStoredLooksUse_IsRejected(string nextRace, string expectedFragment)
    {
        RaceCatalog previous = Catalog($$"""{"id":1,"code":"human","nameKey":"race.human","bodies":[{{Body}}],"options":[{{HairStyle}}]}""");
        ArgumentNullException.ThrowIfNull(nextRace);
        RaceCatalog next = Catalog(nextRace
            .Replace("BODY", Body, StringComparison.Ordinal)
            .Replace("HAIR", HairStyle, StringComparison.Ordinal)
            .Replace("SKIN", Skin, StringComparison.Ordinal));

        Action reload = () => next.RequireCompatibleWith(previous);

        reload.Should().Throw<InvalidDataException>().Which.Message.Should().Contain(expectedFragment);
    }

    [Fact]
    public void Codec_Look_RoundTripsThroughStoredBytes()
    {
        CharacterLook look = new(0xBEEF, 7, [1, 0, 255, 3, 2]);

        byte[] stored = CharacterLookBinaryCodec.Serialize(look);

        stored.Should().Equal([1, 0xEF, 0xBE, 7, 5, 1, 0, 255, 3, 2], "v1: version, race LE, body, count, options");
        CharacterLookBinaryCodec.Deserialize(stored).Should().Be(look);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 2, 1, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 1, 0, 0, 2, 0 })]
    [InlineData(new byte[] { 1, 1, 0, 0, 17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Codec_CorruptBytes_AreRejected(byte[] stored)
    {
        Action read = () => CharacterLookBinaryCodec.Deserialize(stored);

        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ComputeStats_RaceModifiers_AddToTheDerivedStats()
    {
        ProgressionConfig progression = ProgressionConfig.LoadShippedDefaults();
        StatBlock sturdy = new() { MaxHealth = 25f, Defense = -1f };
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        ItemRules rules = ItemRules.LoadShippedDefaults();

        DerivedCharacterStats plain = CharacterStatCalculator.Compute(rules, progression, 1, bag, 0, null);
        DerivedCharacterStats raced = CharacterStatCalculator.Compute(rules, progression, 1, bag, 0, null, sturdy);
        DerivedCharacterStats human = CharacterStatCalculator.Compute(rules, progression, 1, bag, 0, null, s_shipped.FindRace(1)!.StatModifiers);

        raced.MaxHealth.Should().Be(plain.MaxHealth + 25f);
        raced.Defense.Should().Be(plain.Defense - 1f, "a race may also lower a stat");
        raced.MaxStamina.Should().Be(plain.MaxStamina, "only the stats the race names change");
        human.Should().Be(plain, "the shipped human changes no stat");
    }

    private static RaceCatalog Catalog(params string[] races) =>
        RaceCatalog.LoadFromJson($$"""{"schemaVersion":1,"races":[{{string.Join(',', races)}}]}""");

    private static string FindRepoFile(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative)))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
