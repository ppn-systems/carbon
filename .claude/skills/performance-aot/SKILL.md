---
name: performance-aot
description: Use when writing performance-sensitive code in Carbon — game tick loop, packet handling, serialization, entity component iteration, broadcast fan-out, and Native AOT / Trimming configuration. Hot-path vs normal-path allocation rules and benchmarking requirements.
---

# Performance and Native AOT for Carbon

Carbon is designed for high-throughput, low-latency MMORPG gameplay across dedicated server and cross-platform clients (Desktop & WebAssembly).

## Proportional Optimization

- **Hot Paths (Game Simulation & Networking)**:
  - Zero heap allocation during steady-state tick loop.
  - Packet buffers and DTOs must use object pooling (`PacketFactory<T>.Acquire()`, `ArrayPool<byte>.Shared`).
  - Use `Span<T>`, `ReadOnlySpan<T>`, and `Memory<T>` for slicing packet data and byte streams without copying.
  - Entity components in `engine/` should be unmanaged structs to ensure cache locality and zero GC pressure.
- **Cold Paths (Initialization, Asset Loading, Configs, Auth)**:
  - Clean, readable, idiomatic C# (LINQ, async/await, dependency injection) is preferred over premature micro-optimization.

## Native AOT & Trimming Guidelines

| Component | Target Profile | Constraints |
| :--- | :--- | :--- |
| `server/` | `PublishAot=true` or `PublishTrimmed=true` | No unconstrained reflection, no runtime code generation (`Reflection.Emit`). Use source generators for serialization and DI. |
| `contracts/` | `IsAotCompatible=true`, `IsTrimmable=true` | Pure data contracts, source-generated serializers (`[GenerateFormatter]`). |
| `engine/` | `IsAotCompatible=true` | High-performance arithmetic, unmanaged struct components, zero runtime reflection. |
| `wasm/` | `PublishTrimmed=true`, `TrimMode=partial` | WebAssembly client builds require minimal payload size; root necessary reflection in trimmer roots. |

## Hot Path Rules (Tick Loop & Dispatching)

1. **No LINQ on the Tick Path**: Avoid `.Where()`, `.Select()`, `.ToList()` in systems executing every tick (20Hz/30Hz/60Hz). Use direct `for` loops or struct enumerators.
2. **Object Pooling**:
   - Packets and network messages are rented from pools and returned promptly upon send/serialization.
   - Use `using PacketScope<T> lease = PacketFactory<T>.Acquire();` with `try/catch` safety to prevent pool leaks.
3. **Broadcasting & Fan-out**:
   - Serialize once, send to many. When broadcasting a world state snapshot to multiple players in an AoI (Area of Interest), serialize the packet buffer once and dispatch references to each player's socket sender.
4. **Boxing Prevention**:
   - Ensure generic type constraints avoid boxing value types (e.g., `IComponent` structs should be constrained to `where T : struct`).

## Measuring & Verification

- Never optimize based on guesswork. Measure before and after with benchmarks.
- Use `BenchmarkDotNet` for micro-benchmarks on serialization, math, and ECS iterations.
- Leverage `Nalix.Observability` metrics (in `library/`) to track tick duration (p50, p95, p99), GC collections (Gen 0/1/2), and network throughput.
