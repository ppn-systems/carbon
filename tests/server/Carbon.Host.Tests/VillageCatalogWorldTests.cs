// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Maps.Building;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// The server plans villages with the <c>structures.json</c> it serves (the one it builds with), not the copy compiled
/// into <c>Carbon.Maps</c>, so a client planning with its synced copy lays out the same villages.
/// </summary>
public sealed class VillageCatalogWorldTests
{
    [Fact]
    public void Generator_ServedCatalog_PlansVillagesWithTheSameCatalogItBuildsWith()
    {
        StructureCatalog served = StructureCatalog.LoadEmbedded();

        AuthoritativeWorld world = new(
            1337UL,
            32f,
            0.0008f,
            30,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
            entropy: TestGameplayEntropy.Create(1337UL),
            structureCatalog: served);

        world.Generator.Structures.Should().BeSameAs(served);
    }
}
