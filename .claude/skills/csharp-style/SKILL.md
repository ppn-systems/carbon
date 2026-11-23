---
name: csharp-style
description: Use when writing or reviewing any C# — server services, handlers, engine components, contracts, analyzers, desktop, or wasm runners. Layer boundaries, naming conventions, sealed/readonly/nullable, async and CancellationToken, memory safety, and zero-warning policy.
---

# C# / .NET Style Guide for Carbon

Applies across all C# code in `analyzer/`, `contracts/`, `engine/`, `server/`, `desktop/`, and `wasm/`. This skill is the authoritative style source for the Carbon project.

## Non-negotiables

- **Layer boundaries** (strictly enforced across dependencies):
  - `contracts/`: Pure protocol, packet models, DTOs, and interface definitions. Has NO dependencies on game logic, engine, or server runtime.
  - `engine/`: Pure game simulation, ECS (entities/components/systems), math, spatial hashing, tick loop. Depends only on `contracts/`. NEVER references `server/`, `desktop/`, or `wasm/`.
  - `server/`: Dedicated game server, gateway, connection listeners, world loop host, persistence. Depends on `contracts/`, `engine/`, and server libraries.
  - `desktop/` & `wasm/`: Client application runners. Depend on `contracts/` and `engine/` (for client-side prediction/interpolation).
  - `analyzer/`: Roslyn incremental source generators and analyzers. Zero runtime dependencies.
- **Nullable reference types** stay enabled throughout the entire codebase (`<Nullable>enable</Nullable>`). Never use `#nullable disable`.
- **Zero warnings policy**: Code must build with zero errors and zero warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- **Explicit Usings**: Write explicit `using` statements (System → third-party → project). Do not rely on implicit global usings.
- **Line Endings & Formatting**: LF line endings (`.gitattributes` `eol=lf`). No trailing whitespace. Allman braces (`{` on a new line). File-scoped namespaces matching directory structure.

## Language Standards

- **Modern C#**: C# 12 / 13 on modern .NET.
- **Type Declaration**: Explicit types preferred; use `var` only when the type is obvious from the right-hand side (e.g. `var player = new Player()`).
- **Immutability & Safety**:
  - `sealed` on all classes not explicitly designed for inheritance.
  - `readonly` fields wherever possible.
  - `readonly struct` or `record struct` for small, immutable data values and math vectors.
- **Defensive Guard Clauses**: Use modern guard clauses at method boundaries:
  - `ArgumentNullException.ThrowIfNull(argument);`
  - `ArgumentOutOfRangeException.ThrowIfNegative(index);`
- **Pattern Matching**: Use `is null` / `is not null` instead of `== null` / `!= null`. Use `string.Empty` instead of `""`.
- **Async & Cancellation**:
  - Forward `CancellationToken` through every asynchronous call chain.
  - Use `ConfigureAwait(false)` in non-UI libraries (`engine/`, `contracts/`, `server/`).
  - Use `ValueTask` / `ValueTask<T>` on hot paths and methods that frequently complete synchronously.

## Naming Conventions

| Element | Convention | Example |
| :--- | :--- | :--- |
| Classes, Structs, Enums, Interfaces | PascalCase | `WorldSimulation`, `PositionComponent`, `IPacketHandler` |
| Interfaces | `I` + PascalCase | `IEntitySystem`, `IPacketConnection` |
| Methods, Properties, Constants | PascalCase | `Tick()`, `EntityCount`, `MaxPlayerCapacity` |
| Parameters, Local Variables | camelCase | `entityId`, `deltaTime`, `packetBuffer` |
| Private Instance Fields | `_camelCase` | `_connectionHub`, `_worldState` |
| Private Static Fields | `s_camelCase` | `s_emptySnapshot`, `s_defaultOptions` |
| `[ThreadStatic]` Fields | `t_camelCase` | `t_tickBuffer` |

## Packet & Message Naming

- **Client to Server (C2S)**: Verb + Noun or Action (e.g., `PlayerMoveRequest`, `CastSkillCommand`, `PingRequest`).
- **Server to Client (S2C)**: Result / Notification (e.g., `PlayerMoveResult`, `WorldSnapshotSync`, `EntitySpawnNotification`).
- **Opcodes**: Defined via strongly typed enums or constants, partitioned into distinct functional bands.

## File Organization

- One primary type per file. File name must match type name (e.g., `WorldTickLoop.cs`).
- Namespace must strictly match folder path (enforced by IDE0130).
- Split large classes into partial files (`.cs`, `.Systems.cs`, `.Handlers.cs`) only when exceeding ~700 lines.
