// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
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

namespace Carbon.Host.Tests;

/// <summary>
/// An <see cref="AuthoritativeWorld"/> on a throwaway SQLite store with one hero next to a prop, for the ADR-040 regrowth
/// tests: strike the prop, step the clock, read its replicated state and restart the server on the same store.
/// </summary>
internal sealed class RegrowthWorldFixture : IDisposable
{
    public const ulong Seed = 1337UL;
    public const ulong Conn = 951UL;
    public const int TickRate = 30;
    public const float TileSize = 32f;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly PropRegrowthRules _rules;
    private readonly Dictionary<uint, int> _baseline = [];

    private RegrowthWorldFixture(string dbPath, SqliteConnectionFactory factory, PropRegrowthRules rules, AuthoritativeWorld world, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink)
    {
        _dbPath = dbPath;
        _factory = factory;
        _rules = rules;
        this.World = world;
        this.Sink = sink;
    }

    public AuthoritativeWorld World { get; }

    public AuthoritativeWorldMultiplayerTests.RecordingOutboundSink Sink { get; }

    /// <summary>Creates the world and enters a hero 40 px east of the prop (or on its tile) with <paramref name="tool"/> held.</summary>
    public static async Task<RegrowthWorldFixture> CreateAsync(
        PropRegrowthRules rules,
        int propX,
        int propY,
        ItemSlot tool,
        bool standOnTile = false,
        float stamina = 100f)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-regrowth-{Guid.NewGuid():N}.db");
        SqliteConnectionFactory factory = new($"Data Source={dbPath}");
        await new SqliteSchemaMigrator(factory).MigrateAsync().ConfigureAwait(false);
        SqliteAccountStore accounts = new(factory);
        SqliteCharacterStore characters = new(factory);
        DateTimeOffset now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        Guid accountId = Guid.NewGuid();
        _ = await accounts.TryCreateAsync(new AccountRecord(accountId, "grower", "GROWER", Bytes32.Zero, [2], now, false)).ConfigureAwait(false);

        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        AuthoritativeWorld world = CreateWorld(factory, rules, sink);
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
            CurrentStamina: stamina,
            Flags: CharacterTraits.StarterLoadoutGranted);
        _ = await characters.TryCreateAsync(hero).ConfigureAwait(false);
        world.TryEnterWorld(Conn, hero).Status.Should().Be(OpStatus.Ok);
        return new RegrowthWorldFixture(dbPath, factory, rules, world, sink);
    }

    /// <summary>Builds a <c>props.json</c> with short delays for tests; trees grow stump 6 s, sapling 4 s, young 20 s.</summary>
    public static PropRegrowthRules Rules(int forageSeconds = 5, int deferWithinTiles = 0) =>
        PropRegrowthRules.LoadFromJson(
            $$"""
            {
              "spawnForage": { "respawnSeconds": {{forageSeconds}} },
              "blockedRetrySeconds": 2,
              "regrowSeconds": { "bush": 600 },
              "trees": {
                "deferWithinTiles": {{deferWithinTiles}},
                "stump": { "seconds": 6, "durability": 30, "yield": 1, "saplingChance": 0.5 },
                "sapling": { "seconds": 4, "durability": 10, "yield": 0 },
                "young": { "seconds": 20, "durabilityShare": 0.5, "yield": 2 },
                "mature": { "yield": 3, "saplingDrops": { "min": 0, "max": 2 } },
                "dig": { "bareHandDamage": 4, "axeEfficiency": 0.5 }
              }
            }
            """);

    /// <summary>Finds a destructible prop near the spawn, inside or outside the spawn clearing.</summary>
    public static (int X, int Y) FindProp(Func<MapProp, bool> match, bool outsideClearing)
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

    /// <summary>Reads the replicated state of the prop on a tile: destroyed and growth stage (mature when unchanged).</summary>
    public static (bool Destroyed, TreeGrowthStage Stage) Probe(AuthoritativeWorld world, int x, int y)
    {
        // No packet: the chunk has no recorded changes, so the prop stands as generated.
        MapChunkDelta? packet = null;
        try
        {
            if (!world.TryCreateChunkDeltaPacket(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), 0U, out packet) || packet is null)
            {
                return (false, TreeGrowthStage.Mature);
            }

            int local = (Chunk2D.ToLocalCoord(y) * Chunk2D.Size) + Chunk2D.ToLocalCoord(x);
            int index = Array.IndexOf(packet.PropIndices, (ushort)local);
            return index < 0 ? (false, TreeGrowthStage.Mature) : (packet.PropDestroyed[index], (TreeGrowthStage)packet.PropGrowthStages[index]);
        }
        finally
        {
            packet?.Dispose();
        }
    }

    public static bool IsDestroyed(AuthoritativeWorld world, int x, int y) => Probe(world, x, y).Destroyed;

    public (bool Destroyed, TreeGrowthStage Stage) Probe(int x, int y) => Probe(this.World, x, y);

    public bool IsDestroyed(int x, int y) => IsDestroyed(this.World, x, y);

    public AuthoritativeWorld Restart() => CreateWorld(_factory, _rules, new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink());

    /// <summary>Strikes the prop until <paramref name="done"/> holds; returns the strikes that landed.</summary>
    public int StrikeUntil(int x, int y, Func<(bool Destroyed, TreeGrowthStage Stage), bool> done)
    {
        int landed = 0;
        for (int attempt = 0; attempt < 4000 && !done(this.Probe(x, y)); attempt++)
        {
            ulong tick = this.World.CurrentTick;
            OpStatus status = this.World.TryHarvestProp(Conn, x, y).Status;
            status.Should().BeOneOf([OpStatus.Ok, OpStatus.RateLimited], $"the prop at ({x},{y}) does not resist the strike");
            if (status == OpStatus.Ok)
            {
                landed++;
                this.LastStrikeTick = tick;
            }

            this.World.StepFixedTick();
        }

        done(this.Probe(x, y)).Should().BeTrue($"the prop at ({x},{y}) reaches the expected state");
        return landed;
    }

    /// <summary>Gets the world tick of the last strike that landed (the tick its drops were rolled on).</summary>
    public ulong LastStrikeTick { get; private set; }

    public void StrikeUntilGone(int x, int y) => _ = this.StrikeUntil(x, y, p => p.Destroyed);

    public void Step(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            this.World.StepFixedTick();
        }
    }

    /// <summary>
    /// Gets how much of an item lies on the ground now beyond what lay there at the last <see cref="ClearDrops"/>. A
    /// drop that merges into a stack already there re-sends that stack's snapshot, so each stack counts at its latest size.
    /// </summary>
    public int DroppedQuantity(uint itemId) => this.GroundQuantity(itemId) - _baseline.GetValueOrDefault(itemId);

    /// <summary>Takes the ground as it is now as the baseline of <see cref="DroppedQuantity"/>.</summary>
    public void ClearDrops()
    {
        _baseline.Clear();
        foreach ((ulong _, EntitySpawnSnapshot snapshot) in this.Sink.Spawns)
        {
            if (snapshot.Kind == EntityKind.LootDrop)
            {
                _baseline[snapshot.ArchetypeId] = this.GroundQuantity(snapshot.ArchetypeId);
            }
        }
    }

    private int GroundQuantity(uint itemId) =>
        this.Sink.Spawns
            .Where(s => s.ConnectionId == Conn && s.Snapshot.Kind == EntityKind.LootDrop && s.Snapshot.ArchetypeId == itemId)
            .GroupBy(s => s.Snapshot.NetworkId)
            .Sum(g => g.Last().Snapshot.LootCount);

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static AuthoritativeWorld CreateWorld(SqliteConnectionFactory factory, PropRegrowthRules rules, AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink) =>
        new(
            Seed,
            TileSize,
            0.0008f,
            TickRate,
            new SqliteChunkDeltaRepository(factory),
            new SqliteCharacterStore(factory),
            sink,
            entropy: TestGameplayEntropy.Create(Seed),
            propRegrowth: rules);
}
