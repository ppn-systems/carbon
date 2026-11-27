// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
#if DEBUG
using System.Collections.Generic;
#endif
using System.Numerics;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Infrastructure.Configuration;
using Carbon.Maps.Biomes;
using Carbon.Maps.Generators;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class WorldDebugOverridesTests
{
    [Fact]
    public void ReadDebugOverrides_NoVariables_IsInactive()
    {
        WorldDebugOverrides overrides = HostRuntimeOptions.ReadDebugOverrides(_ => null);

        overrides.IsActive.Should().BeFalse();
    }

#if DEBUG
    [Fact]
    public void ReadDebugOverrides_DebugBuild_ParsesSpawnAndHour()
    {
        Dictionary<string, string> env = new()
        {
            ["CARBON_DEBUG_SPAWN_TILE"] = "12,-7",
            ["CARBON_DEBUG_SPAWN_BIOME"] = "DuneDesert",
            ["CARBON_DEBUG_SPAWN_OFFSET"] = "3,4",
            ["CARBON_DEBUG_START_HOUR"] = "23.5",
            ["CARBON_DEBUG_FORGE_KIT"] = "1",
        };

        WorldDebugOverrides overrides = HostRuntimeOptions.ReadDebugOverrides(k => env.GetValueOrDefault(k));

        overrides.SpawnTile.Should().Be((12, -7));
        overrides.SpawnBiome.Should().Be("DuneDesert");
        overrides.SpawnOffset.Should().Be((3, 4));
        overrides.StartHour.Should().Be(23.5f);
        overrides.ForgeKit.Should().BeTrue();
    }
#else
    [Fact]
    public void ReadDebugOverrides_ReleaseBuild_IgnoresVariables()
    {
        WorldDebugOverrides overrides = HostRuntimeOptions.ReadDebugOverrides(_ => "1,1");

        overrides.IsActive.Should().BeFalse();
    }
#endif

    [Fact]
    public void ApplyDebugOverrides_SpawnBiome_MovesSpawnIntoThatBiome()
    {
        AuthoritativeWorld world = CreateWorld();

        _ = world.ApplyDebugOverrides(new WorldDebugOverrides { SpawnBiome = "AncientForest" });

        Vector2 spawn = world.DebugSpawnPosition!.Value;
        ProceduralWorldGenerator generator = new(1337UL);
        generator.SampleTile((int)MathF.Floor(spawn.X / 32f), (int)MathF.Floor(spawn.Y / 32f)).Biome.Should().Be(BiomeType.AncientForest);
    }

    [Fact]
    public void ApplyDebugOverrides_StartHour_WindsTheClock()
    {
        AuthoritativeWorld world = CreateWorld();

        _ = world.ApplyDebugOverrides(new WorldDebugOverrides { StartHour = 22f });

        WorldProgression.ComputeTimeOfDayHours(world.CurrentTick, world.TickRate).Should().BeApproximately(22f, 0.01f);
        world.DebugSpawnPosition.Should().BeNull();
    }

    [Fact]
    public void ApplyDebugOverrides_UnknownBiome_Throws()
    {
        AuthoritativeWorld world = CreateWorld();

        Action act = () => world.ApplyDebugOverrides(new WorldDebugOverrides { SpawnBiome = "Moon" });

        act.Should().Throw<ArgumentException>();
    }

    private static AuthoritativeWorld CreateWorld() => new(
        1337UL,
        32f,
        0.0008f,
        30,
        Substitute.For<IChunkDeltaRepository>(),
        Substitute.For<ICharacterStore>(),
        Substitute.For<Carbon.Abstractions.World.IWorldOutboundSink>());
}
