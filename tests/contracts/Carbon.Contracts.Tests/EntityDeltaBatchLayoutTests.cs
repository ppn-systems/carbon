// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Contracts.Entities.S2C;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>
/// ADR-038 batch 3: <c>EntityDeltaBatch</c>, the hottest S2C packet, carries per entity exactly an id, a position, a
/// velocity and the health, with no dirty-mask byte.
/// </summary>
public sealed class EntityDeltaBatchLayoutTests
{
    private const int BytesPerEntity = sizeof(ulong) + (5 * sizeof(float));

    [Fact]
    public void EntityDeltaBatch_RoundTrip_KeepsEveryColumn()
    {
        using EntityDeltaBatch packet = Batch(2);
        using EntityDeltaBatch copy = EntityDeltaBatch.Deserialize(packet.Serialize());

        Assert.Equal(900UL, copy.ServerTick);
        Assert.Equal(42U, copy.LastProcessedClientTick);
        Assert.Equal(10f, copy.SelfPositionX);
        Assert.Equal(20f, copy.SelfPositionY);
        Assert.Equal(packet.NetworkIds, copy.NetworkIds);
        Assert.Equal(packet.PositionsX, copy.PositionsX);
        Assert.Equal(packet.PositionsY, copy.PositionsY);
        Assert.Equal(packet.VelocitiesX, copy.VelocitiesX);
        Assert.Equal(packet.VelocitiesY, copy.VelocitiesY);
        Assert.Equal(packet.CurrentHealths, copy.CurrentHealths);
    }

    [Fact]
    public void EntityDeltaBatch_EachEntity_CostsOnlyItsIdPositionVelocityAndHealth()
    {
        using EntityDeltaBatch none = Batch(0);
        using EntityDeltaBatch three = Batch(3);

        Assert.Equal(3 * BytesPerEntity, three.Serialize().Length - none.Serialize().Length);
    }

    [Fact]
    public void Version_IsAtLeastSixSinceTheDirtyMasksLeftTheLayout() =>
        Assert.True(CarbonProtocol.Version >= 6, $"protocol {CarbonProtocol.Version} still expects EntityDeltaBatch.DirtyMasks");

    private static EntityDeltaBatch Batch(int count)
    {
        EntityDeltaBatch batch = new()
        {
            ServerTick = 900UL,
            LastProcessedClientTick = 42U,
            SelfPositionX = 10f,
            SelfPositionY = 20f,
            NetworkIds = new ulong[count],
            PositionsX = new float[count],
            PositionsY = new float[count],
            VelocitiesX = new float[count],
            VelocitiesY = new float[count],
            CurrentHealths = new float[count],
        };
        for (int i = 0; i < count; i++)
        {
            batch.NetworkIds[i] = 1000UL + (ulong)i;
            batch.PositionsX[i] = i * 1.5f;
            batch.PositionsY[i] = i * -2f;
            batch.VelocitiesX[i] = i * 0.25f;
            batch.VelocitiesY[i] = -i;
            batch.CurrentHealths[i] = 100f - i;
        }

        return batch;
    }
}
