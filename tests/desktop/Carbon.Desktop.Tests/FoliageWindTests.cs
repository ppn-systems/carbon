// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Graphics.Batching.Common;
using Carbon.Maps.Props;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Wind-driven foliage (docs/art/graphics-quality.md): one deterministic wind field that rain whips up, gust fronts that
/// roll across a forest instead of every tree swaying in unison, and leaves and petals that fall downwind.
/// </summary>
public sealed class FoliageWindTests
{
    private static readonly Vector2 s_tree = new(1200f, -340f);

    [Fact]
    public void Sway_SameInputs_SameLean()
    {
        float a = WindField.Sway(12.5f, s_tree, 0.3f, 1f);
        float b = WindField.Sway(12.5f, s_tree, 0.3f, 1f);

        a.Should().Be(b, "every client and every frame-replay sees the same wind");
    }

    [Fact]
    public void Sway_OverAMinute_LeansDownwindOnAverage()
    {
        float sum = 0f;
        for (int i = 0; i < 600; i++)
        {
            sum += WindField.Sway(i * 0.1f, s_tree, 0f, 1f);
        }

        (sum / 600f).Should().BeGreaterThan(0.5f, "trees lean with the wind (east), not around zero");
    }

    [Fact]
    public void Sway_InRain_IsStrongerThanDry()
    {
        float dry = 0f;
        float wet = 0f;
        for (int i = 0; i < 600; i++)
        {
            dry += MathF.Abs(WindField.Sway(i * 0.1f, s_tree, 0f, 1f));
            wet += MathF.Abs(WindField.Sway(i * 0.1f, s_tree, 1f, 1f));
        }

        wet.Should().BeGreaterThan(dry * 1.6f, "a downpour whips the canopy around");
    }

    [Fact]
    public void Sway_YoungTree_SwaysLessThanMature()
    {
        float mature = WindField.Sway(3f, s_tree, 0.5f, 1f);
        float young = WindField.Sway(3f, s_tree, 0.5f, 0.6f);

        MathF.Abs(young).Should().BeLessThan(MathF.Abs(mature));
    }

    [Fact]
    public void Gust_RollsAcrossTheForest_NotInUnison()
    {
        // Trees 350 px apart along the wind sit at different points of the gust front most of the time.
        float apart = 0f;
        for (int i = 0; i < 200; i++)
        {
            apart += MathF.Abs(WindField.Gust(i * 0.1f, new Vector2(0f, 0f)) - WindField.Gust(i * 0.1f, new Vector2(350f, 0f)));
        }

        (apart / 200f).Should().BeGreaterThan(0.2f);

        float here = WindField.Gust(20f, new Vector2(0f, 0f));

        // The front travels east: the gust at x arrives at x + 200 about a second later.
        float later = WindField.Gust(21f, new Vector2(200f, 0f));
        later.Should().BeApproximately(here, 0.02f, "gust fronts move downwind at about 200 px/s");
    }

    [Fact]
    public void Sway_FrameToFrame_HasNoJumps()
    {
        float previous = WindField.Sway(0f, s_tree, 0.4f, 1f);
        for (int i = 1; i < 3600; i++)
        {
            float current = WindField.Sway(i / 60f, s_tree, 0.4f, 1f);
            MathF.Abs(current - previous).Should().BeLessThan(0.35f, $"a 60 fps frame never jerks the canopy (frame {i})");
            previous = current;
        }
    }

    [Fact]
    public void Sample_AFallingLeaf_DescendsAndDriftsDownwind()
    {
        Vector2 canopy = new(500f, 200f);
        (uint seed, float start) = FindFallingLeaf(canopy);

        float visible0 = FallingLeaves.Sample(seed, 0, start + 0.3f, canopy, 60f, 0f, out Vector2 early, out _);
        float visible1 = FallingLeaves.Sample(seed, 0, start + 3.5f, canopy, 60f, 0f, out Vector2 late, out _);

        visible0.Should().BeGreaterThan(0f);
        visible1.Should().BeGreaterThan(0f);
        late.Y.Should().BeGreaterThan(early.Y + 30f, "leaves fall toward the ground");
        late.X.Should().BeGreaterThan(early.X, "and the wind carries them east");
        late.Y.Should().BeLessThanOrEqualTo(canopy.Y + 60f + 8f, "and stop at the ground below the canopy");
    }

    [Fact]
    public void Sample_RainyWind_DropsMoreLeaves()
    {
        Vector2 canopy = new(500f, 200f);
        int dry = 0;
        int wet = 0;
        for (uint seed = 1; seed < 400; seed++)
        {
            dry += FallingLeaves.Sample(seed, 0, 2f, canopy, 60f, 0f, out _, out _) > 0f ? 1 : 0;
            wet += FallingLeaves.Sample(seed, 0, 2f, canopy, 60f, 1f, out _, out _) > 0f ? 1 : 0;
        }

        wet.Should().BeGreaterThan(dry, "a storm strips more leaves");
        dry.Should().BeGreaterThan(0, "some leaves fall in calm weather too");
    }

    [Theory]
    [InlineData(MapPropType.CherryBlossomTree, true)]
    [InlineData(MapPropType.OakTree, true)]
    [InlineData(MapPropType.BirchTree, true)]
    [InlineData(MapPropType.PineTree, false)]
    [InlineData(MapPropType.DeadTree, false)]
    public void TryGetColor_ByTree_ShedsLeavesOrPetals(MapPropType type, bool sheds)
    {
        FallingLeaves.TryGetColor(type, 0.2f, out ColorRgba color).Should().Be(sheds);
        if (type == MapPropType.CherryBlossomTree)
        {
            color.R.Should().BeGreaterThan(color.G, "blossom trees drop pink petals");
        }
    }

    [Fact]
    public void WindAndLeaves_EveryFrame_AllocateNothing()
    {
        Vector2 canopy = new(500f, 200f);
        AllocationResult window = AllocationProbe.Measure(2000, warmupIterations: 2000, i =>
        {
            _ = WindField.Sway(i * 0.016f, s_tree, 0.5f, 1f);
            _ = FallingLeaves.Sample((uint)i, i & 1, i * 0.016f, canopy, 60f, 0.5f, out _, out _);
        });

        window.Bytes.Should().Be(0, $"wind and leaves are sampled for every tree every frame ({window})");
    }

    // Finds a tree seed and a time at which slot 0 has just started falling.
    private static (uint Seed, float Start) FindFallingLeaf(Vector2 canopy)
    {
        for (uint seed = 1; seed < 1000; seed++)
        {
            for (float t = 0f; t < 14f; t += 0.05f)
            {
                if (FallingLeaves.Sample(seed, 0, t, canopy, 60f, 0f, out _, out _) <= 0f &&
                    FallingLeaves.Sample(seed, 0, t + 0.1f, canopy, 60f, 0f, out _, out _) > 0f)
                {
                    return (seed, t + 0.05f);
                }
            }
        }

        throw new InvalidOperationException("no falling leaf found");
    }
}
