// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class SpatialAoiTrackerTests
{
    private const ulong Connection = 5UL;
    private const ulong ObserverNetId = 1UL;
    private const float ViewRadius = 768f;

    private readonly WorldSimulation _simulation = new();
    private readonly SpatialAoiTracker _tracker = new(ViewRadius);
    private readonly List<ulong> _entered = [];
    private readonly List<ulong> _visible = [];
    private readonly List<ulong> _exited = [];
    private ulong _tick;

    public SpatialAoiTrackerTests()
    {
        _ = _simulation.SpawnNetworkEntity(ObserverNetId, Vector2.Zero, 7f);
        _tracker.TrackConnection(Connection);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(5000)]
    public void ComputeVisibility_DenseClusterAboveTheScratchBuffer_SeesEveryEntityAndNeverFlickers(int count)
    {
        // A dense crowd standing still inside the view: more entities than the initial broadphase buffer (256) and,
        // for 5000, more than the old 4096 growth cap. Each one must enter once and then stay visible.
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = new((i % 70 * 8f) - 280f, (i / 70 * 8f) - 280f);
            _ = _simulation.SpawnNetworkEntity(1000UL + (ulong)i, pos, 3f);
        }

        this.Compute();
        _entered.Should().HaveCount(count, "every entity in view enters on the first tick");
        _exited.Should().BeEmpty();

        for (int tick = 0; tick < 3; tick++)
        {
            this.Compute();
            _exited.Should().BeEmpty("an entity that stays in view must never be despawned");
            _entered.Should().BeEmpty("an entity that stays in view must never be respawned");
            _visible.Should().HaveCount(count);
        }
    }

    [Fact]
    public void ComputeVisibility_EntityWanderingAcrossTheViewEdge_StaysVisibleInsideTheExitMargin()
    {
        Entity mob = _simulation.SpawnNetworkEntity(42UL, new Vector2(ViewRadius - 8f, 0f), 7f);
        this.Compute();
        _entered.Should().Equal(42UL);

        // Just past the enter radius but inside the exit margin: no despawn.
        this.MoveTo(mob, new Vector2(ViewRadius + (SpatialAoiTracker.DefaultExitMargin * 0.5f), 0f));
        this.Compute();
        _exited.Should().BeEmpty("the edge hysteresis keeps it visible");
        _visible.Should().Contain(42UL);

        this.MoveTo(mob, new Vector2(ViewRadius - 8f, 0f));
        this.Compute();
        _entered.Should().BeEmpty("it never left, so it is not spawned again");

        // Beyond the exit radius it leaves, and it only comes back once inside the enter radius again.
        this.MoveTo(mob, new Vector2(ViewRadius + SpatialAoiTracker.DefaultExitMargin + 8f, 0f));
        this.Compute();
        _exited.Should().Equal(42UL);

        this.MoveTo(mob, new Vector2(ViewRadius + (SpatialAoiTracker.DefaultExitMargin * 0.5f), 0f));
        this.Compute();
        _entered.Should().BeEmpty("re-entry needs the enter radius, not the exit radius");

        this.MoveTo(mob, new Vector2(ViewRadius - 8f, 0f));
        this.Compute();
        _entered.Should().Equal(42UL);
    }

    private void Compute() =>
        _tracker.ComputeVisibility(Connection, ObserverNetId, Vector2.Zero, _simulation, _entered, _visible, _exited);

    private void MoveTo(Entity entity, Vector2 position)
    {
        _simulation.EnqueueCommand(SimulationCommand.Teleport(entity, position));
        _tick++;
        _simulation.Tick(new FixedTickArgs(_tick, 1f / 30f, _tick / 30f));
    }
}
