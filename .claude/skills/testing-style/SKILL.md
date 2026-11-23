---
name: testing-style
description: Use when writing, modifying, or reviewing tests in Carbon — xUnit + FluentAssertions + NSubstitute, MethodUnderTest_Scenario_ExpectedBehavior naming, deterministic game simulation testing, tick stepping, and test isolation.
---

# Testing Style for Carbon

Tests reside under `tests/` and cover `contracts/`, `engine/`, `server/`, and `analyzer/`.

## Non-negotiables

- **Deterministic & Isolated**:
  - Tests must never depend on execution order or shared mutable state.
  - Never depend on wall-clock time (e.g., `DateTime.Now`) or real-time delays (`Thread.Sleep`).
  - Game world simulation tests must use a deterministic virtual tick stepper (e.g., `world.Tick(TimeSpan.FromMilliseconds(50))`).
- **No Production Data or Live External Services**:
  - Use deterministic test fakes and in-memory test doubles.
- **Assertions with Context**:
  - Use FluentAssertions with meaningful failure messages (`result.Should().BeTrue("player should be alive after taking partial damage")`).
- **Zero Warnings**:
  - Test projects must compile cleanly with zero warnings under `TreatWarningsAsErrors`.

## Stack & Tools

- **Framework**: xUnit
- **Assertions**: FluentAssertions
- **Mocking**: NSubstitute
- **Simulation Harness**: Virtual tick runner and mock packet sender/receiver.

## Test Naming Convention

Format: `MethodUnderTest_Scenario_ExpectedBehavior`

```csharp
[Fact]
public void ProcessMovement_WhenSpeedExceedsLimit_RejectsMovementAndFlagsAnomaly()
{
    // Arrange
    WorldSimulation simulation = CreateTestWorld();
    PlayerEntity player = simulation.SpawnPlayer(startPosition: Vector3.Zero, maxSpeed: 10.0f);
    MovementCommand illegalCommand = new MovementCommand(targetPosition: new Vector3(100.0f, 0, 0), deltaTime: 1.0f);

    // Act
    MovementResult result = simulation.ProcessMovement(player, illegalCommand);

    // Assert
    result.IsAccepted.Should().BeFalse("movement speed exceeds maximum permitted velocity");
    player.Position.Should().Be(Vector3.Zero, "player position should not advance on rejected movement");
}
```

## Deterministic Tick Stepping Pattern

When testing systems that update over time (e.g., damage over time, cooldowns, spatial partitioning):

```csharp
[Fact]
public void CooldownSystem_WhenDurationElapses_ResetsSkillReady()
{
    // Arrange
    WorldSimulation world = new WorldSimulation();
    Entity player = world.CreateEntityWithCooldown(skillId: 1, durationMs: 1000);

    // Act: Advance virtual clock by 500ms (10 ticks at 50ms/tick)
    world.StepTicks(10, tickDelta: TimeSpan.FromMilliseconds(50));
    world.IsSkillReady(player, skillId: 1).Should().BeFalse("cooldown has only elapsed halfway");

    // Act: Advance remaining 500ms
    world.StepTicks(10, tickDelta: TimeSpan.FromMilliseconds(50));

    // Assert
    world.IsSkillReady(player, skillId: 1).Should().BeTrue("cooldown duration has fully completed");
}
```
