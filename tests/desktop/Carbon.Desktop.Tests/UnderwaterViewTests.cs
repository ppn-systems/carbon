// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.Water;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Batching.Common;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-032 D13: the underwater view, the oxygen bar and the swimming poses.</summary>
public sealed class UnderwaterViewTests
{
    private const float Tile = 32f;

    [Theory]
    [InlineData(SwimState.Walking)]
    [InlineData(SwimState.Wading)]
    public void For_OutOfDeepWater_DrawsNothing(SwimState state)
    {
        UnderwaterLook look = UnderwaterLook.For(new WaterView(state, WaterDepthTier.Shallow, 0.03f, 30f, 30f));

        look.IsVisible.Should().BeFalse("wading or walking is not under water");
    }

    [Fact]
    public void For_SwimmingOnTheSurface_TintsLightlyWithoutFogOrBubbles()
    {
        UnderwaterLook look = UnderwaterLook.For(new WaterView(SwimState.Swimming, WaterDepthTier.Medium, 0.07f, 30f, 30f));

        look.IsVisible.Should().BeTrue();
        look.IsSubmerged.Should().BeFalse("a swimmer's head is above the surface");
        look.EdgeAlpha.Should().Be(0, "the view is not fogged on the surface");
    }

    [Fact]
    public void For_DivingDeeper_GetsDarkerWithANarrowerViewAllTheWayToTheAbyssFloor()
    {
        UnderwaterLook previous = UnderwaterLook.For(new WaterView(SwimState.Swimming, WaterDepthTier.Medium, 0.09f, 30f, 30f));
        for (float depth = WaterDepth.DeepFrom; depth <= WaterDepth.MaxDepth; depth += 0.01f)
        {
            UnderwaterLook look = UnderwaterLook.For(new WaterView(SwimState.Diving, WaterDepth.TierOf(depth), depth, 20f, 30f));

            look.IsSubmerged.Should().BeTrue();
            look.Strength.Should().BeGreaterThan(previous.Strength, $"water {depth:F2} deep must be darker than shallower water");
            look.TintAlpha.Should().BeGreaterThanOrEqualTo(previous.TintAlpha);
            look.ClearRadiusFraction.Should().BeLessThanOrEqualTo(previous.ClearRadiusFraction, "the view narrows with depth");
            previous = look;
        }

        previous.ClearRadiusFraction.Should().BeLessThan(0.3f, "the abyss leaves only a small circle of view");
    }

    [Theory]
    [InlineData(SwimState.Walking, 30f, false)]
    [InlineData(SwimState.Walking, 12f, true)]
    [InlineData(SwimState.Swimming, 30f, false)]
    [InlineData(SwimState.Diving, 30f, true)]
    [InlineData(SwimState.Diving, 3f, true)]
    public void ShowsOxygen_DivingOrNotYetRefilled_ShowsTheBar(SwimState state, float oxygen, bool expected) =>
        new WaterView(state, WaterDepthTier.Deep, 0.12f, oxygen, 30f).ShowsOxygen.Should().Be(expected);

    [Fact]
    public void ShowsOxygen_BeforeTheServerSentTheBreath_HidesTheBar() =>
        new WaterView(SwimState.Diving, WaterDepthTier.Deep, 0.12f, 0f, 0f).ShowsOxygen.Should().BeFalse();

    [Fact]
    public void Label_AirLeftAndOutOfAir_ReadsTheLocalizedText()
    {
        OxygenBarWidget bar = new();

        bar.Label(new WaterView(SwimState.Diving, WaterDepthTier.Deep, 0.12f, 12.2f, 30f)).Should().Contain("13");
        string drowning = bar.Label(new WaterView(SwimState.Diving, WaterDepthTier.Deep, 0.12f, 0f, 30f));
        drowning.Should().Be(GameText.Get("water.out_of_air"));
        drowning.Should().NotBe("water.out_of_air", "the key must exist in the locale files");
    }

