---
name: carbon-engine-ecs
description: Use when writing or reviewing game engine, simulation, ECS, or rendering code in engine/ or desktop/ — unmanaged struct components, pure systems, Spatial Hashing (AoI), Fixed-Tick simulation loop, and Silk.NET.OpenGL rendering pipeline.
---

# Carbon Engine Architecture & ECS

Applies to core simulation in `engine/` and rendering client runners in `desktop/`.

## Architectural Principles

Carbon separates deterministic game state simulation from rendering:
- **Simulation**: Fixed-Tick loop (e.g. 20Hz / 30Hz / 60Hz), deterministic, headless, runs on both Server and Client (for client prediction).
- **Rendering**: Variable FPS loop driven by `Silk.NET.OpenGL` and `Silk.NET.Windowing` on the Desktop client runner.

## Component Design (Entity Component System)

1. **Pure Data Struct Components**:
   - Every component MUST be a `readonly struct` or `unmanaged struct` (e.g. `PositionComponent`, `VelocityComponent`, `HealthComponent`).
   - NEVER put class references, heap collections (`List<T>`, `Dictionary<K,V>`), or interface members inside components.
   - Ensures memory cache locality, zero GC allocation, and SIMD/vectorization potential.
   ```csharp
   public readonly record struct PositionComponent(float X, float Y, float Z);
   public readonly record struct VelocityComponent(float Vx, float Vy, float Vz);
   ```
2. **Pure Systems**:
   - Systems contain logic, NO entity state.
   - Iterate over entity chunks sequentially (`for` loops over `Span<T>` or direct memory pointers).
   - No LINQ, no allocations during system `Update(TimeSpan delta)`.

## Spatial Partitioning & Area of Interest (AoI)

- **Spatial Hashing**:
  - The game world is divided into uniform 2D/3D grid cells (e.g., 32x32 units per cell).
  - Entities update their cell index only when crossing cell boundaries.
  - Querying nearby entities for combat, collision, and network broadcast queries the cell and its 8 adjacent neighbors.
  - Zero heap allocation during AoI queries (rent pooled buffers or write into pre-allocated scratch spans).

## Fixed-Tick Simulation Loop

- Decoupled from frame rate:
  ```csharp
  // Fixed timestep accumulator
  _accumulator += frameDelta;
  while (_accumulator >= TargetTickInterval)
  {
      TickSimulation(TargetTickInterval);
      _accumulator -= TargetTickInterval;
  }
  ```
- All physics, movement, cooldowns, and collision logic execute strictly inside `TickSimulation`.

## Rendering Pipeline (Silk.NET.OpenGL)

- The rendering system consumes snapshots or interpolated transforms from the ECS simulation.
- Uses modern OpenGL (Core Profile ≥ 3.3 / 4.5) via `Silk.NET.OpenGL.GL`.
- Batched drawing: Sprites/meshes are batched into dynamic Vertex Buffer Objects (VBO / VAO) with instanced rendering to minimize draw calls.
- Resource management: Textures, shaders, and buffers must be explicitly released (`IDisposable` pattern).
