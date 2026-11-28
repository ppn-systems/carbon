// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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
        using CancellationTokenSource cts = new();
        WorldTickHostedRunner runner = new(world, NullLogger<WorldTickHostedRunner>.Instance, cts.Cancel);

        Task<WorldTickRunOutcome> run = runner.RunAsync(cts.Token);
        world.WaitForTicks(3).Should().BeTrue("the runner ticks the world");
        await cts.CancelAsync().ConfigureAwait(true);
        (await run.ConfigureAwait(true)).Should().Be(WorldTickRunOutcome.Stopped);

        world.Calls.Should().EndWith(["FailPendingCommands", "FlushDirtyPersistence", "StopBackgroundBuilds"]);
        world.Calls.Should().ContainSingle(c => c == "StopBackgroundBuilds");
    }

    [Fact]
    public async Task RunAsync_WhenATickThrows_StopsTheHostAfterAFinalFlushAndExitsNonZero()
    {
        // Audit TS-5: a failed tick ends the run, logs the tick and the exception, stops the host (so no player joins a
        // world that no longer ticks), still answers pending commands and flushes, and reports a non-zero exit code.
        RecordingLoop world = new() { ThrowOnTick = 3 };
        CapturingLogger logger = new();
        using CancellationTokenSource host = new();
        WorldTickHostedRunner runner = new(world, logger, host.Cancel);

        WorldTickRunOutcome outcome = await runner.RunAsync(host.Token).ConfigureAwait(true);

        outcome.Should().Be(WorldTickRunOutcome.Faulted);
        ((int)outcome).Should().NotBe(0, "the process exits non-zero so its supervisor restarts it");
        host.IsCancellationRequested.Should().BeTrue("the host is stopped");
        world.CurrentTick.Should().Be(3UL, "the world is never ticked again after a failed tick");
        world.Calls.Should().EndWith(["FailPendingCommands", "FlushDirtyPersistence", "StopBackgroundBuilds"]);
        logger.Critical.Should().ContainSingle().Which.Should().Match("World tick 3 failed*");
        logger.CriticalExceptions.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_WhenATickThrowsAndTheFinalFlushFails_StillStopsTheHostAndExitsNonZero()
    {
        // The final flush is best effort: its failure is logged and does not hide the tick failure.
        RecordingLoop world = new() { ThrowOnTick = 2, FailFlush = true };
        using CancellationTokenSource host = new();
        WorldTickHostedRunner runner = new(world, NullLogger<WorldTickHostedRunner>.Instance, host.Cancel);

        WorldTickRunOutcome outcome = await runner.RunAsync(host.Token).ConfigureAwait(true);

        outcome.Should().Be(WorldTickRunOutcome.Faulted);
        host.IsCancellationRequested.Should().BeTrue();
        world.Calls.Should().EndWith(["FailPendingCommands", "FlushDirtyPersistence", "StopBackgroundBuilds"]);
    }

    private sealed class CapturingLogger : ILogger<WorldTickHostedRunner>
    {
        public List<string> Critical { get; } = [];

        public List<Exception?> CriticalExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical)
            {
                this.Critical.Add(formatter(state, exception));
                this.CriticalExceptions.Add(exception);
            }
        }
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

        /// <summary>Gets the tick whose step throws; 0 never throws.</summary>
        public int ThrowOnTick { get; init; }

        public bool WaitForTicks(int count) => SpinWait.SpinUntil(() => Volatile.Read(ref _ticks) >= count, TimeSpan.FromSeconds(30));

        public Task InitializeFromStoreAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void StepFixedTick()
        {
            int tick = Interlocked.Increment(ref _ticks);
            if (tick == this.ThrowOnTick)
            {
                throw new InvalidOperationException($"tick {tick} failed");
            }
        }

        /// <summary>Gets a value indicating whether every flush throws.</summary>
        public bool FailFlush { get; init; }

        public Task FlushDirtyPersistenceAsync(CancellationToken ct = default)
        {
            this.Record("FlushDirtyPersistence");
            return this.FailFlush ? Task.FromException(new InvalidOperationException("store down")) : Task.CompletedTask;
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
