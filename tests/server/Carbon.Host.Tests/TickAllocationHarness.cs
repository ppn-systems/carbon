// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Infrastructure.Persistence.Sqlite;
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

    private TickAllocationHarness(string dbPath, SqliteConnectionFactory factory, AuthoritativeWorld world, ulong[] connections)
    {
        _dbPath = dbPath;
        _factory = factory;
        this.World = world;
        _connections = connections;
    }

    public AuthoritativeWorld World { get; }

    /// <summary>Creates a world on a temporary SQLite file and enters <paramref name="players"/> characters near spawn.</summary>
    /// <param name="players">Characters to enter near spawn.</param>
    /// <param name="sink">Outbound sink to observe; a no-op sink when <see langword="null"/>.</param>
    public static TickAllocationHarness Create(int players, IWorldOutboundSink? sink = null)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-tick-alloc-{Guid.NewGuid():N}.db");
        SqliteConnectionFactory factory = new($"Data Source={dbPath}");
        new SqliteSchemaMigrator(factory).MigrateAsync().GetAwaiter().GetResult();
        AuthoritativeWorld world = new(
            1337UL,
            32f,
            0.0008f,
            30,
            new SqliteChunkDeltaRepository(factory),
            new SqliteCharacterStore(factory),
            sink ?? new NullOutboundSink(),
            entropy: TestGameplayEntropy.Create(1337UL));
        world.InitializeFromStoreAsync().GetAwaiter().GetResult();

        ulong[] connections = new ulong[players];
        DateTimeOffset createdAt = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < players; i++)
        {
            // A 4-wide grid two tiles apart, so every player sees the others and the same monsters.
            Vector2 position = world.DefaultSpawnWorldPosition + (new Vector2(i % 4, i / 4) * 64f);
            CharacterRecord hero = new(Guid.NewGuid(), Guid.NewGuid(), $"Bench{i}", 1, 0, position.X, position.Y, 100f, 100f, 0, [], createdAt);
            connections[i] = 1_000UL + (ulong)i;
            OpStatus status = world.TryEnterWorld(connections[i], hero).Status;
            if (status != OpStatus.Ok)
            {
                throw new InvalidOperationException($"player {i} could not enter the world: {status}");
            }
        }

        return new TickAllocationHarness(dbPath, factory, world, connections);
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

    /// <summary>Runs <paramref name="ticks"/> ticks and returns bytes allocated per tick and mean milliseconds per tick.</summary>
    public (double BytesPerTick, double MillisecondsPerTick) Measure(int ticks)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < ticks; i++)
        {
            this.Step();
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return ((double)allocated / ticks, elapsed.TotalMilliseconds / ticks);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
