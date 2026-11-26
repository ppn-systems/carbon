// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-017: map mutations submitted by handlers run inside the next fixed tick.</summary>
public sealed class TickQueuedWorldCommandTests
{
    private const ulong Seed = 1337UL;
    private const ulong ConnectionId = 901UL;

    [Fact]
    public async Task SubmitHarvestPropAsync_BeforeTick_CompletesOnlyAfterStepFixedTick()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);

        Task<WorldMutationOutcome> pending = f.World.SubmitHarvestPropAsync(ConnectionId, f.PropTileX, f.PropTileY).AsTask();

        pending.IsCompleted.Should().BeFalse("the command waits for the tick instead of mutating on the caller's thread");

        f.World.StepFixedTick();
        WorldMutationOutcome outcome = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

        outcome.Status.Should().NotBe(OpStatus.RateLimited);
        outcome.Status.Should().NotBe(OpStatus.OutOfRange, "the hero stands next to the prop");
        f.World.TryHarvestProp(ConnectionId, f.PropTileX, f.PropTileY).Status
            .Should().Be(OpStatus.RateLimited, "the queued strike ran in the tick and started the shared attack cooldown");
    }

    [Fact]
    public async Task SubmitToggleDoorAsync_MoreThanPerConnectionLimit_RejectsExtraWithRateLimited()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);

        Task<WorldMutationOutcome>[] accepted = new Task<WorldMutationOutcome>[16];
        for (int i = 0; i < accepted.Length; i++)
        {
            accepted[i] = f.World.SubmitToggleDoorAsync(ConnectionId, f.PropTileX, f.PropTileY).AsTask();
        }

        Task<WorldMutationOutcome> overflow = f.World.SubmitToggleDoorAsync(ConnectionId, f.PropTileX, f.PropTileY).AsTask();

        overflow.IsCompleted.Should().BeTrue("the 17th command is rejected without queueing");
        (await overflow.ConfigureAwait(true)).Status.Should().Be(OpStatus.RateLimited);
        accepted.Should().OnlyContain(t => !t.IsCompleted);

        f.World.StepFixedTick();
        foreach (Task<WorldMutationOutcome> task in accepted)
        {
            (await task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true)).Status.Should().NotBe(OpStatus.RateLimited);
        }

        Task<WorldMutationOutcome> afterDrain = f.World.SubmitToggleDoorAsync(ConnectionId, f.PropTileX, f.PropTileY).AsTask();
        afterDrain.IsCompleted.Should().BeFalse("the drained commands released their per-connection slots");
        f.World.StepFixedTick();
        _ = await afterDrain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SubmitSelectHotbarSlotAsync_BeforeTick_AppliesOnlyAfterStepFixedTick()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);

        Task<WorldMutationOutcome> pending = f.World.SubmitSelectHotbarSlotAsync(ConnectionId, 4).AsTask();

        pending.IsCompleted.Should().BeFalse();
        f.World.TryGetPendingCharacterSnapshot(f.HeroId, out CharacterRecord? before).Should().BeTrue();
        before!.SelectedHotbarSlot.Should().Be(0, "nothing changes until the tick processes the command");

        f.World.StepFixedTick();

        (await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
        f.World.TryGetPendingCharacterSnapshot(f.HeroId, out CharacterRecord? after).Should().BeTrue();
        after!.SelectedHotbarSlot.Should().Be(4);
    }

    [Fact]
    public async Task SubmitAllocateCharacterPointAsync_BeforeTick_CompletesOnlyAfterStepFixedTick()
    {
        // Level 3 has unspent attribute points to allocate.
        using WorldFixture f = await WorldFixture.CreateAsync(level: 3).ConfigureAwait(true);

        Task<WorldMutationOutcome> pending = f.World
            .SubmitAllocateCharacterPointAsync(ConnectionId, PointAllocationKind.Attribute, (ushort)PrimaryAttributeId.Vitality)
            .AsTask();

        pending.IsCompleted.Should().BeFalse("point allocation now runs inside the tick (ADR-017 part 3)");

        f.World.StepFixedTick();

        (await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true)).Status.Should().Be(OpStatus.Ok);
    }

    [Fact]
    public async Task FailPendingCommands_WithQueuedCommand_CompletesItWithConflict()
    {
        using WorldFixture f = await WorldFixture.CreateAsync().ConfigureAwait(true);
        Task<WorldMutationOutcome> pending = f.World.SubmitDemolishStructureAsync(ConnectionId, f.PropTileX, f.PropTileY).AsTask();

        f.World.FailPendingCommands();

        (await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true)).Status.Should().Be(OpStatus.Conflict);
    }

    private sealed class WorldFixture : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;

        private WorldFixture(string dbPath, SqliteConnectionFactory factory, AuthoritativeWorld world, Guid heroId, int propTileX, int propTileY)
        {
            _dbPath = dbPath;
            _factory = factory;
            this.World = world;
            this.HeroId = heroId;
            this.PropTileX = propTileX;
            this.PropTileY = propTileY;
        }

        public AuthoritativeWorld World { get; }

        public Guid HeroId { get; }

        public int PropTileX { get; }

        public int PropTileY { get; }

        public static async Task<WorldFixture> CreateAsync(ushort level = 1)
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-tick-commands-{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(false);

            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            DateTimeOffset now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
            Guid accountId = Guid.NewGuid();
            _ = await accounts.TryCreateAsync(new AccountRecord(accountId, "queuer", "QUEUER", [1], [2], now, false)).ConfigureAwait(false);

            AuthoritativeWorld world = new(Seed, 32f, 0.0008f, 30, new SqliteChunkDeltaRepository(factory), characters, new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink());
            await world.InitializeFromStoreAsync().ConfigureAwait(false);

            (int tx, int ty) = FindDestructibleProp(world);
            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            StarterGameCatalog.GrantStarterLoadout(inv, startingClass: 0);
            CharacterRecord hero = new(
                Guid.NewGuid(),
                accountId,
                "Queuer",
                Level: level,
                AppearancePreset: 0,
                PositionX: ((tx + 0.5f) * 32f) + 40f,
                PositionY: (ty + 0.5f) * 32f,
                CurrentHealth: 100f,
                MaxHealth: 100f,
                SelectedHotbarSlot: 0,
                InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: now,
                Flags: CharacterTraits.StarterLoadoutGranted);
            _ = await characters.TryCreateAsync(hero).ConfigureAwait(false);
            world.TryEnterWorld(ConnectionId, ConnectionId, hero).Status.Should().Be(OpStatus.Ok);

            return new WorldFixture(dbPath, factory, world, hero.Id, tx, ty);
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

        private static (int X, int Y) FindDestructibleProp(AuthoritativeWorld world)
        {
            // Mirror the server's deterministic generator to find a destructible prop near spawn.
            ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), 32f, 0.0008f);
            int sx = (int)MathF.Floor(world.DefaultSpawnWorldPosition.X / 32f);
            int sy = (int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / 32f);
            for (int r = 0; r <= 64; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r &&
                            mirror.TryGetPropAt(sx + dx, sy + dy, out MapProp prop) &&
                            prop.Destructibility.IsDestructible && !prop.Destructibility.IsDestroyed)
                        {
                            return (sx + dx, sy + dy);
                        }
                    }
                }
            }

            throw new InvalidOperationException("No destructible prop near spawn for the test seed.");
        }
    }
}