    [Theory]
    [InlineData(1024, 640)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void GetOxygenBarRect_SitsAboveTheVitalsClearOfTheHotbarStatusStripAndColumn(int width, int height)
    {
        RectangleF bar = HudLayout.GetOxygenBarRect(width, height);
        RectangleF vitals = new((width - 526f) * 0.5f, HudLayout.GetVitalsTop(height), 526f, 20f);
        RectangleF hotbar = new((width - 526f) * 0.5f, height - 54f - 16f, 526f, 54f);

        bar.Bottom.Should().BeLessThan(vitals.Y, "the bar sits above the vitals");
        (vitals.Y - bar.Bottom).Should().BeLessThan(12f, "and close enough to read as one of them");
        Overlaps(bar, hotbar).Should().BeFalse();
        Overlaps(bar, HudLayout.GetStatusStripRect(width, showMinimap: true, StatusEffectStripLayout.MaxRows)).Should().BeFalse();
        Overlaps(bar, HudLayout.GetMinimapFrameRect(width)).Should().BeFalse();
        (bar.X + (bar.Width * 0.5f)).Should().BeApproximately(width * 0.5f, 1f, "the bar is centred like the vitals");
    }

    [Fact]
    public void MainHud_DrawsTheUnderwaterVeilFirstAndTheOxygenBarWithTheHud()
    {
        ReadOnlySpan<HudElement> main = HudComposition.MainHud;

        main[0].Should().Be(HudElement.UnderwaterVeil, "the filter covers the world but not the HUD drawn after it");
        main.ToArray().Should().Contain(HudElement.OxygenBar);
    }

    [Fact]
    public void ViewAt_AfterAnOxygenSync_CarriesTheServersBreathAndThePredictedDepth()
    {
        WaterSession water = new();
        using PlayerOxygenSync sync = new() { OxygenMs = 12_500U, MaxOxygenMs = 30_000U };
        water.OnPlayerOxygenSync(sync);
        ChunkWorld2D world = CreateLake();

        WaterView view = water.ViewAt(world, Centre(10));

        view.OxygenSeconds.Should().Be(12.5f);
        view.MaxOxygenSeconds.Should().Be(30f);
        view.SwimState.Should().Be(SwimState.Diving, "the swim state follows the terrain under the predicted position at once");
        view.Tier.Should().Be(WaterDepthTier.Deep);
        water.ViewAt(world, Centre(3)).SwimState.Should().Be(SwimState.Walking);
    }

    [Fact]
    public void Resolve_SwimmingAndDiving_SinkThePlayerToTheShouldersOrUnderTheSurface()
    {
        ChunkWorld2D world = CreateLake();

        WadingRenderer.Wade swim = WadingRenderer.Resolve(world, Centre(8), 36f, 7f);
        WadingRenderer.Wade dive = WadingRenderer.Resolve(world, Centre(10), 36f, 7f);
        WadingRenderer.Wade dry = WadingRenderer.Resolve(world, Centre(3), 36f, 7f);

        swim.State.Should().Be(SwimState.Swimming);
        swim.Sink.Should().BeApproximately(WadingRenderer.SwimSinkFraction * 36f, 0.01f);
        dive.State.Should().Be(SwimState.Diving);
        dive.IsWading.Should().BeTrue("a diver is drawn in the water, not standing on it");
        dry.IsWading.Should().BeFalse();
        WorldSceneRenderer.SwimClip(swim.State).Should().Be("swim");
        WorldSceneRenderer.SwimClip(dive.State).Should().Be("dive");
        WorldSceneRenderer.SwimClip(SwimState.Wading).Should().BeNull("waders walk");
    }

    [Fact]
    public void BaseHero_HasTheSwimAndDiveClipsTheRendererAsksFor()
    {
        using JsonDocument rig = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "resources", "art", "characters", "base_hero.cvg.json")));
        JsonElement animations = rig.RootElement.GetProperty("animations");

        foreach (SwimState state in new[] { SwimState.Swimming, SwimState.Diving })
        {
            string clip = WorldSceneRenderer.SwimClip(state)!;
            animations.TryGetProperty(clip, out JsonElement found).Should().BeTrue($"base_hero needs a '{clip}' clip");
            found.GetProperty("loop").GetBoolean().Should().BeTrue("strokes repeat");
        }
    }

    private static bool Overlaps(RectangleF a, RectangleF b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static Vector2 Centre(int tx) => new((tx + 0.5f) * Tile, 8.5f * Tile);

    // Land to x = 5, then Shallow (6..7), Medium (8), Deep (9..12) water in rows 4..12 of chunk (0, 0).
    private static ChunkWorld2D CreateLake()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(99UL), tileSize: Tile);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);
        chunk.ClearProps();
        for (int y = 2; y <= 14; y++)
        {
            for (int x = 2; x <= 15; x++)
            {
                float depth = y is < 4 or > 12 || x <= 5 || x >= 13 ? 0f : x <= 7 ? 0.03f : x == 8 ? 0.07f : 0.15f;
                BiomeType biome = depth > 0f ? WaterDepth.BiomeFor(WaterDepth.TierOf(depth)) : BiomeType.LushPlains;
                float elevation = depth > 0f ? WaterDepth.ToElevation(depth) : 0.5f;
                chunk.SetTile(x, y, new MapTile(biome, elevation, 0.5f, 0.5f, BiomeProperties.Get(biome).IsSolid, Variant: 0));
            }
        }

        return world;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
