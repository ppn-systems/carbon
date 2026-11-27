// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-033: the world tick validates sprint, tool swings and build work against stamina.</summary>
public sealed class StaminaWorldTests
{
    private const ulong Seed = 1337UL;
    private const ulong ConnectionId = 933UL;
    private const int TicksPerSecond = 30;

    [Fact]
    public async Task StepFixedTick_SprintingOneSecond_DrainsExactlyTheConfiguredRate()
    {
        using StaminaWorld f = await StaminaWorld.CreateAsync(stamina: 100f).ConfigureAwait(true);

        f.Sprint(TicksPerSecond);

        f.Stamina().Should().BeApproximately(100f - StaminaTestConfig.SprintDrainPerSecond, 0.05f);
    }

    [Fact]
    public async Task StepFixedTick_SprintedEmpty_ExhaustedBlocksSprintUntilTheRecoverFraction()
    {
        using StaminaWorld f = await StaminaWorld.CreateAsync(stamina: 15f).ConfigureAwait(true);

        f.Sprint(TicksPerSecond + 2); // 15 stamina at 15/s
        f.Stamina().Should().Be(0f);
        f.Sink.StaminaStatuses.Should().ContainSingle(s => s.ConnectionId == ConnectionId && s.Exhausted);

        // Sprint stays held: the exhausted body walks and regenerates instead of draining.
        f.Sprint(3 * TicksPerSecond);
        float afterHeldSprint = f.Stamina();
        afterHeldSprint.Should().BeGreaterThan(0f, "an exhausted player cannot sprint, so holding the key regenerates");

        float threshold = 100f * StaminaTestConfig.ExhaustedRecoverFraction;
        for (int i = 0; i < 60 * TicksPerSecond && f.Sink.StaminaStatuses.Count < 2; i++)
        {
            f.Sprint(1);
        }

        f.Sink.StaminaStatuses.Should().HaveCount(2);
        f.Sink.StaminaStatuses[1].Exhausted.Should().BeFalse();
        float recovered = f.Stamina();
        recovered.Should().BeGreaterThanOrEqualTo(threshold, "exhaustion ends only at the recover fraction");

        f.Sprint(TicksPerSecond);
        f.Stamina().Should().BeLessThan(recovered, "sprint drains again once recovered");
    }

    [Fact]
    public async Task TryHarvestProp_WithEnoughStamina_DebitsExactlyTheToolCost()
    {
        using StaminaWorld f = await StaminaWorld.CreateAsync(stamina: 100f).ConfigureAwait(true);

        f.World.TryHarvestProp(ConnectionId, f.PropTileX, f.PropTileY).Status.Should().Be(OpStatus.Ok);

        f.Stamina().Should().Be(100f - StaminaTestConfig.WoodAxeCost);
        f.Sink.PropChanges.Should().Contain(p => p.TileX == f.PropTileX && p.TileY == f.PropTileY);
    }

    [Fact]
    public async Task TryHarvestProp_WithoutEnoughStamina_IsRefusedAndTheTreeIsUntouched()
    {
        float low = StaminaTestConfig.WoodAxeCost - 1f;
        using StaminaWorld f = await StaminaWorld.CreateAsync(stamina: low).ConfigureAwait(true);

        f.World.TryHarvestProp(ConnectionId, f.PropTileX, f.PropTileY).Status.Should().Be(OpStatus.RateLimited);

        f.Sink.PropChanges.Should().BeEmpty("a refused swing does not damage the prop");
        f.Stamina().Should().Be(low);
        f.World.StepFixedTick();
        f.Sink.StaminaStatuses.Should().ContainSingle().Which.Should().Be((ConnectionId, true, 1U), "the client is told to flash the bar");
    }

    [Fact]
    public async Task StepFixedTick_OneSecondOfBuildWork_DebitsExactlyTheBuildCost()
    {
        using StaminaWorld f = await StaminaWorld.CreateAsync(stamina: 100f).ConfigureAwait(true);
        (int tx, int ty) = f.PlaceWall();

        f.Step(TicksPerSecond); // the 120-work wall needs two seconds

        f.Stamina().Should().BeApproximately(100f - StaminaTestConfig.BuildWorkPerSecond, 0.05f);
        f.Sink.StructureChanges.Should().NotContain(c => c.TileX == tx && c.TileY == ty);
    }

    [Fact]
    public async Task StepFixedTick_BuildingWhileExhausted_PausesTheJob()
    {
        using StaminaWorld withStamina = await StaminaWorld.CreateAsync(stamina: 100f).ConfigureAwait(true);
        (int rtx, int rty) = withStamina.PlaceWall();
        withStamina.Step(70);
        withStamina.Sink.StructureChanges.Should().Contain(c => c.TileX == rtx && c.TileY == rty, "control: a rested builder finishes in 60 ticks");

        using StaminaWorld f = await StaminaWorld.CreateAsync(stamina: 0f).ConfigureAwait(true);
        (int tx, int ty) = f.PlaceWall();

        f.Step(70);

        f.Sink.StructureChanges.Should().NotContain(c => c.TileX == tx && c.TileY == ty, "an exhausted builder does no work");
    }

