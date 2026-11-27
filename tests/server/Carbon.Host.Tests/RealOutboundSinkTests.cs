// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Host.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Audit round 3, ALLOC-1: the steady-state tick through the real <see cref="WorldOutboundSink"/>, not a null sink. Each
/// player has a <see cref="SynchronousConnection"/>, so every send completes synchronously, without a socket. This pins
/// that the sink's sends all arrive and none fails. Its allocation budget is not asserted yet: in our setup every
/// <c>PacketFactory&lt;T&gt;.Acquire()</c> allocates its packet (audit ALLOC-1, open; ADR-043 proposes the fix).
/// </summary>
public sealed class RealOutboundSinkTests
{
    private const int Players = 8;

    [Fact]
    public void StepFixedTick_RealSinkWithSynchronousConnections_EverySendArrivesAndNoneFails()
    {
        using SynchronousConnectionHub hub = new();
        SessionTransportRegistry registry = new();
        registry.AttachHub(hub);
        FailureLogger logger = new();
        WorldOutboundSink sink = new(registry, logger);
        sink.AttachHub(hub);
        List<SynchronousConnection> connections = [];
        for (int i = 0; i < Players; i++)
        {
            SynchronousConnection connection = new(1_000UL + (ulong)i);
            connections.Add(connection);
            hub.RegisterConnection(connection);
        }

        using TickAllocationHarness harness = TickAllocationHarness.Create(Players, sink);
        for (int i = 0; i < 900; i++)
        {
            harness.Step();
        }

        long[] framesBefore = [.. connections.ConvertAll(c => c.Frames)];
        for (int i = 0; i < 300; i++)
        {
            harness.Step();
        }

        logger.Failures.Should().BeEmpty("every send reaches the synchronous connection");
        for (int i = 0; i < Players; i++)
        {
            (connections[i].Frames - framesBefore[i]).Should().BeGreaterThanOrEqualTo(300, $"player {i} gets at least one delta batch per tick");
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
