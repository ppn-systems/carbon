---
name: carbon-repo-navigation
description: Use to locate code, decide where a new file or project belongs, or navigate between server, engine, contracts, client runners, and docs across the 11 Carbon folders — analyzer, contracts, deploy, desktop, docs, engine, graphics, library, resources, server, wasm.
---

# Carbon Repository Navigation

Carbon is organized into 10 decoupled directories to maintain clear architectural boundaries and support both Dedicated Server and cross-platform clients.

## Directory Map & Responsibilities

```
carbon/
├── analyzer/     # Roslyn incremental analyzers & source generators
├── contracts/    # Shared networking protocols, DTOs, opcodes, and game event definitions
├── deploy/       # Docker, docker-compose, Kubernetes manifests, and CI/CD pipelines
├── desktop/      # Cross-platform native desktop client runner
├── docs/         # Architecture specifications, ADRs, execution prompts, and protocol specs
├── engine/       # Core game simulation, ECS, fixed-tick loop, spatial hashing, and collision
├── graphics/     # Client-only 2.5D rendering modules (Carbon.Graphics.*: OpenGL, batching, fonts, scenes, UI)
├── library/      # Pre-built managed assemblies and dependencies (e.g. Nalix.Observability)
├── resources/    # Game configurations, schemas, maps, tilesets, assets, and localization
├── server/       # Dedicated game server, gateway multiplexer, world simulation, and auth
└── wasm/         # WebAssembly client target for browser execution
```

## Layer Boundaries & References

- **`contracts/`**: Depends on NOTHING. Pure contracts, DTOs, packet definitions, serialization markers.
- **`engine/`**: Depends ONLY on `contracts/`. Pure simulation, ECS components and systems, spatial partitioning, deterministic math.
- **`graphics/`**: Client-only rendering (Carbon.Graphics.*, Silk.NET.OpenGL). May depend on `contracts/` and `engine/`. Never referenced by `server/`.
- **`server/`**: Depends on `contracts/`, `engine/`, and server-side transports. Never references `graphics/` or client runners.
- **`desktop/` & `wasm/`**: Depend on `contracts/`, `engine/` (for client prediction and simulation replay), and `graphics/` (rendering).
- **`analyzer/`**: Analyzers and Source Generators. Referenced as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
- **`resources/`**: Static and data-driven assets consumed at runtime (JSON schemas, tile maps, localization).
- **`library/`**: Third-party pre-compiled assemblies (read-only).

## Documentation Navigation (`docs/`)

- `docs/adr/`: **Authoritative Architecture Decision Records**. Check here first before designing new systems or changing protocols.
- `docs/execution/`: Active execution prompts drafted by `prompt-author` and executed by `prompt-execute`.
- `docs/archive/`: Historical records, completed execution logs, and superseded ADRs. Never implement from archive.

## How to Locate a Game Feature

1. **Packet / Opcode**: Look in `contracts/Packets/<Domain>/` or search `CarbonOpCode.<Name>`.
2. **Server Packet Handler**: Look in `server/Handlers/<Domain>/` by matching `[PacketOpcode(CarbonOpCode.<Name>)]`.
3. **Game Logic / ECS System**: Look in `engine/Systems/<Domain>/` (e.g., `MovementSystem`, `CombatSystem`).
4. **Entity Components**: Look in `engine/Components/<Domain>/` (struct components).
5. **Client Rendering / Input**: Look in `desktop/Input/` or `desktop/Rendering/` for Desktop; `wasm/Pages/` or `wasm/Interop/` for WebAssembly.
