// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Camera;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Desktop.Scenes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.TrueType;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Props;
using Carbon.Prediction.Reconciliation;
using FluentAssertions;
using NSubstitute;
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
        summary.MonstersValidated.Should().Be(9);
        summary.NpcsValidated.Should().Be(4);
        summary.ItemsValidated.Should().Be(85);
        summary.PropsValidated.Should().Be(22);
        summary.BiomesValidated.Should().Be(14);
        summary.StructuresValidated.Should().Be(Enum.GetValues<StructureType>().Length - 1, "every non-None structure type has art");
        summary.RasterImagesFound.Should().Be(0, "declarative vector pipeline uses zero PNG/JPG images");

        string? uiPanelDir = CarbonUiSkin.TryFindPanelDirectory();
        uiPanelDir.Should().NotBeNull("standardized 9-slice UI panels must exist under resources/ui/panels/");
        File.Exists(Path.Combine(uiPanelDir!, "10.png")).Should().BeTrue();
        File.Exists(Path.Combine(uiPanelDir!, "5.png")).Should().BeTrue();
        File.Exists(Path.Combine(uiPanelDir!, "15.png")).Should().BeTrue();

        CarbonVectorArtLibrary lib = CarbonVectorArtLibrary.LoadFromDirectory(artRoot!);
        Span<BonePose> pose = stackalloc BonePose[(int)VectorBoneId.Count];
        for (byte p = 0; p < 4; p++)
        {
            lib.TryGetCharacter(p, out CompiledVectorRig? rig).Should().BeTrue();
            rig!.GetBonePivot(VectorBoneId.ArmFront).Y.Should().BeLessThan(0f, "shoulder pivot must sit in upper body");
            rig.GetBonePivot(VectorBoneId.LegLeft).Y.Should().BeLessThan(0f, "hip pivot must sit above boots");
            rig.ClipNames.Should().Contain(["idle", "walk", "slash", "chop", "mine", "cast"]);
            rig.EvaluatePose("chop", "idle", 0.42f, isNormalized: true, pose);
            rig.EvaluatePose("mine", "idle", 0.40f, isNormalized: true, pose);
            rig.EvaluatePose("slash", "idle", 0.35f, isNormalized: true, pose);
            rig.EvaluatePose("cast", "idle", 0.45f, isNormalized: true, pose);
        }

        foreach (MapPropType propType in Enum.GetValues<MapPropType>())
        {
            if (propType == MapPropType.None)
            {
                continue;
            }

            lib.TryGetProp((byte)propType, out CompiledPropArt? propArt).Should().BeTrue($"prop {propType} must exist in props/*.cvg.json");
            propArt!.GroundVariants.Length.Should().BeGreaterThanOrEqualTo(1);
            if (propArt.HasCanopy)
            {
                propArt.CanopyVariants[0].Length.Should().BeGreaterThan(0);
            }
        }

        foreach (BiomeType biome in Enum.GetValues<BiomeType>())
        {
            lib.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt).Should().BeTrue($"biome {biome} must exist in biomes/*.cvg.json");
            biomeArt!.DisplayName.Should().NotBeNullOrWhiteSpace();
            biomeArt.DetailVariants.Length.Should().BeGreaterThanOrEqualTo(3, $"every biome ({biome}) should define surface micro-detail variants");
        }

        lib.GetBiomeDisplayName((byte)BiomeType.RedMesa).Should().Be("Red Mesa");

        foreach (StructureType structType in Enum.GetValues<StructureType>())
        {
            if (structType == StructureType.None)
            {
                continue;
            }

            lib.TryGetStructure((byte)structType, out CompiledStructureArt? structArt).Should().BeTrue($"structure {structType} must exist in structures/*.cvg.json");
            structArt!.Shapes.Length.Should().BeGreaterThan(0);
            if (structType is StructureType.WoodDoor or StructureType.StoneDoor)
            {
                structArt.OpenShapes.Length.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public void DesktopArchitecture_ShouldEnforceSegregatedInterfacesDipAndLauncherScriptInvariants()
    {
        // ISP: composite session implements all 4 focused role ports.
        typeof(IWorldSession).Should().Implement<IAuthenticationGateway>();
        typeof(IWorldSession).Should().Implement<ICharacterRosterService>();
        typeof(IWorldSession).Should().Implement<IWorldReadModel>();
        typeof(IWorldSession).Should().Implement<IGameplayController>();
        typeof(MultiplayerSession).Should().Implement<IWorldSession>();

        Assembly[] renamedAssemblies =
        [
            typeof(WorldGameplayScene).Assembly,
            typeof(MultiplayerSession).Assembly,
            typeof(IWorldSession).Assembly,
            typeof(HudOverlayRenderer).Assembly,
            typeof(SkillCatalog).Assembly,
            typeof(TrueTypeFontLoader).Assembly,
            typeof(PredictionReconciler).Assembly,
        ];
        foreach (Assembly assembly in renamedAssemblies)
        {
            Type[] types = [.. assembly.GetTypes().Where(static t => !t.Name.StartsWith('<'))];
            types.Select(static t => t.Name)
                .Should().NotContain(
                    name => name.Contains("Nalix", StringComparison.Ordinal) || name.Contains("Client", StringComparison.Ordinal),
                    because: $"types in {assembly.GetName().Name} should use clean domain names without Nalix* or Client* prefixes");
            types.Select(static t => t.Namespace ?? string.Empty)
                .Should().NotContain(
                    ns => ns.EndsWith(".Client", StringComparison.Ordinal),
                    because: $"namespaces in {assembly.GetName().Name} should avoid trailing .Client segments");
        }

        // CQRS & ISP: renderers accept read-only IWorldReadModel instead of mutation session.
        MethodInfo? worldRender = typeof(WorldSceneRenderer).GetMethod(nameof(WorldSceneRenderer.Render));
        worldRender.Should().NotBeNull();
        worldRender!.GetParameters()[0].ParameterType.Should().Be<IWorldReadModel>();

        MethodInfo? hudRender = typeof(HudOverlayRenderer).GetMethod(nameof(HudOverlayRenderer.Render));
        hudRender.Should().NotBeNull();
        hudRender!.GetParameters()[0].ParameterType.Should().Be<IWorldReadModel>();

        // DIP: renderers accept IVectorArtRepository injection.
        typeof(WorldSceneRenderer).GetConstructors()[0].GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(IVectorArtRepository));
        typeof(HudOverlayRenderer).GetConstructors()[0].GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(IVectorArtRepository));

        // Script folder strictly holds run-server.ps1 & run-client.ps1 only.
        string? artRoot = VectorArtValidator.TryFindArtRootDirectory();
        string repoRoot = FindRepoRoot(artRoot!);

        Type[] abstractionTypes = [.. typeof(IWorldSession).Assembly.GetTypes().Where(static t => !t.Name.StartsWith('<'))];
        abstractionTypes.Select(static t => t.Namespace ?? string.Empty)
            .Should().AllSatisfy(ns => ns.Should().StartWith("Carbon.Desktop.Abstractions."));
        Directory.GetFiles(Path.Combine(repoRoot, "desktop", "Carbon.Desktop.Abstractions"), "*.cs", SearchOption.TopDirectoryOnly)
            .Should().BeEmpty("all abstractions should live inside domain subfolders");
        string scriptDir = Path.Combine(repoRoot, "script");
        string[] ps1Files =
        [
            .. Directory.GetFiles(scriptDir, "*.ps1")
                .Select(static f => Path.GetFileName(f)!)
                .OrderBy(static n => n, StringComparer.Ordinal),
        ];
        ps1Files.Should().Equal("run-client.ps1", "run-server.ps1");
        Directory.GetFiles(scriptDir, "*.bat").Should().BeEmpty();
        Directory.GetFiles(scriptDir, "*.cmd").Should().BeEmpty();
        Directory.GetFiles(scriptDir, "*.sh").Should().BeEmpty();

        string tempCfg = Path.Combine(Path.GetTempPath(), $"carbon-ui-settings-{Guid.NewGuid():N}.cfg");
        try
        {
            UiSettings saved = new()
            {
                ShowMinimap = false,
                ShowCombatNumbers = false,
                ShowTelemetry = false,
                ShowKeybindHints = true,
                CameraZoomPreset = 2,
            };
            saved.Save(tempCfg);

            UiSettings loaded = UiSettings.LoadOrDefault(tempCfg);
            loaded.ShowMinimap.Should().BeFalse();
            loaded.ShowCombatNumbers.Should().BeFalse();
            loaded.ShowTelemetry.Should().BeFalse();
            loaded.ShowKeybindHints.Should().BeTrue();
            loaded.CameraZoomPreset.Should().Be(2);
        }
        finally
        {
            if (File.Exists(tempCfg))
            {
                File.Delete(tempCfg);
            }
        }
    }

    [Fact]
    public void UnifiedPlayerModal_ShouldMaintainEqualTabWidthsAndFixedSizeAcrossAllTabs()
    {
        IWorldReadModel fakeWorld = Substitute.For<IWorldReadModel>();
        InventoryModalWidget bag = new();
        CharacterSheetModalWidget character = new();
        CraftingModalWidget crafting = new();
        ClassSkillsModalWidget skills = new();

        (int Width, int Height)[] viewports =
        [
            (1280, 720),
            (1600, 900),
            (1920, 1080),
            (2560, 1440),
        ];

        foreach ((int vw, int vh) in viewports)
        {
            PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(vw, vh);
            (layout.PanelH / vh).Should().BeLessThan(0.76f);
            (layout.PanelW / vw).Should().BeLessThan(0.60f);

            foreach (int tabCount in new[] { 3, 4 })
            {
                RectangleF first = PlayerModalTabs.GetTabRect(in layout, 0, tabCount);
                RectangleF last = PlayerModalTabs.GetTabRect(in layout, tabCount - 1, tabCount);
                first.X.Should().Be(layout.Ox);
                last.Right.Should().Be(layout.Ox + layout.PanelW);

                for (int i = 0; i < tabCount; i++)
                {
                    RectangleF tab = PlayerModalTabs.GetTabRect(in layout, i, tabCount);
                    tab.Height.Should().Be(first.Height);
                    MathF.Abs(tab.Width - first.Width).Should().BeLessThanOrEqualTo(2f);
                    if (i > 0)
                    {
                        RectangleF prev = PlayerModalTabs.GetTabRect(in layout, i - 1, tabCount);
                        tab.X.Should().BeGreaterThan(prev.Right);
                    }
                }
            }

            Vector2 topLeftInside = new(layout.Ox + 4f, layout.Oy + 4f);
            Vector2 bottomRightInside = new(layout.Ox + layout.PanelW - 4f, layout.Oy + layout.PanelH - 4f);
            Vector2 tabPoint = new(layout.Ox + (layout.PanelW * 0.5f), layout.Oy - 8f);
            Vector2 outsideLeft = new(layout.Ox - 4f, layout.Oy + (layout.PanelH * 0.5f));
            Vector2 outsideBottom = new(layout.Ox + (layout.PanelW * 0.5f), layout.Oy + layout.PanelH + 4f);

            RectangleF closeRect = PlayerModalTabs.GetCloseButtonRect(in layout);
            Vector2 closeCenter = new(closeRect.X + (closeRect.Width * 0.5f), closeRect.Y + (closeRect.Height * 0.5f));

            Func<Vector2, bool>[] containsChecks =
            [
                p => bag.ContainsPoint(fakeWorld, p, vw, vh),
                p => character.ContainsPoint(fakeWorld, p, vw, vh),
                p => crafting.ContainsPoint(fakeWorld, p, vw, vh),
                p => skills.ContainsPoint(fakeWorld, p, vw, vh),
            ];

            Func<Vector2, bool>[] closeChecks =
            [
                p => bag.TryHitTestCloseButton(p, vw, vh),
                p => character.TryHitTestCloseButton(p, vw, vh),
                p => crafting.TryHitTestCloseButton(p, vw, vh),
                p => skills.TryHitTestCloseButton(p, vw, vh),
            ];

            foreach (Func<Vector2, bool> contains in containsChecks)
            {
                contains(topLeftInside).Should().BeTrue();
                contains(bottomRightInside).Should().BeTrue();
                contains(tabPoint).Should().BeTrue();
                contains(outsideLeft).Should().BeFalse();
                contains(outsideBottom).Should().BeFalse();
            }

            foreach (Func<Vector2, bool> hitClose in closeChecks)
            {
                hitClose(closeCenter).Should().BeTrue();
                hitClose(topLeftInside).Should().BeFalse();
            }

            RectangleF hotbar0 = InventoryModalWidget.GetSlotRectForTesting(in layout, 0);
            RectangleF hotbar8 = InventoryModalWidget.GetSlotRectForTesting(in layout, 8);
            hotbar0.X.Should().Be(layout.ContentX);
            hotbar8.Right.Should().Be(layout.ContentX + layout.ContentW);
            hotbar0.Bottom.Should().Be(layout.ContentY + layout.ContentH);
        }
    }

    [Fact]
    public void SceneManagerAndCameraRig_ShouldKeepViewportAndWorldToScreenAlignedAfterResizeAndSceneSwap()
    {
        int width = 1280;
        int height = 720;
        IGameWindow fakeWindow = Substitute.For<IGameWindow>();
        fakeWindow.Width.Returns(_ => width);
        fakeWindow.Height.Returns(_ => height);

        Camera2D camera = new(1280f, 720f);
        using SceneManager sceneManager = new(fakeWindow, camera);
        using StubScene initialScene = new();
        sceneManager.ChangeScene(initialScene);

        // Simulate window maximize while still on Login / Character Select (scenes that do not override Resize).
        width = 1920;
        height = 1017;
        sceneManager.Resize(width, height);
        camera.ViewportSize.Should().Be(new Vector2(1920f, 1017f));

        // Swap to a new scene and verify Camera2D retains synchronized framebuffer dimensions.
        using StubScene worldScene = new();
        sceneManager.ChangeScene(worldScene);
        CameraRig2D rig = new(camera, macroView: false);
        rig.Update(new Vector2(3600f, 15900f), insideBuilding: false, default, 0.016f);

        Vector2 monsterWorld = new(3420f, 15840f);
        Vector2 screenPt = camera.WorldToScreen(in monsterWorld);

        Vector4 clip = Vector4.Transform(new Vector4(monsterWorld.X, monsterWorld.Y, 0f, 1f), camera.ViewProjectionMatrix);
        float expectedX = ((clip.X / clip.W) + 1f) * 0.5f * 1920f;
        float expectedY = (1f - (clip.Y / clip.W)) * 0.5f * 1017f;

        screenPt.X.Should().BeApproximately(expectedX, 0.01f);
        screenPt.Y.Should().BeApproximately(expectedY, 0.01f);
        camera.ScreenToWorld(in screenPt).X.Should().BeApproximately(monsterWorld.X, 0.05f);
        camera.ScreenToWorld(in screenPt).Y.Should().BeApproximately(monsterWorld.Y, 0.05f);
    }

    [Fact]
    public void Desktop_Assemblies_Have_Zero_Root_Files_And_No_Domain_Duplication()
    {
        string repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        string[] guardedProjects =
        [
            Path.Combine(repoRoot, "desktop", "Carbon.Desktop.Abstractions"),
            Path.Combine(repoRoot, "desktop", "Carbon.Desktop.Networking"),
            Path.Combine(repoRoot, "desktop", "Carbon.Desktop.Rendering"),
        ];

        foreach (string projDir in guardedProjects)
        {
            string[] rootCsFiles = Directory.GetFiles(projDir, "*.cs", SearchOption.TopDirectoryOnly);
            rootCsFiles.Should().BeEmpty($"domain files in {Path.GetFileName(projDir)} must live in domain subfolders");
        }

        string craftWidgetPath = Path.Combine(repoRoot, "desktop", "Carbon.Desktop.Rendering", "Hud", "CraftingModalWidget.cs");
        string craftWidgetSource = File.ReadAllText(craftWidgetPath);
        craftWidgetSource.Should().NotContain("CanCraftFromSpan", "crafting eligibility logic belongs in PlayerInventoryLayout");
        craftWidgetSource.Should().NotContain("CountBagItem", "bag counting logic belongs in PlayerInventoryLayout");
    }

    [Fact]
    public void All_CvgJson_Art_Polygons_Are_Convex_And_All_Items_And_Monster_Animations_Exist()
    {
        string repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        string artRoot = Path.Combine(repoRoot, "resources", "art");
        string[] allCvgFiles = Directory.GetFiles(artRoot, "*.cvg.json", SearchOption.AllDirectories);
        allCvgFiles.Should().NotBeEmpty();

        Span<Vector2> pts = stackalloc Vector2[12];
        foreach (string file in allCvgFiles)
        {
            if (string.Equals(Path.GetFileName(file), "catalog.cvg.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("shapes", out JsonElement shapes))
            {
                continue;
            }

            foreach (JsonElement shape in shapes.EnumerateArray())
            {
                if (!shape.TryGetProperty("kind", out JsonElement kindEl) ||
                    !string.Equals(kindEl.GetString(), "polygon", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JsonElement ptsEl = shape.GetProperty("points");
                int count = ptsEl.GetArrayLength();
                count.Should().BeInRange(3, 12, $"polygon in {Path.GetFileName(file)} must have 3..12 vertices");

                for (int i = 0; i < count; i++)
                {
                    pts[i] = new Vector2(ptsEl[i][0].GetSingle(), ptsEl[i][1].GetSingle());
                }

                int sign = 0;
                for (int i = 0; i < count; i++)
                {
                    Vector2 a = pts[i];
                    Vector2 b = pts[(i + 1) % count];
                    Vector2 c = pts[(i + 2) % count];
                    float cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
                    if (MathF.Abs(cross) > 1e-4f)
                    {
                        int s = cross > 0f ? 1 : -1;
                        if (sign == 0)
                        {
                            sign = s;
                        }
                        else
                        {
                            sign.Should().Be(s, $"polygon in {Path.GetFileName(file)} must be convex");
                        }
                    }
                }

                sign.Should().NotBe(0, $"polygon in {Path.GetFileName(file)} must be non-degenerate");
            }
        }

        CarbonVectorArtLibrary? lib = CarbonVectorArtLibrary.LoadFromDirectory(artRoot);
        lib.Should().NotBeNull();

        foreach (Carbon.Items.Definitions.ItemArchetype item in StarterGameCatalog.Items.All)
        {
            lib!.TryGetItem(item.Id, out CompiledItemVectorArt? itemArt).Should().BeTrue($"item {item.Id} ({item.DisplayName}) must have compiled cvg.json art");
            itemArt.Should().NotBeNull();
        }

        lib!.TryGetUiIcon("coin", out CompiledItemVectorArt? coinIcon).Should().BeTrue("the wallet HUD needs a gold coin icon");
        lib.TryGetUiIcon("diamond", out CompiledItemVectorArt? diamondIcon).Should().BeTrue("the wallet HUD needs a diamond icon");
        coinIcon!.Shapes.Length.Should().BePositive();
        diamondIcon!.Shapes.Length.Should().BePositive();

        string[] expectedMonsterClips =
        [
            "skeleton_archer.cvg.json:attack_bow",
            "ash_wraith.cvg.json:attack_cast",
            "orc_shaman.cvg.json:attack_cast",
            "rock_golem.cvg.json:attack_slam",
            "crimson_demon.cvg.json:attack_slam",
        ];

        foreach (string spec in expectedMonsterClips)
        {
            string[] parts = spec.Split(':');
            using JsonDocument mDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(artRoot, "monsters", parts[0])));
            JsonElement anims = mDoc.RootElement.GetProperty("animations");
            anims.TryGetProperty("attack", out _).Should().BeTrue();
            anims.TryGetProperty(parts[1], out JsonElement clip).Should().BeTrue($"{parts[0]} must define {parts[1]}");
            clip.GetProperty("tracks").GetArrayLength().Should().BeGreaterThanOrEqualTo(4);
        }
    }

    private sealed class StubScene : GameScene;

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
