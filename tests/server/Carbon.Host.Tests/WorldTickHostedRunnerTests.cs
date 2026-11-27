// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>The host's tick runner, which owns the world's lifetime between start-up and shutdown.</summary>
public sealed class WorldTickHostedRunnerTests
{
    [Fact]
    public async Task RunAsync_OnShutdown_StopsTheGeneratorsBackgroundBuildsAfterTheLastFlush()
    {
        // ADR-045 D10: once no tick will run again, the world's background generation builds stop, and only then.
        RecordingLoop world = new();
        WorldTickHostedRunner runner = new(world, NullLogger<WorldTickHostedRunner>.Instance);
        using CancellationTokenSource cts = new();

        Task run = runner.RunAsync(cts.Token);
        world.WaitForTicks(3).Should().BeTrue("the runner ticks the world");
        await cts.CancelAsync().ConfigureAwait(true);
        await run.ConfigureAwait(true);

        world.Calls.Should().EndWith(["FailPendingCommands", "FlushDirtyPersistence", "StopBackgroundBuilds"]);
        world.Calls.Should().ContainSingle(c => c == "StopBackgroundBuilds");
    }

    private sealed class RecordingLoop : IWorldSimulationLoop
    {
        private readonly Lock _gate = new();
        private readonly List<string> _calls = [];
        private int _ticks;

        public ushort TickRate => 60;

        public ulong CurrentTick => (ulong)Volatile.Read(ref _ticks);

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        public bool WaitForTicks(int count) => SpinWait.SpinUntil(() => Volatile.Read(ref _ticks) >= count, TimeSpan.FromSeconds(30));

        public Task InitializeFromStoreAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void StepFixedTick() => Interlocked.Increment(ref _ticks);

        public Task FlushDirtyPersistenceAsync(CancellationToken ct = default)
        {
            this.Record("FlushDirtyPersistence");
            return Task.CompletedTask;
        }

        public void FailPendingCommands() => this.Record("FailPendingCommands");

        public bool StopBackgroundBuilds(TimeSpan timeout)
        {
            this.Record("StopBackgroundBuilds");
            return true;
        }

        private void Record(string call)
        {
            lock (_gate)
            {
                _calls.Add(call);
            }
        }
    }
}
