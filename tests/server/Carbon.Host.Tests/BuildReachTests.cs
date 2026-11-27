// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Maps.Building;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// How far a player may build (audit round 3, DUP-1). Reach is one value, <c>structures.json → placementReachTiles</c>,
/// measured from the player to the tile centre. An out-of-reach request is refused with <see cref="OpStatus.OutOfRange"/>
/// before any terrain or material check, so "not OutOfRange" means the reach rule let it through.
/// </summary>
public sealed class BuildReachTests
{
    private const float TileSize = 32f;
    private const ulong Builder = 7001UL;

    [Fact]
    public void TryPlaceStructure_TwoTilesAway_IsInReach()
    {
        (AuthoritativeWorld world, Vector2 at) = Enter(catalog: null);

        (int tx, int ty) = TileAtDistance(at, 1.8f, 2.4f);

        world.TryPlaceStructure(Builder, tx, ty, StructureType.WoodWall).Status.Should().NotBe(OpStatus.OutOfRange);
    }

    [Fact]
    public void TryPlaceStructure_SixTilesAway_IsOutOfReach()
    {
        (AuthoritativeWorld world, Vector2 at) = Enter(catalog: null);

        (int tx, int ty) = TileAtDistance(at, 5.6f, 6.4f);

        world.TryPlaceStructure(Builder, tx, ty, StructureType.WoodWall).Status.Should().Be(OpStatus.OutOfRange);
    }

    [Fact]
    public void TryPlaceStructure_ShippedReach_AllowsUpToFourTiles()
    {
        (AuthoritativeWorld world, Vector2 at) = Enter(catalog: null);
        world.StructureCatalog.Rules.PlacementReachTiles.Should().Be(4, "structures.json ships a 4-tile reach (owner decision, DUP-1)");

        (int inX, int inY) = TileAtDistance(at, 3.6f, 3.95f);
        (int outX, int outY) = TileAtDistance(at, 4.1f, 4.6f);

        world.TryPlaceStructure(Builder, inX, inY, StructureType.WoodWall).Status.Should().NotBe(OpStatus.OutOfRange, "3.6–3.95 tiles is within 4 (the old 3.5 refused it)");
        world.TryPlaceStructure(Builder, outX, outY, StructureType.WoodWall).Status.Should().Be(OpStatus.OutOfRange);
    }

    [Fact]
    public void TryPlaceStructure_CatalogWithASixTileReach_AllowsFiveAndAHalfTiles()
    {
        StructureCatalog wide = StructureCatalog.Parse(ShippedStructuresJson().Replace("\"placementReachTiles\": 4", "\"placementReachTiles\": 6", StringComparison.Ordinal));
        (AuthoritativeWorld world, Vector2 at) = Enter(wide);

        (int tx, int ty) = TileAtDistance(at, 5.2f, 5.8f);

        world.TryPlaceStructure(Builder, tx, ty, StructureType.WoodWall).Status.Should().NotBe(OpStatus.OutOfRange, "the server reads the reach from the loaded catalog");
    }

    [Fact]
    public void TryToggleDoor_UsesTheSameReach()
    {
        (AuthoritativeWorld world, Vector2 at) = Enter(catalog: null);

        (int inX, int inY) = TileAtDistance(at, 3.6f, 3.95f);
        (int outX, int outY) = TileAtDistance(at, 4.1f, 4.6f);

        world.TryToggleDoor(Builder, inX, inY).Status.Should().NotBe(OpStatus.OutOfRange);
        world.TryToggleDoor(Builder, outX, outY).Status.Should().Be(OpStatus.OutOfRange);
    }

    [Fact]
    public void TryDemolishStructure_FiveTilesAway_IsOutOfReach()
    {
        (AuthoritativeWorld world, Vector2 at) = Enter(catalog: null);

        (int tx, int ty) = TileAtDistance(at, 4.6f, 5.4f);

        world.TryDemolishStructure(Builder, tx, ty).Status.Should().Be(OpStatus.OutOfRange);
    }

    private static (AuthoritativeWorld World, Vector2 At) Enter(StructureCatalog? catalog)
    {
        AuthoritativeWorld world = new(
            1337UL,
            TileSize,
            0.0008f,
            30,
            Substitute.For<IChunkDeltaRepository>(),
            Substitute.For<ICharacterStore>(),
            new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
            entropy: TestGameplayEntropy.Create(1337UL),
            structureCatalog: catalog);
        Vector2 at = world.DefaultSpawnWorldPosition;
        CharacterRecord hero = new(Guid.NewGuid(), Guid.NewGuid(), "Mason", 1, default, at.X, at.Y, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
        EnterWorldResult entered = world.TryEnterWorld(Builder, hero);
        entered.Status.Should().Be(OpStatus.Ok);
        return (world, new Vector2(entered.PositionX, entered.PositionY));
    }

    /// <summary>The first tile (scanning outwards) whose centre is between <paramref name="min"/> and <paramref name="max"/> tiles away.</summary>
    private static (int X, int Y) TileAtDistance(Vector2 from, float min, float max)
    {
        int px = (int)MathF.Floor(from.X / TileSize);
        int py = (int)MathF.Floor(from.Y / TileSize);
        for (int dy = -8; dy <= 8; dy++)
        {
            for (int dx = 0; dx <= 8; dx++)
            {
                Vector2 centre = new((px + dx + 0.5f) * TileSize, (py + dy + 0.5f) * TileSize);
                float tiles = Vector2.Distance(from, centre) / TileSize;
                if (tiles > min && tiles < max)
                {
                    return (px + dx, py + dy);
                }
            }
        }

        throw new InvalidOperationException($"No tile between {min} and {max} tiles away.");
    }

    private static string ShippedStructuresJson() =>
        System.IO.File.ReadAllText(System.IO.Path.Combine(FindRepositoryRoot(), "resources", "config", "structures.json"));

    private static string FindRepositoryRoot()
    {
        System.IO.DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
