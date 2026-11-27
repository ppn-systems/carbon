// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Text;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Races;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The client turns a look into a palette and a rig from the synced <c>races.json</c> alone (ADR-042).</summary>
public sealed class CharacterLookResolverTests
{
    private const string TestRaceJson = """
        {"schemaVersion":1,"races":[
          {"id":4242,"code":"test_kin","nameKey":"race.test_kin",
           "bodies":[{"id":5,"code":"slim","nameKey":"race.body.slim","rig":"characters/test_kin_slim.cvg.json"}],
           "options":[{"code":"crest","nameKey":"appearance.crest","role":"hair","choices":[
                        {"nameKey":"appearance.crest.none","part":"crest_none"},{"nameKey":"appearance.crest.fan","part":"crest_fan"}]},
                      {"code":"scales","nameKey":"appearance.scales","role":"skin","choices":[
                        {"nameKey":"appearance.scales.jade","colors":["#2F855A","#22543D"]},{"nameKey":"appearance.scales.ember","colors":["#C05621"]}]}]}]}
        """;

    [Fact]
    public void ResolvePalette_LegacyStylePreset_MatchesThePaletteTheClientUsedToHardcode()
    {
        RaceDefinition human = RaceCatalog.LoadShippedDefaults().FindRace(1)!;

        // Old preset (hair 2, colour 3, eyes 1, skin 2) = options [2, 3, 1, 2].
        CharacterPalette p = CharacterLookResolver.ResolvePalette(human, new CharacterLook(1, 0, [2, 3, 1, 2]));

        p.Skin.Should().Be(new ColorRgba(198, 134, 92, 255), "Olive Bronze");
        p.SkinShadow.Should().Be(new ColorRgba(158, 98, 62, 255));
        p.Hair.Should().Be(new ColorRgba(203, 213, 225, 255), "Silver Ash");
        p.HairHighlight.Should().Be(new ColorRgba(241, 245, 249, 255));
        p.Iris.Should().Be(new ColorRgba(220, 38, 38, 255), "Fierce Crimson");
        p.HairPart.Should().Be(2);
        p.EyeStyle.Should().Be(1);
    }

    [Fact]
    public void ResolvePalette_ConfigOnlyRace_ReadsColoursAndPartsByRole()
    {
        RaceDefinition kin = RaceCatalog.LoadFromJson(TestRaceJson).FindRace(4242)!;

        CharacterPalette p = CharacterLookResolver.ResolvePalette(kin, new CharacterLook(4242, 5, [1, 1]));

        p.HairPart.Should().Be(1, "the 'hair' part option picks the crest");
        p.Skin.Should().Be(new ColorRgba(0xC0, 0x56, 0x21, 255), "the 'skin' palette option recolours skin shapes");
        p.SkinShadow.Should().Be(p.Skin, "a one-colour choice shades with its base");
        p.Hair.Should().Be(CharacterPalette.Default.Hair, "roles the race does not define keep the default");
    }

    [Fact]
    public void SyncedRaceCatalog_InvalidDocumentAfterAValidOne_KeepsTheValidCatalog()
    {
        SyncedRaceCatalog synced = TestRaces.Synced();
        RaceCatalog? good = synced.Current;

        synced.Apply(TestRaces.Catalog(Encoding.UTF8.GetBytes("""{"schemaVersion":1,"races":[]}"""))).Should().BeFalse();

        good.Should().NotBeNull();
        synced.Current.Should().BeSameAs(good);
    }

    [Fact]
    public void HeroAppearance_ConfigOnlyRace_IsOfferedAndProducesAValidLook()
    {
        RaceCatalog races = RaceCatalog.LoadFromJson(TestRaceJson);
        HeroAppearance appearance = new();
        appearance.Bind(races);

        appearance.CycleOption(0, 1);
        appearance.CycleOption(1, -1);

        appearance.Race!.Code.Should().Be("test_kin");
        appearance.Body!.Code.Should().Be("slim");
        appearance.Look.Should().Be(new CharacterLook(4242, 5, [1, 1]));
        races.Check(appearance.Look).Should().Be(CharacterLookStatus.Valid);
    }
}
