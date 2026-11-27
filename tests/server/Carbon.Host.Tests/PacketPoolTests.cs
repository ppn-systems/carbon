// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Carbon.Abstractions.World;
using Carbon.Contracts.Entities.S2C;
using Carbon.Host.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-043: outbound packets are leased from a <see cref="PacketPool"/>. A packet goes back exactly once, only after its
/// send completed, failed or was cancelled, and reset, so nothing of one send reaches the next recipient.
/// </summary>
public sealed class PacketPoolTests
{
    private const ulong Connection = 7UL;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Lease_Dispose_ReturnsThePacketOnceAndReset()
    {
        RecordingPoolManager manager = new();
        using PacketPool pool = new(manager);

        EntityDespawn first;
        using (PacketLease<EntityDespawn> lease = pool.Rent<EntityDespawn>())
        {
            first = lease.Value;
            first.NetworkId = 42UL;
            manager.IsOutstanding(first).Should().BeTrue();
        }

        manager.Outstanding.Should().Be(0);
        using PacketLease<EntityDespawn> again = pool.Rent<EntityDespawn>();
        again.Value.Should().BeSameAs(first, "the returned packet is reused");
        again.Value.NetworkId.Should().Be(0UL, "the pool reset it; the manager here does not");
        manager.DoubleReturns.Should().Be(0);
    }

    [Fact]
    public void SendEntityDespawn_WhileTheSendIsPending_KeepsThePacketOutAndHandsTheNextSendAnother()
    {
        using Rig rig = new();

        rig.Sink.SendEntityDespawn(Connection, 100UL);
        rig.Sink.SendEntityDespawn(Connection, 200UL);

        rig.Connection.PendingSends.Should().Be(2);
        rig.Manager.Outstanding.Should().Be(2, "neither packet may go back while its send is outstanding");
        rig.Manager.Rents.Should().Be(2, "the second send got a second instance, not the first one again");

        rig.Connection.CompleteNext();
        rig.WaitUntilOutstanding(1);
        rig.Connection.CompleteNext();
        rig.WaitUntilOutstanding(0);

        rig.Manager.DoubleReturns.Should().Be(0);
        rig.Logger.Failures.Should().BeEmpty();
    }

    [Fact]
    public void SendEntityDespawn_FailedSend_StillReturnsThePacketOnce()
    {
        using Rig rig = new();

        rig.Sink.SendEntityDespawn(Connection, 100UL);
        rig.Connection.FailNext(new IOException("connection reset"));
        rig.WaitUntilOutstanding(0);

        rig.Manager.DoubleReturns.Should().Be(0);
        rig.Logger.WaitForFailures(1, s_timeout).Should().BeTrue("the failure is logged");
    }

    [Fact]
    public void SendEntityDespawn_CancelledSend_StillReturnsThePacketOnce()
    {
        using Rig rig = new();

        rig.Sink.SendEntityDespawn(Connection, 100UL);
        rig.Connection.CancelNext();
        rig.WaitUntilOutstanding(0);

        rig.Manager.DoubleReturns.Should().Be(0);
    }

    [Fact]
    public void SendEntityDeltaBatch_WhileTheSendIsPending_KeepsThePacketOut()
    {
        using Rig rig = new();
        EntityDeltaEntry[] entries = [new(1UL, 1f, 2f, 0f, 0f, 50f), new(2UL, 3f, 4f, 0f, 0f, 60f)];

        rig.Sink.SendEntityDeltaBatch(Connection, 10UL, 3U, 0f, 0f, entries);

        rig.Manager.Outstanding.Should().Be(1);
        rig.Connection.CompleteNext();
        rig.WaitUntilOutstanding(0);
        rig.Manager.DoubleReturns.Should().Be(0);
    }

    private sealed class Rig : IDisposable
    {
        private readonly SynchronousConnectionHub _hub = new();
        private readonly PacketPool _pool;

        public Rig()
        {
            _pool = new PacketPool(this.Manager);
            SessionTransportRegistry registry = new();
            registry.AttachHub(_hub);
            this.Sink = new WorldOutboundSink(registry, this.Logger, _pool);
            this.Sink.AttachHub(_hub);
            _hub.RegisterConnection(this.Connection);
        }

        public RecordingPoolManager Manager { get; } = new();

        public SynchronousConnection Connection { get; } = new(PacketPoolTests.Connection, holdSends: true);

        public FailureLogger Logger { get; } = new();

        public WorldOutboundSink Sink { get; }

        public void WaitUntilOutstanding(int count)
        {
            // Completed sends resume their dispatchers asynchronously.
            SpinWait.SpinUntil(() => this.Manager.Outstanding == count, s_timeout).Should().BeTrue(
                $"{count} packet(s) should be out, but {this.Manager.Outstanding} are");
        }

        public void Dispose()
        {
            _pool.Dispose();
            _hub.Dispose();
        }
    }

    private sealed class FailureLogger : ILogger<WorldOutboundSink>
    {
        private readonly List<string> _failures = [];

        public IReadOnlyList<string> Failures
        {
            get
            {
                lock (_failures)
                {
                    return [.. _failures];
                }
            }
        }

        public bool WaitForFailures(int count, TimeSpan timeout) => SpinWait.SpinUntil(() => this.Failures.Count >= count, timeout);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                lock (_failures)
                {
                    _failures.Add($"{exception.GetType().Name}: {exception.Message}");
                }
            }
        }
    }
}
