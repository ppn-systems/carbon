// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Maps.S2C;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Nalix.Abstractions.Primitives;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-040 through the authoritative world: felled trees come back after their <c>props.json</c> delay, mined stone and
/// ore never do, spawn forage comes back fast, a pending regrowth survives a server restart, and nothing grows into a
/// player standing on the tile.
/// </summary>
public sealed class PropRegrowthWorldTests
{
    private const ulong Seed = 1337UL;
    private const ulong Conn = 951UL;
    private const int TickRate = 30;
    private const float TileSize = 32f;
    private const int TreeSeconds = 20;
    private const int ForageSeconds = 5;

    private static readonly PropRegrowthRules s_rules = PropRegrowthRules.LoadFromJson(
        $$"""
        {
          "spawnForage": { "respawnSeconds": {{ForageSeconds}} },
          "blockedRetrySeconds": 2,
          "regrowSeconds": { {{string.Join(", ", Enum.GetValues<MapPropType>().Where(MapProp.IsTree).Select(t => $"\"{t}\": {TreeSeconds}"))}} }
        }
        """);

    [Fact]
    public async Task TryHarvestProp_TreeFelled_ComesBackOnlyAfterItsConfiguredDelay()
    {
        (int x, int y) = FindProp(p => MapProp.IsTree(p.Type) && p.Destructibility.HarvestTier <= 1, outsideClearing: true);
        using Fixture f = await Fixture.CreateAsync(x, y, tool: ItemSlot.Empty).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.Step((TreeSeconds - 2) * TickRate);
        f.IsDestroyed(x, y).Should().BeTrue("the tree is still down before its regrowth delay");

        f.Step(4 * TickRate);
        f.IsDestroyed(x, y).Should().BeFalse("the tree stands again once its props.json delay has passed");
    }

    [Fact]
    public async Task TryHarvestProp_BoulderMined_NeverComesBack()
    {
        (int x, int y) = FindProp(p => p.Type == MapPropType.Boulder, outsideClearing: true);
        using Fixture f = await Fixture.CreateAsync(x, y, tool: new ItemSlot(StarterGameCatalog.CrystalPickaxe, 1, 0, 500f)).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.Step(10 * TreeSeconds * TickRate);

        f.IsDestroyed(x, y).Should().BeTrue("stone and ore never come back (ADR-040 D2)");
    }

    [Fact]
    public async Task TryHarvestProp_StarterForageNearSpawn_ComesBackAfterTheFastForageDelay()
    {
        (int x, int y) = FindProp(p => p.Type == MapPropType.LooseStones, outsideClearing: false);
        using Fixture f = await Fixture.CreateAsync(x, y, tool: ItemSlot.Empty).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.IsDestroyed(x, y).Should().BeTrue();
        f.Step((ForageSeconds + 2) * TickRate);

        f.IsDestroyed(x, y).Should().BeFalse("a new player can always gather loose stones at spawn (ADR-040 D3)");
    }

    [Fact]
    public async Task InitializeFromStoreAsync_ServerRestartsWhileATreeRegrows_TheTreeStillComesBack()
    {
        (int x, int y) = FindProp(p => MapProp.IsTree(p.Type) && p.Destructibility.HarvestTier <= 1, outsideClearing: true);
        using Fixture f = await Fixture.CreateAsync(x, y, tool: ItemSlot.Empty).ConfigureAwait(true);
        f.StrikeUntilGone(x, y);
        await f.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);

        AuthoritativeWorld restarted = f.Restart();
        await restarted.InitializeFromStoreAsync().ConfigureAwait(true);
        Fixture.IsDestroyed(restarted, x, y).Should().BeTrue("the felled tree is still down after the restart");
        for (int i = 0; i < (TreeSeconds + 2) * TickRate; i++)
        {
            restarted.StepFixedTick();
        }

