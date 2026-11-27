// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.World;
using Carbon.Contracts.Entities.S2C;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>Audit round 2, PERF-3: exact-length delta-batch columns are pooled per entry count.</summary>
public sealed class DeltaColumnPoolTests(ITestOutputHelper output)
{
    [Fact]
    public void Rent_CopiesEveryEntryIntoExactLengthColumns()
    {
        DeltaColumnPool pool = new();
        EntityDeltaEntry[] entries = Entries(3);

        DeltaColumns columns = pool.Rent(entries);

        columns.Count.Should().Be(3, "the packet serializes each array by its length");
        columns.Ids.Should().Equal(entries[0].NetworkId, entries[1].NetworkId, entries[2].NetworkId);
        columns.PositionsX[1].Should().Be(entries[1].PositionX);
        columns.PositionsY[1].Should().Be(entries[1].PositionY);
        columns.VelocitiesX[2].Should().Be(entries[2].VelocityX);
        columns.VelocitiesY[0].Should().Be(entries[0].VelocityY);
        columns.Healths[2].Should().Be(entries[2].CurrentHealth);
    }

    [Fact]
    public void Return_ThenRentSameCount_ReusesTheSameColumns()
    {
        DeltaColumnPool pool = new();
        DeltaColumns first = pool.Rent(Entries(5));
        pool.Return(first);

        DeltaColumns second = pool.Rent(Entries(5, seed: 9));
        DeltaColumns otherCount = pool.Rent(Entries(6));

        second.Should().BeSameAs(first);
        second.Ids[0].Should().Be(Entries(5, seed: 9)[0].NetworkId, "a reused set is overwritten with the new entries");
        otherCount.Should().NotBeSameAs(first, "columns are pooled per exact count");
    }

    [Fact]
    public void Rent_EmptyBatch_ReturnsSharedEmptyColumnsThatAreNeverPooled()
    {
        DeltaColumnPool pool = new();

        DeltaColumns empty = pool.Rent([]);
        pool.Return(empty);

        empty.Should().BeSameAs(DeltaColumns.Empty);
        empty.Count.Should().Be(0);
    }

    [Fact]
    public void Return_OverPerCountLimit_DropsTheExtraSets()
    {
        DeltaColumnPool pool = new();
        DeltaColumns[] rented = new DeltaColumns[DeltaColumnPool.MaxSetsPerCount + 1];
        for (int i = 0; i < rented.Length; i++)
        {
            rented[i] = pool.Rent(Entries(2));
        }

        foreach (DeltaColumns c in rented)
        {
            pool.Return(c);
        }

        int reused = 0;
        for (int i = 0; i < rented.Length; i++)
        {
            if (Array.IndexOf(rented, pool.Rent(Entries(2))) >= 0)
            {
                reused++;
            }
        }

        reused.Should().Be(DeltaColumnPool.MaxSetsPerCount, "the pool keeps a bounded number of sets per count");
    }

    [Fact]
    public void PopulatedPacket_FromPooledColumns_SerializesLikeFreshArrays()
    {
        DeltaColumnPool pool = new();
        EntityDeltaEntry[] entries = Entries(4);
        pool.Return(pool.Rent(Entries(4, seed: 3)));

        using EntityDeltaBatch pooled = new();
        WorldOutboundSink.PopulateEntityDeltaBatch(pooled, 42UL, 7U, 1f, 2f, pool.Rent(entries));
        using EntityDeltaBatch fresh = new()
        {
            ServerTick = 42UL,
            LastProcessedClientTick = 7U,
            SelfPositionX = 1f,
            SelfPositionY = 2f,
            NetworkIds = Array.ConvertAll(entries, e => e.NetworkId),
            PositionsX = Array.ConvertAll(entries, e => e.PositionX),
            PositionsY = Array.ConvertAll(entries, e => e.PositionY),
            VelocitiesX = Array.ConvertAll(entries, e => e.VelocityX),
            VelocitiesY = Array.ConvertAll(entries, e => e.VelocityY),
            CurrentHealths = Array.ConvertAll(entries, e => e.CurrentHealth),
        };

        Serialize(pooled).Should().Equal(Serialize(fresh), "pooling must not change a single byte on the wire");
    }

    [Fact]
    public void PopulateEntityDeltaBatch_EachEntity_CostsOnlyItsIdPositionVelocityAndHealth()
    {
        // ADR-038 batch 3: no per-entity dirty-mask byte; the server's dirty flags only pick which entities are sent.
        DeltaColumnPool pool = new();
        using EntityDeltaBatch none = new();
        using EntityDeltaBatch three = new();
        WorldOutboundSink.PopulateEntityDeltaBatch(none, 1UL, 1U, 0f, 0f, DeltaColumns.Empty);
        WorldOutboundSink.PopulateEntityDeltaBatch(three, 1UL, 1U, 0f, 0f, pool.Rent(Entries(3)));

        (Serialize(three).Length - Serialize(none).Length).Should().Be(3 * (sizeof(ulong) + (5 * sizeof(float))));
    }

    [Fact]
    public void ClearColumns_AfterSend_LeavesNoReferenceToPooledArrays()
    {
        DeltaColumnPool pool = new();
        DeltaColumns columns = pool.Rent(Entries(3));
        using EntityDeltaBatch packet = new();
        WorldOutboundSink.PopulateEntityDeltaBatch(packet, 1UL, 1U, 0f, 0f, columns);

        WorldOutboundSink.ClearColumns(packet);

        packet.NetworkIds.Should().BeEmpty();
        packet.CurrentHealths.Should().BeEmpty();
    }

    [Fact]
    public void RentPopulateReturn_SteadyStateAoi_AllocatesNothing()
    {
        // AGENTS.md rule 4: one delta batch per observer per tick. Before PERF-3 this built 7 new arrays every time.
        DeltaColumnPool pool = new();
        using EntityDeltaBatch packet = new();
        EntityDeltaEntry[][] observers = [Entries(12), Entries(40), Entries(40), Entries(73)];
        for (int i = 0; i < 64; i++)
        {
            SendOnce(pool, packet, observers);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 1_000; tick++)
        {
            SendOnce(pool, packet, observers);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{allocated} bytes over 4,000 batches");
        allocated.Should().Be(0, "steady-state delta batches must reuse their columns");
    }

    private static void SendOnce(DeltaColumnPool pool, EntityDeltaBatch packet, EntityDeltaEntry[][] observers)
    {
        foreach (EntityDeltaEntry[] entries in observers)
        {
            DeltaColumns columns = pool.Rent(entries);
            WorldOutboundSink.PopulateEntityDeltaBatch(packet, 1UL, 1U, 0f, 0f, columns);
            WorldOutboundSink.ClearColumns(packet);
            pool.Return(columns);
        }
    }

    private static byte[] Serialize(EntityDeltaBatch packet)
    {
        byte[] buffer = new byte[packet.Length];
        int written = packet.Serialize(buffer);
        return buffer.AsSpan(0, written).ToArray();
    }

    private static EntityDeltaEntry[] Entries(int count, int seed = 1)
    {
        EntityDeltaEntry[] entries = new EntityDeltaEntry[count];
        for (int i = 0; i < count; i++)
        {
            int v = (seed * 1_000) + i;
            entries[i] = new EntityDeltaEntry((ulong)v, v * 1.5f, v * -2f, v * 0.25f, v * -0.5f, 100f - i);
        }

        return entries;
    }
}