    private sealed class StaminaWorld : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;
        private readonly Guid _heroId;
        private uint _clientTick;

        private StaminaWorld(string dbPath, SqliteConnectionFactory factory, AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink, Guid heroId, int propTileX, int propTileY)
        {
            _dbPath = dbPath;
            _factory = factory;
            _heroId = heroId;
            this.World = world;
            this.Sink = sink;
            this.PropTileX = propTileX;
            this.PropTileY = propTileY;
        }

        public AuthoritativeWorld World { get; }

        public AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink { get; }

        public int PropTileX { get; }

        public int PropTileY { get; }

        public static async Task<StaminaWorld> CreateAsync(float stamina)
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-stamina-{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(false);
            AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
            AuthoritativeWorld world = new(
                Seed,
                32f,
                0.0008f,
                TicksPerSecond,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                sink,
                time: new FixedTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero)),
                entropy: TestGameplayEntropy.Create(Seed),
                stamina: StaminaTestConfig.Create());
            await world.InitializeFromStoreAsync().ConfigureAwait(false);

            (int tx, int ty) = FindChoppableProp(world);
            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            inv.SetSlot(0, new ItemSlot(StarterGameCatalog.WoodAxe, 1, 0, 120f));
            inv.SetSlot(1, new ItemSlot(StarterGameCatalog.WoodLog, 12));
            CharacterRecord hero = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Stamina",
                Level: 1,
                AppearancePreset: 0,
                PositionX: ((tx + 0.5f) * 32f) + 40f,
                PositionY: (ty + 0.5f) * 32f,
                CurrentHealth: 100f,
                MaxHealth: 100f,
                SelectedHotbarSlot: 0,
                InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero),
                CurrentStamina: stamina);
            world.TryEnterWorld(ConnectionId, hero).Status.Should().Be(OpStatus.Ok);
            return new StaminaWorld(dbPath, factory, world, sink, hero.Id, tx, ty);
        }

        public float Stamina()
        {
            this.World.TryGetPendingCharacterSnapshot(_heroId, out CharacterRecord? snapshot).Should().BeTrue();
            return snapshot!.CurrentStamina;
        }

        public void Sprint(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                this.World.EnqueueMoveInput(ConnectionId, ++_clientTick, new Vector2(i % 60 < 30 ? 1f : -1f, 0f), sprint: true);
                this.World.StepFixedTick();
            }
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                this.World.StepFixedTick();
            }
        }

        /// <summary>Places a wood wall blueprint (3 logs delivered) on a free tile next to the hero.</summary>
        public (int TileX, int TileY) PlaceWall()
        {
            this.World.TryGetPendingCharacterSnapshot(_heroId, out CharacterRecord? hero).Should().BeTrue();
            int px = (int)MathF.Floor(hero!.PositionX / 32f);
            int py = (int)MathF.Floor(hero.PositionY / 32f);
            foreach ((int dx, int dy) in new[] { (0, 2), (0, -2), (2, 0), (2, 2), (2, -2), (-1, 2), (-1, -2), (1, 2), (1, -2) })
            {
                if (this.World.TryPlaceStructure(ConnectionId, px + dx, py + dy, StructureType.WoodWall).Status == OpStatus.Ok)
                {
                    return (px + dx, py + dy);
                }
            }

            throw new InvalidOperationException("No free tile for the wall near the hero.");
        }

        public void Dispose()
        {
            _factory.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        /// <summary>Mirrors the deterministic generator to find flora near spawn that a wood axe can chop.</summary>
        private static (int X, int Y) FindChoppableProp(AuthoritativeWorld world)
        {
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), 32f, 0.0008f);
            int sx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f);
            int sy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            for (int r = 0; r <= 64; r++)
            {
                foreach ((int dx, int dy) in Ring(r))
                {
                    if (mirror.TryGetPropAt(sx + dx, sy + dy, out MapProp prop) &&
                        prop.Category != PropCategory.Mineral &&
                        prop.Destructibility.IsDestructible &&
                        !prop.Destructibility.IsDestroyed &&
                        prop.Destructibility.HarvestTier <= 1)
                    {
                        return (sx + dx, sy + dy);
                    }
                }
            }

            throw new InvalidOperationException("No choppable prop near spawn for the test seed.");
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private static (int Dx, int Dy)[] Ring(int r) =>
            [.. Enumerable.Range(-r, (2 * r) + 1)
                .SelectMany(dy => Enumerable.Range(-r, (2 * r) + 1).Select(dx => (dx, dy)))
                .Where(p => Math.Max(Math.Abs(p.dx), Math.Abs(p.dy)) == r)];
    }
}
