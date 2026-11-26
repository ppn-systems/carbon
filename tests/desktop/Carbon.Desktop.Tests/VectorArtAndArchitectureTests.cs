// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Networking;
using Carbon.Desktop.Rendering;
using Carbon.Desktop.Rendering.VectorArt;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class VectorArtAndArchitectureTests
{
    [Fact]
    public void ExternalVectorArtCatalog_ShouldValidateAllRigsItemsAndContainZeroRasterImages()
    {
        string? artRoot = VectorArtValidator.TryFindArtRootDirectory();
        artRoot.Should().NotBeNull("resources/art/catalog.cvg.json must exist in repository or build output");

        string repoRoot = FindRepoRoot(artRoot!);
        Directory.Exists(Path.Combine(repoRoot, "resources", "sprites")).Should().BeFalse("deprecated PNG sprite folder must be absent");
        Directory.Exists(Path.Combine(repoRoot, "resources", "vector-art")).Should().BeFalse("duplicate vector-art folder must not exist");

        VectorArtValidationSummary summary = VectorArtValidator.ValidateDirectory(artRoot!);
        summary.CharactersValidated.Should().Be(4);
        summary.MonstersValidated.Should().Be(4);
        summary.NpcsValidated.Should().Be(3);
        summary.ItemsValidated.Should().Be(12);
        summary.RasterImagesFound.Should().Be(0, "declarative vector pipeline uses zero PNG/JPG images");

        CarbonVectorArtLibrary lib = CarbonVectorArtLibrary.LoadFromDirectory(artRoot!);
        Span<BonePose> pose = stackalloc BonePose[(int)VectorBoneId.Count];
        for (byte p = 0; p < 4; p++)
        {
            lib.TryGetCharacter(p, out CompiledVectorRig? rig).Should().BeTrue();
            rig!.GetBonePivot(VectorBoneId.ArmFront).Y.Should().BeLessThan(0f, "shoulder pivot must sit in upper body");
            rig.GetBonePivot(VectorBoneId.LegLeft).Y.Should().BeLessThan(0f, "hip pivot must sit above boots");
            rig.ClipNames.Should().Contain(["idle", "walk", "slash", "chop", "mine"]);
            rig.EvaluatePose("chop", "idle", 0.42f, isNormalized: true, pose);
            rig.EvaluatePose("mine", "idle", 0.40f, isNormalized: true, pose);
            rig.EvaluatePose("slash", "idle", 0.35f, isNormalized: true, pose);
        }
    }

    [Fact]
    public void ClientArchitecture_ShouldEnforceSegregatedInterfacesDipAndLauncherScriptInvariants()
    {
        // ISP: composite session implements all 4 focused client role ports.
        typeof(IClientWorldSession).Should().Implement<IClientAuthService>();
        typeof(IClientWorldSession).Should().Implement<IClientCharacterService>();
        typeof(IClientWorldSession).Should().Implement<IClientWorldReadModel>();
        typeof(IClientWorldSession).Should().Implement<IClientGameplayController>();
        typeof(NalixMultiplayerSession).Should().Implement<IClientWorldSession>();

        // CQRS & ISP: renderers accept read-only IClientWorldReadModel instead of mutation session.
        MethodInfo? worldRender = typeof(WorldSceneRenderer).GetMethod(nameof(WorldSceneRenderer.Render));
        worldRender.Should().NotBeNull();
        worldRender!.GetParameters()[0].ParameterType.Should().Be<IClientWorldReadModel>();

        MethodInfo? hudRender = typeof(HudOverlayRenderer).GetMethod(nameof(HudOverlayRenderer.Render));
        hudRender.Should().NotBeNull();
        hudRender!.GetParameters()[0].ParameterType.Should().Be<IClientWorldReadModel>();

        // DIP: renderers accept IVectorArtRepository injection.
        typeof(WorldSceneRenderer).GetConstructors()[0].GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(IVectorArtRepository));
        typeof(HudOverlayRenderer).GetConstructors()[0].GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(IVectorArtRepository));

        // Script folder strictly holds run-server.ps1 & run-client.ps1 only.
        string? artRoot = VectorArtValidator.TryFindArtRootDirectory();
        string repoRoot = FindRepoRoot(artRoot!);
        string scriptDir = Path.Combine(repoRoot, "script");
        string[] ps1Files = Directory.GetFiles(scriptDir, "*.ps1")
            .Select(Path.GetFileName)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray()!;
        ps1Files.Should().Equal("run-client.ps1", "run-server.ps1");
        Directory.GetFiles(scriptDir, "*.bat").Should().BeEmpty();
        Directory.GetFiles(scriptDir, "*.cmd").Should().BeEmpty();
        Directory.GetFiles(scriptDir, "*.sh").Should().BeEmpty();
    }

    private static string FindRepoRoot(string startPath)
    {
        DirectoryInfo? cursor = new(startPath);
        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "Carbon.slnx")))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        return Path.GetFullPath(Path.Combine(startPath, "..", ".."));
    }
}
