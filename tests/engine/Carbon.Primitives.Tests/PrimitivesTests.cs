// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Carbon.Primitives.Buffers;
using Carbon.Primitives.Collections;
using Carbon.Primitives.Math;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Primitives.Tests;

public sealed partial class PrimitivesTests
{
    [Fact]
    public void PrimitivesSource_UsesNoPlatformDependentMath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "engine", "Carbon.Primitives")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull();
        string root = Path.Combine(dir!.FullName, "engine", "Carbon.Primitives");
        string[] offenders =
        [
            .. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
                .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && BannedMath().IsMatch(x.line))
                .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
        ];

        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(-1f, -1f)]
    [InlineData(0.3f, -0.7f)]
    public void DeterministicMath_Atan2AndWrapAngle_MatchSystemMath(float y, float x)
    {
        DeterministicMath.Atan2(y, x).Should().BeApproximately(MathF.Atan2(y, x), 0.0001f);
        DeterministicMath.WrapAngle(7.5f).Should().BeApproximately(7.5f - (2f * MathF.PI), 0.001f);
    }

    [Fact]
    public void DeterministicRandom_NextUnitVector2_ReturnsUnitLength()
    {
        DeterministicRandom rng = new(99UL);
        Vector2 dir = rng.NextUnitVector2();
        dir.Length().Should().BeApproximately(1f, 0.0005f);
    }

    [Fact]
    public void RingBuffer_PushBackAndPopFront_OverwritesOldestWhenFull()
    {
        RingBuffer<int> ring = new(capacity: 4);
        ring.Capacity.Should().Be(4);
        ring.IsEmpty.Should().BeTrue();

        ring.PushBack(1);
        ring.PushBack(2);
        ring.PushBack(3);
        ring.PushBack(4);
        ring.IsFull.Should().BeTrue();

        ring.PushBack(5); // Overwrites 1
        ring.Count.Should().Be(4);
        ring[0].Should().Be(2);
        ring[3].Should().Be(5);

        ring.TryPopFront(out int first).Should().BeTrue();
        first.Should().Be(2);
        ring.Count.Should().Be(3);

        ring.Clear();
        ring.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void BitSet256_SetClearPopCount_TracksAll256Bits()
    {
        BitSet256 bits = default;
        bits.IsEmpty.Should().BeTrue();

        bits.Set(0);
        bits.Set(64);
        bits.Set(130);
        bits.Set(255);

        bits.PopCount.Should().Be(4);
        bits.IsSet(64).Should().BeTrue();
        bits.IsSet(255).Should().BeTrue();

        bits.Clear(64);
        bits.IsSet(64).Should().BeFalse();
        bits.PopCount.Should().Be(3);

        bits.ClearAll();
        bits.IsEmpty.Should().BeTrue();
    }

    [GeneratedRegex(@"\bMathF?\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Log|Log2|Log10|Exp|Pow|Cbrt|Sinh|Cosh|Tanh)\s*\(")]
    private static partial Regex BannedMath();
}
