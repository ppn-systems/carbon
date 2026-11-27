// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Items.Races;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Every body rig of a race shares one skeleton (ADR-042 D11): the same bones, every clip of the reference body, and no
/// per-clip copies, so clip rework on one body (walk, attacks, swim, dive, downed, tool use) reaches every body.
/// </summary>
public sealed class BodyRigParityTests
{
    private static readonly Lazy<(CarbonVectorArtLibrary Library, string ArtRoot)> s_art = new(() =>
    {
        string root = VectorArtValidator.TryFindArtRootDirectory() ?? throw new InvalidOperationException("art root not found");
        return (CarbonVectorArtLibrary.LoadFromDirectory(root), root);
    });

    public static TheoryData<string, string> BodyPairs()
    {
        TheoryData<string, string> pairs = [];
        foreach (RaceDefinition race in RaceCatalog.LoadShippedDefaults().Races)
        {
            for (int i = 1; i < race.Bodies.Count; i++)
            {
                pairs.Add(race.Bodies[0].Rig, race.Bodies[i].Rig);
            }
        }

        return pairs;
    }

    [Fact]
    public void ShippedRaces_Human_HasAFemaleBodyOnItsOwnRig()
    {
        RaceDefinition human = RaceCatalog.LoadShippedDefaults().FindRace(1)!;

        human.Bodies.Select(b => b.Code).Should().Equal("male", "female");
        human.FindBody(1)!.Rig.Should().NotBe(human.FindBody(0)!.Rig, "each body draws its own shapes");
    }

    [Theory]
    [MemberData(nameof(BodyPairs))]
    public void EveryClipOfTheFirstBody_ResolvesOnEveryOtherBody(string referenceRig, string otherRig)
    {
        CarbonVectorArtLibrary lib = s_art.Value.Library;
        lib.TryGetCharacterRig(referenceRig, out CompiledVectorRig? reference).Should().BeTrue();
        lib.TryGetCharacterRig(otherRig, out CompiledVectorRig? other).Should().BeTrue();

        string[] referenceClips = [.. reference!.ClipNames.Order(StringComparer.Ordinal)];
        referenceClips.Should().Contain(["idle", "walk", "slash", "chop", "mine", "cast", "swim", "dive", "downed"]);
        other!.ClipNames.Order(StringComparer.Ordinal).Should().Equal(referenceClips, $"{otherRig} must play every clip of {referenceRig}");

        Span<BonePose> a = stackalloc BonePose[(int)VectorBoneId.Count];
        Span<BonePose> b = stackalloc BonePose[(int)VectorBoneId.Count];
        foreach (string clip in referenceClips)
        {
            foreach (float t in (float[])[0f, 0.33f, 0.66f, 1f])
            {
                reference.EvaluatePose(clip, "idle", t, isNormalized: true, a);
                other.EvaluatePose(clip, "idle", t, isNormalized: true, b);
                for (int bone = 0; bone < a.Length; bone++)
                {
                    b[bone].Should().Be(a[bone], $"clip '{clip}' at {t} moves bone {(VectorBoneId)bone} the same on both bodies");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BodyPairs))]
    public void OtherBodies_ShareTheSkeletonAndBorrowClipsInsteadOfCopyingThem(string referenceRig, string otherRig)
    {
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(s_art.Value.ArtRoot, referenceRig)));
        using JsonDocument other = JsonDocument.Parse(File.ReadAllText(Path.Combine(s_art.Value.ArtRoot, otherRig)));

        Bones(other).Should().Equal(Bones(reference), "bone names are the attachment points of clips, gear and held items");
        other.RootElement.TryGetProperty("animations", out _).Should().BeFalse("clips are shared, never duplicated per body");
        string borrowed = other.RootElement.GetProperty("animationsFrom").GetString()!;
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.Combine(s_art.Value.ArtRoot, otherRig))!, borrowed))
            .Should().Be(Path.GetFullPath(Path.Combine(s_art.Value.ArtRoot, referenceRig)));
        other.RootElement.GetProperty("weaponGrip").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .Should().Equal(reference.RootElement.GetProperty("weaponGrip").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal), "held items attach on every facing");
    }

    [Theory]
    [InlineData("both")]
    [InlineData("chain")]
    [InlineData("missing")]
    public void LoadFromDirectory_BadAnimationsFrom_IsRejected(string defect)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"carbon-rigs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "characters"));
        try
        {
            string male = File.ReadAllText(Path.Combine(s_art.Value.ArtRoot, "characters", "base_hero.cvg.json"));
            string female = File.ReadAllText(Path.Combine(s_art.Value.ArtRoot, "characters", "human_female.cvg.json"));
            File.WriteAllText(Path.Combine(dir, "characters", "base_hero.cvg.json"), male);
            File.WriteAllText(Path.Combine(dir, "characters", "borrower.cvg.json"), female.Replace("\"base_hero.cvg.json\"", "\"human_female.cvg.json\"", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(dir, "characters", "human_female.cvg.json"), defect switch
            {
                "both" => male.Replace("\"animations\":", "\"animationsFrom\": \"base_hero.cvg.json\", \"animations\":", StringComparison.Ordinal),
                "missing" => female.Replace("\"base_hero.cvg.json\"", "\"nobody.cvg.json\"", StringComparison.Ordinal),
                _ => female,
            });
            string rig = defect == "chain" ? "characters/borrower.cvg.json" : "characters/human_female.cvg.json";
            File.WriteAllText(Path.Combine(dir, "catalog.cvg.json"), $$"""{"characters":[{"id":"r","title":"R","file":"{{rig}}"}]}""");

            Action load = () => CarbonVectorArtLibrary.LoadFromDirectory(dir);

            load.Should().Throw<Exception>($"a rig with a {defect} animationsFrom must not load")
                .Where(e => (e is InvalidDataException || e is FileNotFoundException) && e.Message.Contains("animations", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string[] Bones(JsonDocument rig) =>
        [.. rig.RootElement.GetProperty("bonePivots").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
}