        Fixture.IsDestroyed(restarted, x, y).Should().BeFalse("the regrowth timer was saved with the chunk and survived the restart");
    }

    [Fact]
    public async Task ProcessPropRespawns_PlayerStandsOnTheTile_ForageWaitsForThem()
    {
        (int x, int y) = FindProp(p => p.Type == MapPropType.DryBush, outsideClearing: false);
        using Fixture f = await Fixture.CreateAsync(x, y, tool: ItemSlot.Empty, standOnTile: true).ConfigureAwait(true);

        f.StrikeUntilGone(x, y);
        f.Step((ForageSeconds + 4) * TickRate);

        f.IsDestroyed(x, y).Should().BeTrue("nothing grows into a player standing on the tile");
    }

    private static (int X, int Y) FindProp(Func<MapProp, bool> match, bool outsideClearing)
    {
        ChunkWorld2D mirror = new(new ProceduralWorldGenerator(Seed), TileSize, 0.0008f);
        (int sx, int sy) = mirror.SpawnTile;
        for (int r = 0; r <= 80; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = sx + dx;
                    int y = sy + dy;
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r &&
                        SpawnClearingStage.IsInClearing((sx, sy), x, y) != outsideClearing &&
                        mirror.TryGetPropAt(x, y, out MapProp prop) && prop.Destructibility.IsDestructible && match(prop))
                    {
                        return (x, y);
                    }
                }
            }
        }

        throw new InvalidOperationException("the test seed has no such prop near spawn");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;

        private Fixture(string dbPath, SqliteConnectionFactory factory, AuthoritativeWorld world)
        {
            _dbPath = dbPath;
            _factory = factory;
            this.World = world;
        }

        public AuthoritativeWorld World { get; }

        public static async Task<Fixture> CreateAsync(int propX, int propY, ItemSlot tool, bool standOnTile = false)
        {
            string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-regrowth-{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(false);
            SqliteAccountStore accounts = new(factory);
            SqliteCharacterStore characters = new(factory);
            DateTimeOffset now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
            Guid accountId = Guid.NewGuid();
            _ = await accounts.TryCreateAsync(new AccountRecord(accountId, "grower", "GROWER", Bytes32.Zero, [2], now, false)).ConfigureAwait(false);

            AuthoritativeWorld world = CreateWorld(factory);
            await world.InitializeFromStoreAsync().ConfigureAwait(false);

            InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
            if (!tool.IsEmpty)
            {
                inv.SetSlot(0, in tool);
            }

            CharacterRecord hero = new(
                Guid.NewGuid(),
                accountId,
                "Grower",
                Level: 1,
                AppearancePreset: 0,
                PositionX: ((propX + 0.5f) * TileSize) + (standOnTile ? 0f : 40f),
                PositionY: (propY + 0.5f) * TileSize,
                CurrentHealth: 100f,
                MaxHealth: 100f,
                SelectedHotbarSlot: 0,
                InventoryBlob: InventoryBinaryCodec.Serialize(inv),
                UpdatedAtUtc: now,
                Flags: CharacterTraits.StarterLoadoutGranted);
            _ = await characters.TryCreateAsync(hero).ConfigureAwait(false);
            world.TryEnterWorld(Conn, hero).Status.Should().Be(OpStatus.Ok);
            return new Fixture(dbPath, factory, world);
        }

        public static bool IsDestroyed(AuthoritativeWorld world, int x, int y)
        {
            // No packet: the chunk has no recorded changes, so the prop stands as generated.
            MapChunkDelta? packet = null;
            try
            {
                if (!world.TryCreateChunkDeltaPacket(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), 0U, out packet) || packet is null)
                {
                    return false;
                }

                int local = (Chunk2D.ToLocalCoord(y) * Chunk2D.Size) + Chunk2D.ToLocalCoord(x);
                int index = Array.IndexOf(packet.PropIndices, (ushort)local);
                return index >= 0 && packet.PropDestroyed[index];
            }
            finally
            {
                packet?.Dispose();
            }
        }

        public bool IsDestroyed(int x, int y) => IsDestroyed(this.World, x, y);

        public AuthoritativeWorld Restart() => CreateWorld(_factory);

        public void StrikeUntilGone(int x, int y)
        {
            for (int attempt = 0; attempt < 3000 && !this.IsDestroyed(x, y); attempt++)
            {
                OpStatus status = this.World.TryHarvestProp(Conn, x, y).Status;
                status.Should().BeOneOf([OpStatus.Ok, OpStatus.RateLimited], $"the prop at ({x},{y}) does not resist the strike");
                this.World.StepFixedTick();
            }

            this.IsDestroyed(x, y).Should().BeTrue($"the prop at ({x},{y}) breaks");
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                this.World.StepFixedTick();
            }
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

        private static AuthoritativeWorld CreateWorld(SqliteConnectionFactory factory) =>
            new(
                Seed,
                TileSize,
                0.0008f,
                TickRate,
                new SqliteChunkDeltaRepository(factory),
                new SqliteCharacterStore(factory),
                new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
                entropy: TestGameplayEntropy.Create(Seed),
                propRegrowth: s_rules);
    }
}
