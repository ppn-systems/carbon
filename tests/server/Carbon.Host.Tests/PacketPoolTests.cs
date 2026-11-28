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
    // Only bounds how long a test waits for the thread pool to resume a dispatcher; every outcome is asserted exactly.
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

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

        rig.WaitForPendingSends(2);
        rig.Manager.Outstanding.Should().Be(2, "neither packet may go back while its send is outstanding");
        rig.Manager.Rents.Should().Be(2, "the second send got a second instance, not the first one again");

        rig.Connection.CompleteNext();
        rig.WaitUntilOutstanding(1);
        rig.Manager.Outstanding.Should().Be(1, "the completed send returned its packet; the other is still out");
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
        rig.WaitForPendingSends(1);
        rig.Connection.FailNext(new IOException("connection reset"));

        rig.WaitUntilOutstanding(0);
        rig.Manager.DoubleReturns.Should().Be(0);
        rig.Logger.WaitForFailures(1, s_timeout).Should().BeTrue("the failure is logged");
        rig.Logger.Failures.Should().ContainSingle();
    }

    [Fact]
    public void SendEntityDespawn_CancelledSend_StillReturnsThePacketOnce()
    {
        using Rig rig = new();

        rig.Sink.SendEntityDespawn(Connection, 100UL);
        rig.WaitForPendingSends(1);
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

        rig.WaitForPendingSends(1);
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

        // The dispatchers reach the connection through Nalix's send path; wait until it holds the sends, rather than
        // assume they are queued when the sink call returns.
        // A completed send resumes its dispatcher on the thread pool; wait for the return it makes.
        public void WaitUntilOutstanding(int count) =>
            this.Manager.WaitForOutstanding(count, s_timeout).Should().BeTrue(
                $"{count} packet(s) should be out, but {this.Manager.Outstanding} are");

        public void WaitForPendingSends(int count) =>
            this.Connection.WaitForPendingSends(count, s_timeout).Should().BeTrue(
                $"{count} send(s) should be held, but {this.Connection.PendingSends} are");

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

        // Woken by every logged failure; a failed send logs from its dispatcher on the thread pool.
        public bool WaitForFailures(int count, TimeSpan timeout)
        {
            long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            lock (_failures)
            {
                while (_failures.Count < count)
                {
                    long left = deadline - Environment.TickCount64;
                    if (left <= 0 || !Monitor.Wait(_failures, TimeSpan.FromMilliseconds(left)))
                    {
                        return _failures.Count >= count;
                    }
                }

                return true;
            }
        }

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
                    Monitor.PulseAll(_failures);
                }
            }
        }
    }
}
