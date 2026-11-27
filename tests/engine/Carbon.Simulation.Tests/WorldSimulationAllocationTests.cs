// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

/// <summary>
/// AGENTS.md rule 4: <see cref="WorldSimulation.Update"/> runs once per frame on the desktop client
/// (<c>MultiplayerSession</c>), so the frame path into the fixed tick must not allocate (audit round 3, ALLOC-10).
/// </summary>
public sealed class WorldSimulationAllocationTests
{
    [Fact]
    public void Update_EveryFrameWithAMovingEntity_AllocatesNothing()
    {
        WorldSimulation world = new(tickRate: 30);
        Entity mover = world.SpawnEntity(Vector2.Zero, radius: 8f);
        world.Entities.SetVelocity(mover, new Velocity2D(new Vector2(10f, 0f)));

        // One fixed tick per frame, so every measured frame enters the tick.
        AllocationResult window = AllocationProbe.Measure(300, warmupIterations: 60, i => _ = world.Update(1.0 / 30.0));

        window.Bytes.Should().Be(0, $"a frame that runs the fixed tick allocates nothing ({window})");
        world.Entities.GetTransform(mover).Position.X.Should().BeGreaterThan(100f, "the measured frames ran fixed ticks that moved the entity");
    }
}
