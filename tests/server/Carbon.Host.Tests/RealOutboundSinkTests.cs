// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Host.Tests.Fakes;
using Carbon.Testing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>
/// Audit round 3, ALLOC-1 and ADR-043: the steady-state tick through the real <see cref="WorldOutboundSink"/>, not a null
/// sink. Each player has a <see cref="SynchronousConnection"/>, so every send completes synchronously, without a socket:
/// every send arrives, none fails, and the tick stays within the same allocation budget as with the null sink now that
/// outbound packets come from a <see cref="PacketPool"/>.
/// </summary>
public sealed class RealOutboundSinkTests(ITestOutputHelper output)
{
    private const int Players = 8;
    private const int WarmupTicks = 900;

    [Fact]
    public void StepFixedTick_RealSinkWithSynchronousConnections_EverySendArrivesAndNoneFails()
    {
        using Rig rig = new();

        long[] framesBefore = [.. rig.Connections.ConvertAll(c => c.Frames)];
        for (int i = 0; i < 300; i++)
        {
            rig.Harness.Step();
        }

        rig.Logger.Failures.Should().BeEmpty("every send reaches the synchronous connection");
        for (int i = 0; i < Players; i++)
        {
            (rig.Connections[i].Frames - framesBefore[i]).Should().BeGreaterThanOrEqualTo(300, $"player {i} gets at least one delta batch per tick");
        }
    }

    [Fact]
    public void StepFixedTick_RealSinkWithSynchronousConnections_StaysWithinTheTickAllocationBudget()
    {
        using Rig rig = new();

        AllocationResult window = rig.Harness.Measure(900);
        output.WriteLine($"{Players} players, real sink: {window.BytesPerIteration:F1} B/tick ({window})");

        rig.Logger.Failures.Should().BeEmpty();
        window.BytesPerIteration.Should().BeLessThanOrEqualTo(
            ServerTickAllocationTests.MaxBytesPerTick,
            $"outbound packets are pooled (ADR-043); the real sink costs what the null sink costs ({window})");
    }

    // Eight players on synchronous connections through the real sink, warmed up.
    private sealed class Rig : IDisposable
    {
        private readonly SynchronousConnectionHub _hub = new();
        private readonly PacketPool _packets = new();

        public Rig()
        {
            SessionTransportRegistry registry = new();
            registry.AttachHub(_hub);
            WorldOutboundSink sink = new(registry, this.Logger, _packets);
            sink.AttachHub(_hub);
            for (int i = 0; i < Players; i++)
            {
                SynchronousConnection connection = new(1_000UL + (ulong)i);
                this.Connections.Add(connection);
                _hub.RegisterConnection(connection);
            }

            this.Harness = TickAllocationHarness.Create(Players, sink);
            for (int i = 0; i < WarmupTicks; i++)
            {
                this.Harness.Step();
            }
        }

        public FailureLogger Logger { get; } = new();

        public List<SynchronousConnection> Connections { get; } = [];

        public TickAllocationHarness Harness { get; }

        public void Dispose()
        {
            this.Harness.Dispose();
            _packets.Dispose();
            _hub.Dispose();
        }
    }

    private sealed class FailureLogger : ILogger<WorldOutboundSink>
    {
        public List<string> Failures { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                this.Failures.Add($"{formatter(state, exception)}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
