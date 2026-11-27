// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Testing;
using Nalix.Abstractions.Primitives;
using Microsoft.Data.Sqlite;

namespace Carbon.Host.Tests;

/// <summary>
/// Steps a real <see cref="AuthoritativeWorld"/> (players + the monsters that populate around them) with a no-op
/// outbound sink and measures what one fixed tick costs: managed bytes allocated on the tick thread and wall time.
/// Used by the steady-state allocation guard and as a before/after harness for perf work (audit round 2, PERF-*).
/// </summary>
internal sealed class TickAllocationHarness : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ulong[] _connections;
    private uint _clientTick;

    private TickAllocationHarness(string dbPath, SqliteConnectionFactory factory, AuthoritativeWorld world, SqliteCharacterStore characters, ulong[] connections)
    {
        _dbPath = dbPath;
        _factory = factory;
        this.World = world;
        this.Characters = characters;
        _connections = connections;
    }

    public AuthoritativeWorld World { get; }

    /// <summary>Gets the character store the world saves to.</summary>
    public SqliteCharacterStore Characters { get; }

    /// <summary>Gets the ids of the entered characters, in entry order.</summary>
    public List<Guid> CharacterIds { get; } = [];

    /// <summary>Creates a world on a temporary SQLite file and enters <paramref name="players"/> characters near spawn.</summary>
    /// <param name="players">Characters to enter near spawn.</param>
    /// <param name="sink">Outbound sink to observe; a no-op sink when <see langword="null"/>.</param>
    /// <param name="overrides">Developer overrides applied before anyone enters (for example status effects on entry).</param>
    public static TickAllocationHarness Create(int players, IWorldOutboundSink? sink = null, WorldDebugOverrides? overrides = null)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-tick-alloc-{Guid.NewGuid():N}.db");
        SqliteConnectionFactory factory = new($"Data Source={dbPath}");
        new SqliteSchemaMigrator(factory).MigrateAsync().GetAwaiter().GetResult();
        SqliteCharacterStore characters = new(factory);
        SqliteAccountStore accounts = new(factory);
        AuthoritativeWorld world = new(
            1337UL,
            32f,
            0.0008f,
            30,
            new SqliteChunkDeltaRepository(factory),
            characters,
            sink ?? new NullOutboundSink(),
            entropy: TestGameplayEntropy.Create(1337UL));
        world.InitializeFromStoreAsync().GetAwaiter().GetResult();
        if (overrides is not null)
        {
            _ = world.ApplyDebugOverrides(overrides);
        }

        TickAllocationHarness harness = new(dbPath, factory, world, characters, new ulong[players]);

        ulong[] connections = harness._connections;
        DateTimeOffset createdAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < players; i++)
        {
            // A 4-wide grid two tiles apart, so every player sees the others and the same monsters.
            Vector2 position = world.DefaultSpawnWorldPosition + (new Vector2(i % 4, i / 4) * 64f);
            Guid accountId = Guid.NewGuid();
            CharacterRecord hero = new(Guid.NewGuid(), accountId, $"Bench{i}", 1, default, position.X, position.Y, 100f, 100f, 0, [], createdAt);
            _ = accounts.TryCreateAsync(new AccountRecord(accountId, $"bench{i}", $"BENCH{i}", Bytes32.Zero, [1], createdAt, false)).GetAwaiter().GetResult();
            _ = characters.TryCreateAsync(hero).GetAwaiter().GetResult();
            harness.CharacterIds.Add(hero.Id);
            connections[i] = 1_000UL + (ulong)i;
            OpStatus status = world.TryEnterWorld(connections[i], hero).Status;
            if (status != OpStatus.Ok)
            {
                throw new InvalidOperationException($"player {i} could not enter the world: {status}");
            }
        }

        return harness;
    }

    /// <summary>Feeds every player a move input that walks a small circle (they stay inside loaded chunks), then ticks.</summary>
    public void Step()
    {
        _clientTick++;
        float angle = _clientTick * 0.05f;
        for (int i = 0; i < _connections.Length; i++)
        {
            Vector2 direction = new(MathF.Cos(angle + i), MathF.Sin(angle + i));
            this.World.EnqueueMoveInput(_connections[i], _clientTick, direction, sprint: false);
        }

        this.World.StepFixedTick();
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks twice through the shared probe (warm the world up first) and returns the first
    /// window; <see cref="AllocationResult.BytesPerIteration"/> is the bytes per tick.
    /// </summary>
    public AllocationResult Measure(int ticks) => AllocationProbe.Measure(ticks, warmupIterations: 0, i => this.Step());

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
