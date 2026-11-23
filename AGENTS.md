# AGENTS.md — Carbon Agent Rules

> The single agent entry point for Google Antigravity, Claude Code, and autonomous AI agents.
> Detail lives in the skills under [`.claude/skills/`](.claude/skills/). Load a skill when your task touches its domain.

## Project Identity

| Field | Value |
| :--- | :--- |
| **Product** | **Carbon** — High-performance, modular C# / .NET MMORPG engine and server-client framework |
| **Targets** | Dedicated Server, Desktop Client (Silk.NET.OpenGL / Windowing), WebAssembly Client (Browser) |
| **Core Architecture** | Entity Component System (ECS), Fixed-Tick Simulation, Spatial AoI Grid, Zero-Allocation Networking |
| **Stack** | C# 12/13, .NET 10/11 preview, Nalix Observability / Realtime Core, Native AOT & Trimming |

## Directory Map

```
carbon/
├── analyzer/     # Roslyn incremental analyzers & source generators (packets, RPC, ECS)
├── contracts/    # Shared networking protocols, DTOs, messages, and game event definitions
├── deploy/       # Docker, docker-compose, Kubernetes manifests, and CI/CD pipelines
├── desktop/      # Native desktop client runner (Silk.NET.OpenGL)
├── docs/         # Architecture specifications, ADRs, protocol docs, and system design
├── engine/       # Core game simulation, ECS, fixed-tick loop, spatial hashing, and collision
├── graphics/     # Client-only 2.5D rendering modules (Carbon.Graphics.*: OpenGL, batching, fonts, scenes, UI)
├── library/      # Pre-built managed assemblies and dependencies (e.g. Nalix.Observability)
├── resources/    # Game configurations, schemas, maps, tilesets, assets, and localization
├── server/       # Dedicated game server, gateway multiplexer, world simulation, and auth
└── wasm/         # WebAssembly client target for browser execution
```

## Non-Negotiable Core Rules

1. **Never Commit to `master`**: Always create a feature branch (`feature/<name>` or `fix/<name>`) and open a Pull Request.
2. **Zero Warning Policy**: Code must compile with zero errors and zero warnings (`TreatWarningsAsErrors`).
3. **Layer Boundaries**:
   - `contracts/` depends on nothing.
   - `engine/` depends ONLY on `contracts/`.
   - `graphics/` (client rendering) depends only on `contracts/` and `engine/`; it must never be referenced by `server/`.
   - `server/` consumes `contracts/` and `engine/`.
   - `desktop/` and `wasm/` consume `contracts/`, `engine/`, and `graphics/`.
4. **No Allocations on Hot Paths**: Game tick loops and packet dispatching must be zero-allocation (struct components, `Span<T>`, `PacketFactory<T>.Acquire()`).
5. **No Blind Architecture Changes**: No new frameworks, database shifts, or breaking wire protocol changes without an Architecture Decision Record (ADR) in `docs/adr/`.
6. **No Speculative Code**: Follow Karpathy Guidelines — implement only what is requested, make surgical edits, and verify with tests.

## Skill Directory & Trigger Routing

| Task / Domain | Primary Skill | Notes |
| :--- | :--- | :--- |
| Locating code, deciding project placement across 11 folders | `carbon-repo-navigation` | Folder boundaries, feature discovery, contracts/engine layout |
| Writing documentation, ADRs in `docs/adr/`, localization | `carbon-docs-style` | Language by audience, ADR lifecycle (`Proposed`/`Accepted`) |
| Drafting / authoring a new execution prompt | `prompt-author` | Investigation first, cite `file:line`, DOD, never self-executes |
| Executing / running a numbered prompt from `docs/execution/` | `prompt-execute` | Pre-flight check, literal DOD, scoped test, commit `[exec/N]` |
| Direct bug fix or small task without a prompt | `direct-fix` | Triage, surgical fix, scoped verification, PR |
| Game engine (`engine/`), ECS components/systems, rendering (`graphics/`, Silk.NET.OpenGL) | `carbon-engine-ecs` | Unmanaged structs, fixed-tick loop, AoI spatial grid, OpenGL |
| Writing / reviewing any C# code, layer boundaries | `csharp-style` | File-scoped namespaces, nullable, explicit types, naming |
| LLM behavior, avoiding complexity & hallucinations | `karpathy-guidelines` | Simplicity first, surgical edits, verify with goals |
| Hot path optimization, Native AOT, trimming | `performance-aot` | Zero-alloc tick loop, struct components, object pooling |
| Verification before committing, PR guidelines | `review-gates` | Conventional commits, scoped build/test verification |
| Check if an action is forbidden | `do-not-do` | Fast prohibition reference table |
| Unit tests, integration tests, simulation testing | `testing-style` | Deterministic fakes, tick-stepping, FluentAssertions |
| Network transports, rate limiting, connection lifecycle | `carbon-network` | UDP/TCP/WebSocket, 0-RTT reconnect, ConnectionGuard |
| Defining or editing wire packets and DTOs | `carbon-packets` | `[SerializeOrder]`, opcodes, delta compression, pooling |
| Server-side packet handlers and dispatching | `carbon-handlers` | World tick queue, thread-safety, pooled replies, AoI |
