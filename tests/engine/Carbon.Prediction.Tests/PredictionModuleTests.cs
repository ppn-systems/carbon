// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Prediction.Client;
using Carbon.Prediction.Remote;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Prediction.Tests;

public sealed class PredictionModuleTests
{
    [Fact]
    public void ClientPredictionReconciler_WhenServerMatches_DoesNotCorrect()
    {
        WorldSimulation sim = new(tickRate: 20);
        Entity player = sim.SpawnEntity(new Vector2(10f, 0f), radius: 5f);
        sim.Entities.SetVelocity(player, new Velocity2D(0f, 0f));

        ClientPredictionReconciler reconciler = new();
        reconciler.RecordPredictedTick(new PredictedTickRecord(1, Vector2.UnitX, 100f, new Vector2(10f, 0f), new Vector2(100f, 0f)));
        reconciler.RecordPredictedTick(new PredictedTickRecord(2, Vector2.UnitX, 100f, new Vector2(15f, 0f), new Vector2(100f, 0f)));

        sim.Entities.GetTransform(player).Position = new Vector2(15f, 0f);

        ReconciliationResult res = reconciler.Reconcile(1, new Vector2(10f, 0f), new Vector2(100f, 0f), sim, player);

        res.WasCorrected.Should().BeFalse();
        res.VisualErrorOffset.Should().Be(Vector2.Zero);
        reconciler.BufferedTickCount.Should().Be(1);
    }

    [Fact]
    public void ClientPredictionReconciler_WhenServerDiverges_RewindsAndReplaysRemainingTicks()
    {
        WorldSimulation sim = new(tickRate: 20); // dt = 0.05s
        Entity player = sim.SpawnEntity(new Vector2(20f, 0f), radius: 5f);
        sim.Entities.SetVelocity(player, new Velocity2D(100f, 0f));

        ClientPredictionReconciler reconciler = new();
        reconciler.RecordPredictedTick(new PredictedTickRecord(1, Vector2.UnitX, 100f, new Vector2(10f, 0f), new Vector2(100f, 0f)));
        reconciler.RecordPredictedTick(new PredictedTickRecord(2, Vector2.UnitX, 100f, new Vector2(15f, 0f), new Vector2(100f, 0f)));
        reconciler.RecordPredictedTick(new PredictedTickRecord(3, Vector2.UnitX, 100f, new Vector2(20f, 0f), new Vector2(100f, 0f)));

        // Server reports authoritative tick 1 at X = 50 (40u discrepancy -> triggers replay of ticks 2 & 3 at 100*0.05 = +5 each => X=60)
        ReconciliationResult res = reconciler.Reconcile(1, new Vector2(50f, 0f), new Vector2(100f, 0f), sim, player);

        res.WasCorrected.Should().BeTrue();
        res.CorrectedPosition.X.Should().BeApproximately(60f, 0.001f);
        sim.Entities.GetTransform(player).Position.X.Should().BeApproximately(60f, 0.001f);
        res.VisualErrorOffset.X.Should().BeApproximately(-40f, 0.001f);
        reconciler.BufferedTickCount.Should().Be(2);

        Vector2 decayed = VisualErrorSmoother.Decay(res.VisualErrorOffset, deltaTime: 0.05f, smoothSpeed: 10f);
        decayed.X.Should().BeApproximately(-20f, 0.001f);
    }

    [Fact]
    public void SnapshotInterpolationBuffer_InterpolatesAndBoundsExtrapolation()
    {
        SnapshotInterpolationBuffer buffer = new(capacity: 8);
        buffer.PushSnapshot(new TransformSnapshot(1.0, new Vector2(0f, 0f), new Vector2(100f, 0f))).Should().BeTrue();
        buffer.PushSnapshot(new TransformSnapshot(0.5, new Vector2(-50f, 0f), new Vector2(100f, 0f))).Should().BeFalse("out of order");
        buffer.PushSnapshot(new TransformSnapshot(2.0, new Vector2(100f, 0f), new Vector2(100f, 0f))).Should().BeTrue();

        TransformSnapshot mid = buffer.Sample(1.25);
        mid.Position.X.Should().BeApproximately(25f, 0.001f);

        // Extrapolates at most maxExtrapolationSeconds (0.1s * 100u/s = +10u -> X=110)
        TransformSnapshot extra = buffer.Sample(2.5, maxExtrapolationSeconds: 0.1f);
        extra.Position.X.Should().BeApproximately(110f, 0.001f);
    }
}
